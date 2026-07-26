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
///
/// 写入始终遵循"先落盘、后提交内存状态"的事务式流程：任何一次启用/关闭/修改主密码，
/// 都是先把新的密文写到磁盘成功之后，才更新 <see cref="IsMasterPasswordEnabled"/>、
/// 缓存密钥等内存状态；磁盘写入本身也是原子的（先写临时文件再替换），避免进程崩溃/
/// 磁盘写入失败导致内存状态与磁盘内容不一致，或半写文件损坏账号库。
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

    /// <summary>
    /// 账号库文件的实际路径。默认是 %AppData%\PwdTool\vault.dat；构造时可传入自定义路径，
    /// 主要是为了让单元测试能够指向临时文件，而不是意外读写真实用户的账号库。
    /// </summary>
    public string VaultPath { get; }

    public bool IsMasterPasswordEnabled { get; private set; }

    public bool IsUnlocked => !IsMasterPasswordEnabled || _cachedKey != null;

    public IReadOnlyList<AccountEntry> Entries => _entries;

    public PasswordStore(string? vaultPath = null)
    {
        VaultPath = vaultPath ?? Path.Combine(AppSettings.AppDataDir, "vault.dat");
    }

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

        // 切片前先校验总长度（16 salt + 12 nonce + 16 tag，密文可以为空但这三段必须齐全）。
        // 同一 Windows 账户下的其它进程理论上也能用 DPAPI 保护出一段"合法但过短"的伪造数据，
        // 缺少这层校验会导致下面的范围切片直接抛出未被上层捕获的 ArgumentOutOfRangeException。
        const int minLength = 2 + SaltSize + NonceSize + TagSize;
        if (plain.Length < minLength)
        {
            throw new InvalidDataException("账号库文件格式无法识别（数据长度不足）。");
        }

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
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidMasterPasswordException();
        }

        _cachedKey = key;
        _cachedSalt = salt;
        DeserializeInto(Encoding.UTF8.GetString(jsonBytes));
    }

    private void DeserializeInto(string json)
    {
        var list = JsonSerializer.Deserialize(json, JsonContext.Default.ListAccountEntry) ?? new List<AccountEntry>();
        _entries.Clear();
        _entries.AddRange(list);
    }

    /// <summary>
    /// 按指定的保护方案构造要落盘的 DPAPI 密文；不读写任何实例字段，纯函数式地把
    /// "内存里的账号列表 + 目标保护方式" 转换成最终字节流，供 <see cref="Save"/> 与
    /// 启用/关闭/修改主密码时的事务式写入共用，确保"先构造好完整数据、写盘成功后再提交
    /// 状态"这条规则不会因为分散在多处而遗漏。
    /// </summary>
    private byte[] BuildProtectedBytes(bool masterPasswordEnabled, byte[]? key, byte[]? salt)
    {
        var json = JsonSerializer.Serialize(_entries, JsonContext.Default.ListAccountEntry);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        byte[] plain;
        if (!masterPasswordEnabled)
        {
            plain = new byte[2 + jsonBytes.Length];
            plain[0] = FormatVersion;
            plain[1] = FlagPlain;
            jsonBytes.CopyTo(plain, 2);
        }
        else
        {
            if (key == null || salt == null)
            {
                throw new MasterPasswordRequiredException();
            }

            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize); // 每次保存都用新的随机 nonce，绝不复用
            byte[] cipher = new byte[jsonBytes.Length];
            byte[] tag = new byte[TagSize];
            using (var aesGcm = new AesGcm(key, TagSize))
            {
                aesGcm.Encrypt(nonce, jsonBytes, cipher, tag);
            }

            plain = new byte[2 + SaltSize + NonceSize + TagSize + cipher.Length];
            plain[0] = FormatVersion;
            plain[1] = FlagMasterPassword;
            int offset = 2;
            salt.CopyTo(plain, offset); offset += SaltSize;
            nonce.CopyTo(plain, offset); offset += NonceSize;
            tag.CopyTo(plain, offset); offset += TagSize;
            cipher.CopyTo(plain, offset);
        }

        return System.Security.Cryptography.ProtectedData.Protect(
            plain, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
    }

    /// <summary>
    /// 原子写入 vault.dat：先写临时文件，再用 <see cref="File.Replace"/>（已存在时）或
    /// <see cref="File.Move"/>（首次创建时）整体替换，避免进程崩溃/断电导致半写文件损坏；
    /// <see cref="File.Replace"/> 会保留一份 <c>.bak</c> 备份，作为最后一道数据保险。
    /// </summary>
    private void WriteProtectedBytesAtomically(byte[] protectedBytes)
    {
        string? dir = Path.GetDirectoryName(VaultPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPath = VaultPath + ".tmp";
        string backupPath = VaultPath + ".bak";

        File.WriteAllBytes(tempPath, protectedBytes);

        if (File.Exists(VaultPath))
        {
            File.Replace(tempPath, VaultPath, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, VaultPath);
        }
    }

    /// <summary>将当前内存中的账号库加密落盘，使用与加载时相同的保护方案。</summary>
    public void Save()
    {
        byte[] protectedBytes = BuildProtectedBytes(IsMasterPasswordEnabled, _cachedKey, _cachedSalt);
        WriteProtectedBytesAtomically(protectedBytes);
    }

    /// <summary>开启主密码保护（此前未启用）。先落盘成功后才提交内存状态，落盘失败不会改变任何状态。</summary>
    public void EnableMasterPassword(string newPassword, AppSettings settings)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = DeriveKey(newPassword, salt);

        byte[] protectedBytes = BuildProtectedBytes(masterPasswordEnabled: true, key, salt);
        WriteProtectedBytesAtomically(protectedBytes); // 失败时直接抛出，下面的状态提交不会执行

        ZeroCachedKeyIfAny();
        _cachedKey = key;
        _cachedSalt = salt;
        IsMasterPasswordEnabled = true;

        UpdateVerifier(newPassword, settings);
    }

    /// <summary>关闭主密码保护，改回仅 DPAPI 保护。先落盘成功后才提交内存状态。</summary>
    public void DisableMasterPassword(AppSettings settings)
    {
        byte[] protectedBytes = BuildProtectedBytes(masterPasswordEnabled: false, key: null, salt: null);
        WriteProtectedBytesAtomically(protectedBytes);

        IsMasterPasswordEnabled = false;
        ZeroCachedKeyIfAny();
        _cachedKey = null;
        _cachedSalt = null;

        settings.MasterPasswordEnabled = false;
        settings.MasterPasswordVerifierProtected = null;
    }

    /// <summary>
    /// 修改已启用的主密码。
    /// </summary>
    /// <param name="oldPassword">旧密码，用于校验。</param>
    /// <param name="newPassword">新密码。</param>
    /// <param name="settings">用于更新离线校验信息。</param>
    /// <param name="oldPasswordAlreadyVerified">
    /// 调用方是否已经在此之前校验过旧密码（例如 <see cref="MasterPasswordForm"/> 交互式录入时
    /// 已经调用过一次 <see cref="VerifyMasterPassword"/> 并给出了即时反馈）。默认 false，
    /// 此时本方法仍会完整校验一次，保证 API 本身始终安全、不依赖调用方的行为；仅当调用方明确
    /// 知道刚验证过、想避免同一次操作里重复两次高成本 PBKDF2 派生时才传 true。
    /// </param>
    public void ChangeMasterPassword(string oldPassword, string newPassword, AppSettings settings,
        bool oldPasswordAlreadyVerified = false)
    {
        if (!oldPasswordAlreadyVerified && !VerifyMasterPassword(oldPassword, settings))
        {
            throw new InvalidMasterPasswordException();
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = DeriveKey(newPassword, salt);

        byte[] protectedBytes = BuildProtectedBytes(masterPasswordEnabled: true, key, salt);
        WriteProtectedBytesAtomically(protectedBytes);

        ZeroCachedKeyIfAny();
        _cachedKey = key;
        _cachedSalt = salt;

        UpdateVerifier(newPassword, settings);
    }

    /// <summary>
    /// 快速校验主密码是否正确，无需先解密整个账号库，适合登录界面即时反馈。
    /// 校验信息（盐+哈希）本身也经过 DPAPI(CurrentUser) 保护后才存进 <see cref="AppSettings"/>，
    /// 与 vault.dat 采用同等的"离线单机无法脱库破解"防护级别，而不是明文放在 settings.json 里
    /// （明文哈希会让攻击者绕开 DPAPI 的"必须同账户"防护，直接做离线暴力破解）。
    /// </summary>
    public static bool VerifyMasterPassword(string password, AppSettings settings)
    {
        if (string.IsNullOrEmpty(settings.MasterPasswordVerifierProtected))
        {
            return false;
        }

        byte[] verifierBytes;
        try
        {
            byte[] protectedBytes = Convert.FromBase64String(settings.MasterPasswordVerifierProtected);
            verifierBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }

        if (verifierBytes.Length != SaltSize + KeySize)
        {
            return false;
        }

        byte[] salt = verifierBytes[..SaltSize];
        byte[] expected = verifierBytes[SaltSize..];
        byte[] actual = DeriveKey(password, salt);

        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    /// <summary>把"盐+PBKDF2哈希"打包后整体 DPAPI 加密，写入 settings.MasterPasswordVerifierProtected。</summary>
    private static void UpdateVerifier(string password, AppSettings settings)
    {
        byte[] verifierSalt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] verifierHash = DeriveKey(password, verifierSalt);

        byte[] combined = new byte[SaltSize + KeySize];
        verifierSalt.CopyTo(combined, 0);
        verifierHash.CopyTo(combined, SaltSize);

        byte[] protectedBytes = System.Security.Cryptography.ProtectedData.Protect(
            combined, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);

        settings.MasterPasswordEnabled = true;
        settings.MasterPasswordVerifierProtected = Convert.ToBase64String(protectedBytes);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
    }

    /// <summary>若已有缓存密钥，先清零内容再丢弃引用，避免旧密钥明文残留在托管堆上等待 GC 回收。</summary>
    private void ZeroCachedKeyIfAny()
    {
        if (_cachedKey != null)
        {
            CryptographicOperations.ZeroMemory(_cachedKey);
        }
    }

    /// <summary>清空内存中缓存的密钥（例如程序退出、锁定时调用），避免密钥常驻内存。</summary>
    public void Lock()
    {
        ZeroCachedKeyIfAny();
        _cachedKey = null;
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

    /// <summary>
    /// 批量导入（用于浏览器导入），按 Url+Username 去重，已存在则跳过。
    /// 当 Url 和 Username 同时为空时不做去重判断（直接追加）——否则多条"网址/账号都缺失"
    /// 的记录（例如浏览器导出数据缺列）会因为空字符串互相相等而被误判为重复，导致除第一条外
    /// 全部被静默丢弃。
    /// </summary>
    public int ImportMany(IEnumerable<AccountEntry> imported)
    {
        int added = 0;
        foreach (var entry in imported)
        {
            bool bothEmpty = string.IsNullOrEmpty(entry.Url) && string.IsNullOrEmpty(entry.Username);
            bool exists = !bothEmpty && _entries.Any(e =>
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

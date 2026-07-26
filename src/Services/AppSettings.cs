using System.Text.Json;
using System.Text.Json.Serialization;

namespace PwdTool.Services;

/// <summary>设置文件存在但无法解析/读取（已损坏、被截断、权限异常等），区别于"文件不存在"这一正常首次运行场景。</summary>
public class SettingsCorruptedException : Exception
{
    public SettingsCorruptedException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// 非敏感设置项，明文 JSON 存储在 %AppData%\PwdTool\settings.json。
/// 敏感的账号库另见 <see cref="PasswordStore"/>（DPAPI/主密码加密）。
/// </summary>
public class AppSettings
{
    /// <summary>展示框宽度（像素，物理像素，PerMonitorV2 下与 Cursor.Position 同一坐标系）。</summary>
    public int PopupWidth { get; set; } = 360;

    /// <summary>展示框高度（像素）。</summary>
    public int PopupHeight { get; set; } = 320;

    /// <summary>展示框背景色，ARGB int（System.Drawing.Color.ToArgb()）。</summary>
    public int PopupBackColorArgb { get; set; } = System.Drawing.Color.White.ToArgb();

    /// <summary>展示框强调色（选中项高亮等），ARGB int。</summary>
    public int PopupAccentColorArgb { get; set; } = System.Drawing.Color.FromArgb(0, 120, 215).ToArgb();

    /// <summary>
    /// 选中一条账号后是否"主动填写"到原窗口（SendInput 回填）；
    /// 关闭时改为仅复制密码到剪贴板（更安全但需手动粘贴）。
    /// </summary>
    public bool AutoFillEnabled { get; set; } = true;

    /// <summary>
    /// 是否开机自启。这个字段只是"上次保存时的意图"缓存，程序启动时会用
    /// <see cref="AutoStartManager.IsEnabled"/> 查询注册表的实际状态来校正它——
    /// 避免通过安装程序勾选自启、或手动编辑注册表之后，这里显示的状态与真实行为不一致。
    /// </summary>
    public bool AutoStartEnabled { get; set; }

    /// <summary>全局热键的修饰键位掩码（Native.MOD_*，不含 MOD_NOREPEAT）。</summary>
    public uint HotkeyModifiers { get; set; } = Native.MOD_CONTROL | Native.MOD_ALT;

    /// <summary>全局热键的虚拟键码。默认 P 键。</summary>
    public uint HotkeyKeyCode { get; set; } = (uint)System.Windows.Forms.Keys.P;

    /// <summary>是否启用"主密码"二次加密账号库。</summary>
    public bool MasterPasswordEnabled { get; set; }

    /// <summary>
    /// 主密码离线校验信息（盐+PBKDF2哈希，打包后整体经 DPAPI(CurrentUser) 加密，Base64 存储），
    /// 用于在不解密整个账号库的前提下快速校验用户输入的主密码是否正确。
    /// 之所以要 DPAPI 加密而不是明文存这段校验哈希，是因为明文哈希会让攻击者只要拿到
    /// settings.json 就能离线暴力破解主密码，绕开 DPAPI"必须同一 Windows 账户"这层防护——
    /// 加密后它与 vault.dat 享有同等的防护级别。这段信息只是"快速校验"用的便利缓存，
    /// 并非解锁账号库所必需（真正解锁靠 vault.dat 自身携带的盐值 + 用户输入的主密码）。
    /// </summary>
    public string? MasterPasswordVerifierProtected { get; set; }

    /// <summary>展示框弹出时用于展示的最近/常用条目数量，需求固定为 5，但保留为设置项以便调整。</summary>
    public int PopupItemCount { get; set; } = 5;

    [JsonIgnore]
    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PwdTool");

    [JsonIgnore]
    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    /// <summary>
    /// 从磁盘加载设置。文件不存在（首次运行）时返回默认设置，这是正常场景；
    /// 文件存在但无法解析/读取（已损坏、权限异常等）时抛出 <see cref="SettingsCorruptedException"/>，
    /// 由调用方决定如何提示用户——不应该像早期实现那样用空 catch 静默重置，那样会在用户
    /// 毫无察觉的情况下悄悄清空主密码校验信息等关键字段。
    /// </summary>
    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize(json, JsonContext.Default.AppSettings);
            return settings ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new SettingsCorruptedException(
                "设置文件已损坏或无法读取，将改用默认设置：热键/展示框外观等自定义项会丢失；" +
                "如果之前开启过主密码，其离线校验信息也会一并丢失（不影响账号库本身，仍可用原主密码" +
                "正常解锁，只是\"修改主密码\"需要先关闭再重新开启主密码来恢复）。", ex);
        }
    }

    /// <summary>
    /// 保存设置到磁盘（明文 JSON，主密码校验哈希已经过 DPAPI 加密，不含任何明文密码）。
    /// 采用"先写临时文件、再整体替换"的原子写入方式，避免进程崩溃/断电导致半写文件损坏。
    /// </summary>
    public void Save()
    {
        Directory.CreateDirectory(AppDataDir);
        var json = JsonSerializer.Serialize(this, JsonContext.Default.AppSettings);

        string tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(SettingsPath))
        {
            File.Replace(tempPath, SettingsPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, SettingsPath);
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace PwdTool.Services;

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

    /// <summary>是否开机自启（与注册表 Run 项保持同步，见 <see cref="AutoStartManager"/>）。</summary>
    public bool AutoStartEnabled { get; set; }

    /// <summary>全局热键的修饰键位掩码（Native.MOD_*，不含 MOD_NOREPEAT）。</summary>
    public uint HotkeyModifiers { get; set; } = Native.MOD_CONTROL | Native.MOD_ALT;

    /// <summary>全局热键的虚拟键码。默认 P 键。</summary>
    public uint HotkeyKeyCode { get; set; } = (uint)System.Windows.Forms.Keys.P;

    /// <summary>是否启用"主密码"二次加密账号库。</summary>
    public bool MasterPasswordEnabled { get; set; }

    /// <summary>
    /// 主密码校验用盐值（Base64），与账号库加密用的盐值分开存储，
    /// 用于在不解密整个账号库的前提下快速校验用户输入的主密码是否正确。
    /// </summary>
    public string? MasterPasswordVerifierSalt { get; set; }

    /// <summary>主密码校验哈希（Base64，PBKDF2 派生结果）。</summary>
    public string? MasterPasswordVerifierHash { get; set; }

    /// <summary>展示框弹出时用于展示的最近/常用条目数量，需求固定为 5，但保留为设置项以便调整。</summary>
    public int PopupItemCount { get; set; } = 5;

    [JsonIgnore]
    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PwdTool");

    [JsonIgnore]
    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>从磁盘加载设置；文件不存在或损坏时返回默认设置。</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings ?? new AppSettings();
        }
        catch
        {
            // 设置文件损坏时不阻塞启动，退回默认设置。
            return new AppSettings();
        }
    }

    /// <summary>保存设置到磁盘（明文 JSON，不含任何密码）。</summary>
    public void Save()
    {
        Directory.CreateDirectory(AppDataDir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}

using Microsoft.Win32;

namespace PwdTool.Services;

/// <summary>
/// 开机自启管理：写入/移除 HKCU\Software\Microsoft\Windows\CurrentVersion\Run。
/// 使用 HKCU（当前用户）而非 HKLM，因此无需管理员权限。
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PwdTool";

    /// <summary>查询注册表中是否已配置自启（不代表未被"任务管理器 > 启动"里手动禁用）。</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return !string.IsNullOrEmpty(value);
    }

    /// <summary>启用或关闭开机自启。</summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key == null) return;

        if (enabled)
        {
            // 单文件发布(self-contained/单文件)下 Assembly.Location 会返回空字符串，
            // 必须用 Environment.ProcessPath 取得真正的可执行文件路径（普通进程下恒不为 null）。
            string exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法获取当前可执行文件路径。");
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}

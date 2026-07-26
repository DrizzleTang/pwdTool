using System.Runtime.InteropServices;

namespace PwdTool.Services;

/// <summary>
/// 自动填写服务：把账号/密码通过 SendInput 模拟键盘输入到当前前台窗口。
///
/// 使用 KEYEVENTF_UNICODE 逐字符注入而非 SendKeys，原因见计划中的技术研究结论：
/// SendKeys 依赖当前键盘布局、对特殊字符需要转义、无法可靠输入中文或非 ASCII 密码；
/// SendInput + KEYEVENTF_UNICODE 直接按 Unicode 码位注入，与布局无关，更稳定。
///
/// 限制：受 Windows UIPI（User Interface Privilege Isolation）管制，无法向以管理员
/// 权限运行、完整性级别更高的窗口注入输入，且这种失败是"静默"的（不会抛异常）。
/// 遇到这种情况，调用方应回退为"复制密码到剪贴板"。
/// </summary>
public static class AutoTypeService
{
    /// <summary>在弹窗显示前调用，记下当前前台窗口，用于稍后把输入送回原程序。</summary>
    public static IntPtr CaptureForegroundWindow() => Native.GetForegroundWindow();

    /// <summary>尝试把目标窗口重新置为前台。</summary>
    public static bool ActivateWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd))
        {
            return false;
        }

        return Native.SetForegroundWindow(hWnd);
    }

    /// <summary>
    /// 依次输入：用户名 → Tab → 密码 → （可选）回车。
    /// </summary>
    public static void TypeCredential(string username, string password, bool pressEnterAfter = false)
    {
        var inputs = new List<Native.INPUT>();
        AppendText(inputs, username);
        AppendKey(inputs, Native.VK_TAB);
        AppendText(inputs, password);
        if (pressEnterAfter)
        {
            AppendKey(inputs, Native.VK_RETURN);
        }

        SendAll(inputs);
    }

    /// <summary>仅输入一段文本（例如只填密码）。</summary>
    public static void TypeText(string text)
    {
        var inputs = new List<Native.INPUT>();
        AppendText(inputs, text);
        SendAll(inputs);
    }

    private static void AppendText(List<Native.INPUT> inputs, string text)
    {
        foreach (char c in text)
        {
            inputs.Add(MakeUnicodeInput(c, keyUp: false));
            inputs.Add(MakeUnicodeInput(c, keyUp: true));
        }
    }

    private static void AppendKey(List<Native.INPUT> inputs, ushort vk)
    {
        inputs.Add(MakeVkInput(vk, keyUp: false));
        inputs.Add(MakeVkInput(vk, keyUp: true));
    }

    private static Native.INPUT MakeUnicodeInput(char c, bool keyUp)
    {
        return new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            U = new Native.InputUnion
            {
                ki = new Native.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = (uint)(Native.KEYEVENTF_UNICODE | (keyUp ? Native.KEYEVENTF_KEYUP : 0)),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
    }

    private static Native.INPUT MakeVkInput(ushort vk, bool keyUp)
    {
        return new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            U = new Native.InputUnion
            {
                ki = new Native.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = (uint)(keyUp ? Native.KEYEVENTF_KEYUP : 0),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
    }

    private static void SendAll(List<Native.INPUT> inputs)
    {
        if (inputs.Count == 0) return;

        var arr = inputs.ToArray();
        int size = Marshal.SizeOf(typeof(Native.INPUT));
        Native.SendInput((uint)arr.Length, arr, size);
    }
}

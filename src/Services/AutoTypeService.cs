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
    /// 二次校验：调用方在 <see cref="ActivateWindow"/> 之后、真正注入按键之前应该用这个方法
    /// 确认目标窗口"确实"已经拿到前台焦点（SetForegroundWindow 的返回值并不足以保证这一点，
    /// 系统前台锁定/目标进程无响应/其它窗口抢占焦点等情况都可能导致实际前台窗口与目标不符）。
    /// 校验不通过时调用方应放弃自动填写，回退为复制密码到剪贴板，避免把密码误注入到
    /// 用户当时实际正在操作的其它窗口。
    /// </summary>
    public static bool IsForeground(IntPtr hWnd) => hWnd != IntPtr.Zero && Native.GetForegroundWindow() == hWnd;

    /// <summary>
    /// 依次输入：用户名 → Tab → 密码 → （可选）回车。返回 SendInput 是否把全部按键事件都成功
    /// 提交给系统（并不代表目标应用一定"看到"了这些按键，但至少排除了 cbSize 不匹配等
    /// 会导致系统层面直接拒绝的情况）。
    /// </summary>
    public static bool TypeCredential(string username, string password, bool pressEnterAfter = false)
    {
        var inputs = new List<Native.INPUT>();
        AppendText(inputs, username);
        AppendKey(inputs, Native.VK_TAB);
        AppendText(inputs, password);
        if (pressEnterAfter)
        {
            AppendKey(inputs, Native.VK_RETURN);
        }

        return SendAll(inputs);
    }

    /// <summary>仅输入一段文本（例如只填密码）。返回是否成功提交给系统。</summary>
    public static bool TypeText(string text)
    {
        var inputs = new List<Native.INPUT>();
        AppendText(inputs, text);
        return SendAll(inputs);
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

    /// <summary>INPUT 结构体大小只与进程位数有关，不会随调用变化，缓存一次即可。</summary>
    private static readonly int InputSize = Marshal.SizeOf<Native.INPUT>();

    /// <summary>
    /// 发送全部输入事件。SendInput 的返回值是"系统实际接受了多少个事件"，
    /// 与请求数量不符（包括返回 0，例如 cbSize 与系统认知不一致、或注入被 UIPI 拒绝）
    /// 都视为失败，调用方应据此回退为复制到剪贴板，而不是想当然地认为已经成功。
    /// </summary>
    private static bool SendAll(List<Native.INPUT> inputs)
    {
        if (inputs.Count == 0) return true;

        var arr = inputs.ToArray();
        uint sent = Native.SendInput((uint)arr.Length, arr, InputSize);
        return sent == arr.Length;
    }
}

using System.Runtime.InteropServices;

namespace PwdTool.Services;

/// <summary>
/// 集中存放本项目用到的所有 Win32 P/Invoke 声明与常量，
/// 避免同一个签名在多处重复声明导致不一致。
/// </summary>
internal static class Native
{
    #region 热键 (RegisterHotKey)

    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    /// <summary>防止长按物理键盘时重复触发 WM_HOTKEY (Win7+)。</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    /// <summary>热键已被其它程序占用时 GetLastError 返回的错误码。</summary>
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    #endregion

    #region 前台窗口 / 输入注入 (SendInput)

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public const int INPUT_KEYBOARD = 1;
    public const ushort KEYEVENTF_KEYUP = 0x0002;
    public const ushort KEYEVENTF_UNICODE = 0x0004;

    public const ushort VK_TAB = 0x09;
    public const ushort VK_RETURN = 0x0D;

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    #endregion

    #region 无边框弹窗样式

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    #endregion
}

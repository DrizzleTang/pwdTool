using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PwdTool.Services;

/// <summary>
/// 全局热键管理器：用一个不可见的消息窗口（NativeWindow，不是 Form，不会有任何 UI 痕迹）
/// 承载 RegisterHotKey/WM_HOTKEY，供托盘常驻程序在没有主窗体时也能接收热键消息。
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    /// <summary>本程序内热键的固定 id（进程内唯一即可，不与其它程序冲突，因为 RegisterHotKey 的 id 是按窗口句柄区分的）。</summary>
    private const int HotkeyId = 0xA100;

    private bool _registered;

    public event Action? HotkeyPressed;

    public HotkeyManager()
    {
        // 创建一个没有窗口样式、不可见的消息窗口，仅用来接收 WM_HOTKEY。
        CreateHandle(new CreateParams());
    }

    /// <summary>
    /// 注册全局热键。会先注销之前注册的热键（若有），因此可直接用新的组合调用来"改键"。
    /// </summary>
    /// <param name="modifiers">Native.MOD_* 组合（不含 MOD_NOREPEAT，内部会自动加上）。</param>
    /// <param name="vk">虚拟键码。</param>
    /// <param name="win32Error">失败时的 Win32 错误码，1409 (ERROR_HOTKEY_ALREADY_REGISTERED) 表示与其它程序冲突。</param>
    public bool Register(uint modifiers, uint vk, out int win32Error)
    {
        UnregisterInternal();

        bool ok = Native.RegisterHotKey(Handle, HotkeyId, modifiers | Native.MOD_NOREPEAT, vk);
        win32Error = ok ? 0 : Marshal.GetLastWin32Error();
        _registered = ok;
        return ok;
    }

    public void Unregister() => UnregisterInternal();

    private void UnregisterInternal()
    {
        if (_registered)
        {
            Native.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterInternal();
        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }
}

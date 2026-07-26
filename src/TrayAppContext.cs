using System.Drawing;
using System.Windows.Forms;
using PwdTool.Forms;
using PwdTool.Models;
using PwdTool.Services;

namespace PwdTool;

/// <summary>
/// 托盘常驻应用上下文：承载 NotifyIcon、全局热键、弹窗与设置窗口的生命周期。
/// 不使用主窗体（Application.Run(mainForm)），避免任务栏出现多余窗口。
/// </summary>
public class TrayAppContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly PasswordStore _store;
    private readonly HotkeyManager _hotkeyManager;
    private readonly PopupForm _popupForm;
    private readonly NotifyIcon _trayIcon;
    private SettingsForm? _settingsForm;

    /// <summary>
    /// 尝试创建托盘上下文：加载设置与账号库，若账号库启用了主密码则弹出解锁窗口。
    /// 用户取消解锁或账号库损坏时返回 null，调用方应直接退出程序。
    /// </summary>
    public static TrayAppContext? TryCreate()
    {
        AppSettings settings;
        try
        {
            settings = AppSettings.Load();
        }
        catch (SettingsCorruptedException ex)
        {
            MessageBox.Show(ex.Message, "设置文件异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            settings = new AppSettings();
        }

        // 开机自启的真实状态以注册表为准，而不是只信任上次保存的 settings.json——
        // 例如通过安装程序的"开机自启动"选项勾选、或手动编辑过注册表，都会导致
        // settings.json 里的缓存值与实际状态不一致，这里启动时校正一次。
        settings.AutoStartEnabled = AutoStartManager.IsEnabled();

        var store = new PasswordStore();

        if (!Unlock(store, settings))
        {
            return null;
        }

        return new TrayAppContext(settings, store);
    }

    private static bool Unlock(PasswordStore store, AppSettings settings)
    {
        try
        {
            store.Load();
            return true;
        }
        catch (MasterPasswordRequiredException)
        {
            using var dlg = new MasterPasswordForm(MasterPasswordMode.Unlock, password =>
            {
                try
                {
                    store.Load(password);
                    return true;
                }
                catch (InvalidMasterPasswordException)
                {
                    return false;
                }
            });

            return dlg.ShowDialog() == DialogResult.OK;
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show("加载账号库失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private TrayAppContext(AppSettings settings, PasswordStore store)
    {
        _settings = settings;
        _store = store;
        _hotkeyManager = new HotkeyManager();

        _popupForm = new PopupForm(_store, _settings);
        _popupForm.EntryChosen += PopupForm_EntryChosen;
        _hotkeyManager.HotkeyPressed += () => _popupForm.ShowAtCursor();

        _trayIcon = new NotifyIcon
        {
            Icon = AppIconProvider.Load(),
            Text = "PwdTool 密码管理",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(),
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        RegisterHotkeyOrWarn();

        ThreadExit += (_, _) => Cleanup();
    }

    private void RegisterHotkeyOrWarn()
    {
        bool ok = _hotkeyManager.Register(_settings.HotkeyModifiers, _settings.HotkeyKeyCode, out int win32Error);
        if (!ok)
        {
            _trayIcon.ShowBalloonTip(3000, "PwdTool",
                $"默认快捷键被其它程序占用（错误码 {win32Error}），请在设置中重新指定。", ToolTipIcon.Warning);
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var itemShowPopup = new ToolStripMenuItem("显示密码框", null, (_, _) => _popupForm.ShowAtCursor());
        var itemSettings = new ToolStripMenuItem("设置…", null, (_, _) => ShowSettings());

        var itemAutoStart = new ToolStripMenuItem("开机自启动") { CheckOnClick = true, Checked = _settings.AutoStartEnabled };
        itemAutoStart.Click += (_, _) =>
        {
            _settings.AutoStartEnabled = itemAutoStart.Checked;
            AutoStartManager.SetEnabled(itemAutoStart.Checked);
            _settings.Save();
        };

        var itemExit = new ToolStripMenuItem("退出", null, (_, _) => ExitApp());

        menu.Items.Add(itemShowPopup);
        menu.Items.Add(itemSettings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(itemAutoStart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(itemExit);
        return menu;
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_store, _settings, _hotkeyManager, OnSettingsApplied);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    /// <summary>设置保存后回调：让弹窗立即应用新的尺寸/颜色，无需重启程序。</summary>
    private void OnSettingsApplied()
    {
        _popupForm.ApplySettingsAppearance();
    }

    private void PopupForm_EntryChosen(AccountEntry entry, bool didAutoFill)
    {
        if (!didAutoFill)
        {
            _trayIcon.ShowBalloonTip(2000, "PwdTool", $"已复制「{entry.DisplayName}」的密码到剪贴板。", ToolTipIcon.Info);
        }
    }

    private void ExitApp()
    {
        ExitThread(); // 触发 ApplicationContext.ThreadExit -> Cleanup()
    }

    private bool _cleanedUp;

    /// <summary>
    /// 幂等：目前唯一触发路径是托盘菜单"退出"，但 NotifyIcon.Dispose() 本身没有重复调用保护，
    /// 一旦未来增加新的退出入口（例如系统会话结束事件）导致这里被调用两次，会有抛异常风险，
    /// 加一个幂等标志位作为低成本的防御性加固。
    /// </summary>
    private void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;

        _hotkeyManager.Unregister();
        _hotkeyManager.Dispose();
        _store.Lock();

        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        _popupForm.Dispose();
        _settingsForm?.Dispose();
    }
}

using System.Drawing;
using System.Windows.Forms;
using PwdTool.Models;
using PwdTool.Services;

namespace PwdTool.Forms;

/// <summary>
/// 设置窗口：账号管理（含浏览器/CSV 导入）、展示框外观、常规（主动填写/开机自启/快捷键）、安全（主密码）。
/// </summary>
public class SettingsForm : Form
{
    private readonly PasswordStore _store;
    private readonly AppSettings _settings;
    private readonly HotkeyManager _hotkeyManager;
    private readonly Action _onSettingsApplied;

    // 账号管理
    private ListView _listView = null!;

    // 外观
    private NumericUpDown _numWidth = null!;
    private NumericUpDown _numHeight = null!;
    private Panel _backColorPreview = null!;
    private Panel _accentColorPreview = null!;
    private Color _pendingBackColor;
    private Color _pendingAccentColor;

    // 常规
    private CheckBox _chkAutoFill = null!;
    private CheckBox _chkAutoStart = null!;
    private TextBox _txtHotkey = null!;
    private Label _lblHotkeyError = null!;
    private uint _pendingModifiers;
    private uint _pendingKeyCode;

    // 安全
    private CheckBox _chkMasterPassword = null!;
    private Button _btnChangeMasterPassword = null!;

    /// <summary>录制热键时会立即注册以测试冲突（见 TxtHotkey_KeyDown），因此热键在"保存"前就已生效于
    /// 当前运行的 HotkeyManager。若用户改了但未点保存就关闭窗口，需要在关闭时把热键还原为
    /// AppSettings 里实际持久化的组合，避免本次会话的实际热键与 settings.json 不一致。</summary>
    private bool _saved;

    public SettingsForm(PasswordStore store, AppSettings settings, HotkeyManager hotkeyManager, Action onSettingsApplied)
    {
        _store = store;
        _settings = settings;
        _hotkeyManager = hotkeyManager;
        _onSettingsApplied = onSettingsApplied;

        _pendingBackColor = Color.FromArgb(settings.PopupBackColorArgb);
        _pendingAccentColor = Color.FromArgb(settings.PopupAccentColorArgb);
        _pendingModifiers = settings.HotkeyModifiers;
        _pendingKeyCode = settings.HotkeyKeyCode;

        BuildUi();
        ReloadAccountList();
    }

    private void BuildUi()
    {
        Text = "PwdTool 设置";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 480);
        MinimumSize = new Size(560, 480);
        ShowInTaskbar = true;
        Icon = SystemIcons.Application;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildAccountsTab());
        tabs.TabPages.Add(BuildAppearanceTab());
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildSecurityTab());

        var btnSave = new Button { Text = "保存设置", Width = 100 };
        var btnClose = new Button { Text = "关闭", Width = 100 };
        btnSave.Click += BtnSave_Click;
        btnClose.Click += (_, _) => Close();

        var bottomPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(8),
        };
        bottomPanel.Controls.Add(btnClose);
        bottomPanel.Controls.Add(btnSave);

        Controls.Add(tabs);
        Controls.Add(bottomPanel);

        FormClosing += SettingsForm_FormClosing;
    }

    private void SettingsForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_saved) return;

        // 关闭前若热键测试值与已持久化的设置不同，把实际生效的全局热键还原回持久化的组合。
        if (_pendingModifiers != _settings.HotkeyModifiers || _pendingKeyCode != _settings.HotkeyKeyCode)
        {
            _hotkeyManager.Register(_settings.HotkeyModifiers, _settings.HotkeyKeyCode, out _);
        }
    }

    #region 账号管理

    private TabPage BuildAccountsTab()
    {
        var page = new TabPage("账号管理");

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
        };
        _listView.Columns.Add("标题", 110);
        _listView.Columns.Add("账号", 110);
        _listView.Columns.Add("网址", 130);
        _listView.Columns.Add("标签", 110);
        _listView.Columns.Add("来源", 70);
        _listView.DoubleClick += (_, _) => EditSelected();

        var btnAdd = new Button { Text = "新增", Width = 80 };
        var btnEdit = new Button { Text = "编辑", Width = 80 };
        var btnDelete = new Button { Text = "删除", Width = 80 };
        var btnCopyPwd = new Button { Text = "复制密码", Width = 90 };
        var btnImportChrome = new Button { Text = "从 Chrome 导入", Width = 120 };
        var btnImportEdge = new Button { Text = "从 Edge 导入", Width = 110 };
        var btnImportCsv = new Button { Text = "从 CSV 导入…", Width = 110 };

        btnAdd.Click += (_, _) => AddEntry();
        btnEdit.Click += (_, _) => EditSelected();
        btnDelete.Click += (_, _) => DeleteSelected();
        btnCopyPwd.Click += (_, _) => CopySelectedPassword();
        btnImportChrome.Click += (_, _) => ImportFromBrowser(ChromiumBrowser.Chrome);
        btnImportEdge.Click += (_, _) => ImportFromBrowser(ChromiumBrowser.Edge);
        btnImportCsv.Click += (_, _) => ImportFromCsv();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 76,
            Padding = new Padding(4),
        };
        toolbar.Controls.Add(btnAdd);
        toolbar.Controls.Add(btnEdit);
        toolbar.Controls.Add(btnDelete);
        toolbar.Controls.Add(btnCopyPwd);

        var importLabel = new Label
        {
            Text = "浏览器导入（注：新版 Chrome/Edge 因 App-Bound Encryption 只能导入旧版密码，建议同时用 CSV 导入补全）：",
            AutoSize = false,
            Width = 540,
            Height = 32,
        };
        toolbar.Controls.Add(importLabel);
        toolbar.Controls.Add(btnImportChrome);
        toolbar.Controls.Add(btnImportEdge);
        toolbar.Controls.Add(btnImportCsv);

        page.Controls.Add(_listView);
        page.Controls.Add(toolbar);
        return page;
    }

    private void ReloadAccountList()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var entry in _store.Entries)
        {
            var item = new ListViewItem(entry.Title);
            item.SubItems.Add(entry.Username);
            item.SubItems.Add(entry.Url);
            item.SubItems.Add(entry.TagsDisplay);
            item.SubItems.Add(entry.Source);
            item.Tag = entry;
            _listView.Items.Add(item);
        }
        _listView.EndUpdate();
    }

    private AccountEntry? GetSelectedEntry()
    {
        if (_listView.SelectedItems.Count == 0) return null;
        return _listView.SelectedItems[0].Tag as AccountEntry;
    }

    private void AddEntry()
    {
        using var dlg = new EditEntryForm();
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _store.Add(dlg.Result);
            ReloadAccountList();
        }
    }

    private void EditSelected()
    {
        var entry = GetSelectedEntry();
        if (entry == null) return;

        using var dlg = new EditEntryForm(entry);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _store.Update(dlg.Result);
            ReloadAccountList();
        }
    }

    private void DeleteSelected()
    {
        var entry = GetSelectedEntry();
        if (entry == null) return;

        var confirm = MessageBox.Show(this, $"确定删除「{entry.DisplayName}」吗？", "确认删除",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm == DialogResult.Yes)
        {
            _store.Delete(entry.Id);
            ReloadAccountList();
        }
    }

    private void CopySelectedPassword()
    {
        var entry = GetSelectedEntry();
        if (entry == null) return;
        Clipboard.SetText(entry.Password);
    }

    private void ImportFromBrowser(ChromiumBrowser browser)
    {
        var result = BrowserImporter.ImportFromChromium(browser);
        int added = _store.ImportMany(result.Entries);
        ReloadAccountList();

        string msg = $"成功导入 {added} 条账号。";
        if (result.SkippedNewFormat > 0)
        {
            msg += $"\n另有 {result.SkippedNewFormat} 条使用新版 App-Bound Encryption 加密，无法直接解密，" +
                   "请在浏览器中导出密码 CSV 后使用「从 CSV 导入」补全。";
        }
        if (!string.IsNullOrEmpty(result.Error))
        {
            msg += $"\n提示：{result.Error}";
        }

        MessageBox.Show(this, msg, "导入结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ImportFromCsv()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择浏览器导出的密码 CSV 文件",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var result = BrowserImporter.ImportFromCsv(dlg.FileName);
        if (!string.IsNullOrEmpty(result.Error) && result.Entries.Count == 0)
        {
            MessageBox.Show(this, "导入失败：" + result.Error, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        int added = _store.ImportMany(result.Entries);
        ReloadAccountList();
        MessageBox.Show(this, $"成功导入 {added} 条账号。", "导入结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    #endregion

    #region 外观

    private TabPage BuildAppearanceTab()
    {
        var page = new TabPage("展示框外观");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(16),
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _numWidth = new NumericUpDown { Minimum = 240, Maximum = 800, Value = _settings.PopupWidth, Width = 100 };
        _numHeight = new NumericUpDown { Minimum = 200, Maximum = 700, Value = _settings.PopupHeight, Width = 100 };

        layout.Controls.Add(new Label { Text = "展示框宽度：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(_numWidth, 1, 0);

        layout.Controls.Add(new Label { Text = "展示框高度：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
        layout.Controls.Add(_numHeight, 1, 1);

        _backColorPreview = new Panel { Width = 60, Height = 24, BackColor = _pendingBackColor, BorderStyle = BorderStyle.FixedSingle };
        var btnBackColor = new Button { Text = "选择背景色…", AutoSize = true };
        btnBackColor.Click += (_, _) =>
        {
            using var cd = new ColorDialog { Color = _pendingBackColor };
            if (cd.ShowDialog(this) == DialogResult.OK)
            {
                _pendingBackColor = cd.Color;
                _backColorPreview.BackColor = cd.Color;
            }
        };
        layout.Controls.Add(new Label { Text = "背景颜色：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 2);
        layout.Controls.Add(_backColorPreview, 1, 2);
        layout.Controls.Add(btnBackColor, 2, 2);

        _accentColorPreview = new Panel { Width = 60, Height = 24, BackColor = _pendingAccentColor, BorderStyle = BorderStyle.FixedSingle };
        var btnAccentColor = new Button { Text = "选择强调色…", AutoSize = true };
        btnAccentColor.Click += (_, _) =>
        {
            using var cd = new ColorDialog { Color = _pendingAccentColor };
            if (cd.ShowDialog(this) == DialogResult.OK)
            {
                _pendingAccentColor = cd.Color;
                _accentColorPreview.BackColor = cd.Color;
            }
        };
        layout.Controls.Add(new Label { Text = "选中项高亮色：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 3);
        layout.Controls.Add(_accentColorPreview, 1, 3);
        layout.Controls.Add(btnAccentColor, 2, 3);

        page.Controls.Add(layout);
        return page;
    }

    #endregion

    #region 常规

    private TabPage BuildGeneralTab()
    {
        var page = new TabPage("常规");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16),
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _chkAutoFill = new CheckBox
        {
            Text = "选中账号后主动填写到原窗口（关闭则改为复制密码到剪贴板）",
            Checked = _settings.AutoFillEnabled,
            AutoSize = true,
        };
        layout.Controls.Add(_chkAutoFill, 0, 0);
        layout.SetColumnSpan(_chkAutoFill, 2);

        _chkAutoStart = new CheckBox
        {
            Text = "开机自启动",
            Checked = _settings.AutoStartEnabled,
            AutoSize = true,
        };
        layout.Controls.Add(_chkAutoStart, 0, 1);
        layout.SetColumnSpan(_chkAutoStart, 2);

        layout.Controls.Add(new Label { Text = "全局快捷键：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 2);
        _txtHotkey = new TextBox
        {
            ReadOnly = true,
            Width = 200,
            Text = FormatHotkey(_pendingModifiers, _pendingKeyCode),
        };
        _txtHotkey.KeyDown += TxtHotkey_KeyDown;
        layout.Controls.Add(_txtHotkey, 1, 2);

        _lblHotkeyError = new Label { ForeColor = Color.Firebrick, AutoSize = true };
        layout.Controls.Add(new Label(), 0, 3);
        layout.Controls.Add(_lblHotkeyError, 1, 3);

        var hint = new Label
        {
            Text = "点击上方快捷键输入框后按下新的组合键（需包含 Ctrl/Alt/Shift 之一）即可修改。",
            AutoSize = true,
            ForeColor = Color.DimGray,
        };
        layout.Controls.Add(new Label(), 0, 4);
        layout.Controls.Add(hint, 1, 4);

        page.Controls.Add(layout);
        return page;
    }

    private void TxtHotkey_KeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true; // 阻止字符输入到文本框，我们只关心组合键

        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            return; // 仅按下修饰键时先不处理，等待非修饰键
        }

        uint modifiers = 0;
        if (e.Control) modifiers |= Native.MOD_CONTROL;
        if (e.Alt) modifiers |= Native.MOD_ALT;
        if (e.Shift) modifiers |= Native.MOD_SHIFT;

        if (modifiers == 0)
        {
            _lblHotkeyError.Text = "请至少包含 Ctrl / Alt / Shift 中的一个。";
            return;
        }

        uint vk = (uint)(e.KeyCode & Keys.KeyCode);

        // 立即尝试注册以验证是否冲突；失败则回退到原有热键，保证程序始终可用。
        bool ok = _hotkeyManager.Register(modifiers, vk, out int win32Error);
        if (!ok)
        {
            _lblHotkeyError.Text = win32Error == Native.ERROR_HOTKEY_ALREADY_REGISTERED
                ? "该快捷键已被其它程序占用，请更换组合。"
                : $"注册热键失败（错误码 {win32Error}）。";
            _hotkeyManager.Register(_pendingModifiers, _pendingKeyCode, out _);
            return;
        }

        _pendingModifiers = modifiers;
        _pendingKeyCode = vk;
        _saved = false; // 热键已变更且尚未保存，关闭窗口时需还原为持久化的组合
        _lblHotkeyError.Text = string.Empty;
        _txtHotkey.Text = FormatHotkey(modifiers, vk);
    }

    private static string FormatHotkey(uint modifiers, uint vk)
    {
        var parts = new List<string>();
        if ((modifiers & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & Native.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & Native.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(((Keys)vk).ToString());
        return string.Join("+", parts);
    }

    #endregion

    #region 安全（主密码）

    private TabPage BuildSecurityTab()
    {
        var page = new TabPage("安全");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(16),
            AutoSize = true,
        };

        _chkMasterPassword = new CheckBox
        {
            Text = "启用主密码保护（在系统 DPAPI 之外再加一层加密，每次启动需输入）",
            Checked = _store.IsMasterPasswordEnabled,
            AutoSize = true,
        };
        _chkMasterPassword.CheckedChanged += ChkMasterPassword_CheckedChanged;
        layout.Controls.Add(_chkMasterPassword);

        _btnChangeMasterPassword = new Button
        {
            Text = "修改主密码…",
            AutoSize = true,
            Enabled = _store.IsMasterPasswordEnabled,
        };
        _btnChangeMasterPassword.Click += BtnChangeMasterPassword_Click;
        layout.Controls.Add(_btnChangeMasterPassword);

        var hint = new Label
        {
            Text = "说明：默认仅使用 Windows DPAPI 保护账号库（与当前 Windows 账户绑定，免密自动解锁，\n" +
                   "但同一账户下运行的其它程序理论上也能解密）。启用主密码后安全性更高，但\n" +
                   "丢失主密码将无法恢复账号库，请务必牢记。",
            AutoSize = true,
            ForeColor = Color.DimGray,
        };
        layout.Controls.Add(hint);

        page.Controls.Add(layout);
        return page;
    }

    private void ChkMasterPassword_CheckedChanged(object? sender, EventArgs e)
    {
        if (_chkMasterPassword.Checked && !_store.IsMasterPasswordEnabled)
        {
            using var dlg = new MasterPasswordForm(MasterPasswordMode.SetNew);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    _store.EnableMasterPassword(dlg.NewPasswordInput, _settings);
                    _settings.Save();
                    MessageBox.Show(this, "主密码已启用。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "启用主密码失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _chkMasterPassword.CheckedChanged -= ChkMasterPassword_CheckedChanged;
                    _chkMasterPassword.Checked = false;
                    _chkMasterPassword.CheckedChanged += ChkMasterPassword_CheckedChanged;
                }
            }
            else
            {
                _chkMasterPassword.CheckedChanged -= ChkMasterPassword_CheckedChanged;
                _chkMasterPassword.Checked = false;
                _chkMasterPassword.CheckedChanged += ChkMasterPassword_CheckedChanged;
            }
        }
        else if (!_chkMasterPassword.Checked && _store.IsMasterPasswordEnabled)
        {
            var confirm = MessageBox.Show(this,
                "关闭主密码保护后，账号库将仅依赖系统 DPAPI 保护。确定关闭吗？",
                "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm == DialogResult.Yes)
            {
                _store.DisableMasterPassword(_settings);
                _settings.Save();
            }
            else
            {
                _chkMasterPassword.CheckedChanged -= ChkMasterPassword_CheckedChanged;
                _chkMasterPassword.Checked = true;
                _chkMasterPassword.CheckedChanged += ChkMasterPassword_CheckedChanged;
            }
        }

        _btnChangeMasterPassword.Enabled = _store.IsMasterPasswordEnabled;
    }

    private void BtnChangeMasterPassword_Click(object? sender, EventArgs e)
    {
        using var dlg = new MasterPasswordForm(MasterPasswordMode.Change,
            pwd => PasswordStore.VerifyMasterPassword(pwd, _settings));

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _store.ChangeMasterPassword(dlg.OldPasswordInput, dlg.NewPasswordInput, _settings);
            _settings.Save();
            MessageBox.Show(this, "主密码已修改。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (InvalidMasterPasswordException)
        {
            MessageBox.Show(this, "旧密码不正确。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "修改主密码失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #endregion

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        _settings.PopupWidth = (int)_numWidth.Value;
        _settings.PopupHeight = (int)_numHeight.Value;
        _settings.PopupBackColorArgb = _pendingBackColor.ToArgb();
        _settings.PopupAccentColorArgb = _pendingAccentColor.ToArgb();
        _settings.AutoFillEnabled = _chkAutoFill.Checked;
        _settings.HotkeyModifiers = _pendingModifiers;
        _settings.HotkeyKeyCode = _pendingKeyCode;

        if (_settings.AutoStartEnabled != _chkAutoStart.Checked)
        {
            AutoStartManager.SetEnabled(_chkAutoStart.Checked);
            _settings.AutoStartEnabled = _chkAutoStart.Checked;
        }

        _settings.Save();
        _saved = true;
        _onSettingsApplied();

        MessageBox.Show(this, "设置已保存。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

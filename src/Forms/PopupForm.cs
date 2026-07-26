using System.Drawing;
using System.Windows.Forms;
using PwdTool.Models;
using PwdTool.Services;

namespace PwdTool.Forms;

/// <summary>
/// 鼠标位置快捷弹窗：以当前鼠标位置为左上角展示一个矩形框，
/// 顶部是搜索框，下方是最近/常用账号列表（默认 5 条，输入关键字后按匹配结果展示）。
///
/// 焦点策略（见计划"关键实现要点"）：因为需要在搜索框里打字，采用"可激活"方案——
/// 正常 Show()/Activate()，不加 WS_EX_NOACTIVATE；仅加 WS_EX_TOOLWINDOW 使其不出现在
/// Alt-Tab / 任务栏。点击窗体外部时，Windows 会让本窗体失活，用 Deactivate 事件关闭。
/// </summary>
public class PopupForm : Form
{
    private readonly PasswordStore _store;
    private readonly AppSettings _settings;

    private TextBox _searchBox = null!;
    private ListBox _listBox = null!;
    private List<AccountEntry> _currentList = new();
    private IntPtr _targetWindow;

    /// <summary>选中一条账号后触发：参数为选中的账号，以及是否已尝试自动填写（false 时表示改为复制到剪贴板）。</summary>
    public event Action<AccountEntry, bool>? EntryChosen;

    public PopupForm(PasswordStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        BuildUi();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW; // 不进 Alt-Tab、不在任务栏显示
            return cp;
        }
    }

    private void BuildUi()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        ApplySettingsAppearance();

        _searchBox = new TextBox
        {
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 11),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "搜索账号 / 用户名 / 网址 / 标签…",
        };
        _searchBox.TextChanged += (_, _) => RefreshList();
        _searchBox.KeyDown += SearchBox_KeyDown;

        _listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 40,
            BorderStyle = BorderStyle.None,
        };
        _listBox.DrawItem += ListBox_DrawItem;
        _listBox.DoubleClick += (_, _) => ChooseSelected();
        _listBox.KeyDown += ListBox_KeyDown;
        _listBox.MouseClick += (_, e) =>
        {
            int idx = _listBox.IndexFromPoint(e.Location);
            if (idx >= 0) _listBox.SelectedIndex = idx;
        };
        _listBox.Paint += ListBox_PaintEmptyHint;

        Controls.Add(_listBox);
        Controls.Add(_searchBox);

        Deactivate += (_, _) => Hide();
    }

    /// <summary>设置变更后（框大小/颜色）刷新外观，无需重建窗体。</summary>
    public void ApplySettingsAppearance()
    {
        Size = new Size(_settings.PopupWidth, _settings.PopupHeight);
        BackColor = Color.FromArgb(_settings.PopupBackColorArgb);
    }

    /// <summary>
    /// 在当前鼠标位置弹出（左上角对齐鼠标），超出屏幕边界时回退到工作区内。
    /// 若已经处于显示状态，则视为"再按一次热键关闭"（toggle）。
    /// </summary>
    public void ShowAtCursor()
    {
        if (Visible)
        {
            Hide();
            return;
        }

        // 记住当前前台窗口，选中账号后需要把输入送回这个窗口。
        _targetWindow = AutoTypeService.CaptureForegroundWindow();

        _searchBox.Text = string.Empty;
        RefreshList();

        var cursor = Cursor.Position;
        var workingArea = Screen.FromPoint(cursor).WorkingArea;

        int x = cursor.X;
        int y = cursor.Y;
        if (x + Width > workingArea.Right) x = Math.Max(workingArea.Left, workingArea.Right - Width);
        if (y + Height > workingArea.Bottom) y = Math.Max(workingArea.Top, workingArea.Bottom - Height);
        if (x < workingArea.Left) x = workingArea.Left;
        if (y < workingArea.Top) y = workingArea.Top;

        Location = new Point(x, y);

        Show();
        Activate();
        _searchBox.Focus();
    }

    private void RefreshList()
    {
        string keyword = _searchBox.Text;
        IEnumerable<AccountEntry> results = string.IsNullOrWhiteSpace(keyword)
            ? _store.GetTopUsed(_settings.PopupItemCount)
            : _store.Search(keyword).OrderByDescending(e => e.RecencyScore).Take(20);

        _currentList = results.ToList();

        _listBox.BeginUpdate();
        _listBox.Items.Clear();
        foreach (var entry in _currentList)
        {
            _listBox.Items.Add(entry);
        }
        _listBox.EndUpdate();
        _listBox.Invalidate(); // 确保"空列表提示"能在从有数据切到无数据时立即重绘

        if (_listBox.Items.Count > 0)
        {
            _listBox.SelectedIndex = 0;
        }
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                Hide();
                e.Handled = true;
                break;
            case Keys.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Keys.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Keys.Enter:
                ChooseSelected();
                e.Handled = true;
                break;
        }
    }

    private void ListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            ChooseSelected();
            e.Handled = true;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_listBox.Items.Count == 0) return;
        int idx = _listBox.SelectedIndex < 0 ? 0 : _listBox.SelectedIndex;
        idx = Math.Clamp(idx + delta, 0, _listBox.Items.Count - 1);
        _listBox.SelectedIndex = idx;
    }

    private void ChooseSelected()
    {
        if (_listBox.SelectedItem is not AccountEntry entry) return;

        Hide();
        _store.Touch(entry.Id);

        if (_settings.AutoFillEnabled && AutoTypeService.ActivateWindow(_targetWindow))
        {
            // 用 Timer 而不是 Thread.Sleep 阻塞 UI 线程；给目标窗口一点时间真正拿到前台
            // 焦点后，在 Tick 回调里二次确认前台窗口确实还是目标窗口，再注入按键，
            // 避免这段等待期间焦点被其它系统事件抢走导致密码被打进错误的窗口。
            var timer = new System.Windows.Forms.Timer { Interval = 80 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                CompleteAutoFillOrFallback(entry);
            };
            timer.Start();
        }
        else
        {
            ClipboardHelper.CopyPasswordWithAutoClear(entry.Password);
            EntryChosen?.Invoke(entry, false);
        }
    }

    private void CompleteAutoFillOrFallback(AccountEntry entry)
    {
        bool didAutoFill = false;

        if (AutoTypeService.IsForeground(_targetWindow))
        {
            didAutoFill = AutoTypeService.TypeCredential(entry.Username, entry.Password);
        }

        if (!didAutoFill)
        {
            ClipboardHelper.CopyPasswordWithAutoClear(entry.Password);
        }

        EntryChosen?.Invoke(entry, didAutoFill);
    }

    /// <summary>账号库为空或搜索无结果时，在空白列表中央画一行提示文字，避免用户误以为程序无响应。</summary>
    private void ListBox_PaintEmptyHint(object? sender, PaintEventArgs e)
    {
        if (_listBox.Items.Count > 0) return;

        string text = string.IsNullOrWhiteSpace(_searchBox.Text)
            ? "暂无账号，请在设置中新增或从浏览器导入"
            : "未找到匹配的账号";

        using var brush = new SolidBrush(Color.Gray);
        var size = e.Graphics.MeasureString(text, _listBox.Font);
        e.Graphics.DrawString(text, _listBox.Font, brush,
            (_listBox.ClientSize.Width - size.Width) / 2, (_listBox.ClientSize.Height - size.Height) / 2);
    }

    /// <summary>根据背景亮度动态选择黑/白文字，保证选中项在任意用户自定义强调色下都可读。</summary>
    private static Color GetReadableTextColor(Color background)
    {
        double luminance = (background.R * 299 + background.G * 587 + background.B * 114) / 1000.0;
        return luminance >= 128 ? Color.Black : Color.White;
    }

    private void ListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= _currentList.Count) return;

        var entry = _currentList[e.Index];
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color accent = Color.FromArgb(_settings.PopupAccentColorArgb);
        Color fore = selected ? GetReadableTextColor(accent) : Color.Black;
        Color sub = selected
            ? (fore == Color.Black ? Color.FromArgb(70, 70, 70) : Color.Gainsboro)
            : Color.DimGray;

        using (var backBrush = new SolidBrush(selected ? accent : BackColor))
        {
            e.Graphics.FillRectangle(backBrush, e.Bounds);
        }

        using var titleFont = new Font(e.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
        using var titleBrush = new SolidBrush(fore);
        using var subBrush = new SolidBrush(sub);

        e.Graphics.DrawString(entry.DisplayName, titleFont, titleBrush, e.Bounds.X + 8, e.Bounds.Y + 3);

        string subtitle = string.IsNullOrEmpty(entry.Username) ? entry.Url : entry.Username;
        if (entry.Tags.Count > 0)
        {
            subtitle += "   [" + entry.TagsDisplay + "]";
        }
        e.Graphics.DrawString(subtitle, e.Font ?? SystemFonts.DefaultFont, subBrush, e.Bounds.X + 8, e.Bounds.Y + 20);

        e.DrawFocusRectangle();
    }
}

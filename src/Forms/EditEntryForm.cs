using System.Drawing;
using System.Windows.Forms;
using PwdTool.Models;

namespace PwdTool.Forms;

/// <summary>新增或编辑一条账号密码记录。</summary>
public class EditEntryForm : Form
{
    private readonly AccountEntry _original;
    private readonly bool _isNew;

    private TextBox _txtTitle = null!;
    private TextBox _txtUrl = null!;
    private TextBox _txtUsername = null!;
    private TextBox _txtPassword = null!;
    private TextBox _txtTags = null!;
    private CheckBox _chkShowPassword = null!;
    private Label _lblError = null!;

    /// <summary>确认后的结果（新增时是新建的 AccountEntry，编辑时是修改后的同一条，保留 Id/统计字段）。</summary>
    public AccountEntry Result { get; private set; } = null!;

    public EditEntryForm(AccountEntry? existing = null)
    {
        _isNew = existing == null;
        // 克隆一份而不是直接持有调用方传入的实例：避免在用户点"确定"之前，编辑过程中的
        // 每次赋值就已经原地修改了仍然存在于 _store.Entries 里的同一个对象，让"取消"
        // 这个理应无副作用的操作变得不安全。
        _original = existing?.Clone() ?? new AccountEntry();
        BuildUi();
        Fill();
    }

    private void BuildUi()
    {
        Text = _isNew ? "新增账号" : "编辑账号";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(380, 300);
        Padding = new Padding(16);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "标题：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        _txtTitle = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(_txtTitle, 1, 0);

        layout.Controls.Add(new Label { Text = "网址：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
        _txtUrl = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(_txtUrl, 1, 1);

        layout.Controls.Add(new Label { Text = "账号：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 2);
        _txtUsername = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(_txtUsername, 1, 2);

        layout.Controls.Add(new Label { Text = "密码：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 3);
        _txtPassword = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        layout.Controls.Add(_txtPassword, 1, 3);

        _chkShowPassword = new CheckBox { Text = "显示密码", AutoSize = true };
        _chkShowPassword.CheckedChanged += (_, _) => _txtPassword.UseSystemPasswordChar = !_chkShowPassword.Checked;
        layout.Controls.Add(new Panel(), 0, 4);
        layout.Controls.Add(_chkShowPassword, 1, 4);

        layout.Controls.Add(new Label { Text = "标签：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 5);
        _txtTags = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "多个标签用逗号分隔，例如：工作, 银行" };
        layout.Controls.Add(_txtTags, 1, 5);

        _lblError = new Label { ForeColor = Color.Firebrick, Dock = DockStyle.Top, Height = 24 };

        var btnOk = new Button { Text = "确定", Width = 80 };
        var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 };
        btnOk.Click += BtnOk_Click;

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 40,
        };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        Controls.Add(layout);
        Controls.Add(_lblError);
        Controls.Add(buttonPanel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void Fill()
    {
        _txtTitle.Text = _original.Title;
        _txtUrl.Text = _original.Url;
        _txtUsername.Text = _original.Username;
        _txtPassword.Text = _original.Password;
        _txtTags.Text = _original.TagsDisplay;
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtUsername.Text) && string.IsNullOrWhiteSpace(_txtTitle.Text))
        {
            _lblError.Text = "标题和账号至少填写一项。";
            return;
        }

        if (string.IsNullOrEmpty(_txtPassword.Text))
        {
            _lblError.Text = "请输入密码。";
            return;
        }

        _original.Title = _txtTitle.Text.Trim();
        _original.Url = _txtUrl.Text.Trim();
        _original.Username = _txtUsername.Text.Trim();
        _original.Password = _txtPassword.Text;
        _original.TagsDisplay = _txtTags.Text;

        Result = _original;
        DialogResult = DialogResult.OK;
        Close();
    }
}

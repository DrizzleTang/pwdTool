using System.Drawing;
using System.Windows.Forms;

namespace PwdTool.Forms;

public enum MasterPasswordMode
{
    /// <summary>启动时解锁：仅需输入一次密码。</summary>
    Unlock,
    /// <summary>首次开启主密码保护：需输入并确认新密码。</summary>
    SetNew,
    /// <summary>修改已启用的主密码：需先验证旧密码，再输入并确认新密码。</summary>
    Change,
}

/// <summary>
/// 主密码输入窗口，覆盖"解锁 / 首次设置 / 修改"三种场景。
/// 纯代码手写布局（无 Designer 文件），保持项目整体风格简洁一致。
/// </summary>
public class MasterPasswordForm : Form
{
    private readonly MasterPasswordMode _mode;
    private readonly Func<string, bool>? _unlockValidator;

    private TextBox? _txtOld;
    private TextBox _txtNew = null!;
    private TextBox? _txtConfirm;
    private Label _lblError = null!;

    /// <summary>Unlock/Change 模式下，调用方用来验证密码正确性的回调返回 true 时才允许关闭窗口。</summary>
    public string OldPasswordInput { get; private set; } = string.Empty;

    /// <summary>SetNew/Change 模式下用户输入的新密码；Unlock 模式下与 <see cref="OldPasswordInput"/> 相同。</summary>
    public string NewPasswordInput { get; private set; } = string.Empty;

    public MasterPasswordForm(MasterPasswordMode mode, Func<string, bool>? unlockValidator = null)
    {
        _mode = mode;
        _unlockValidator = unlockValidator;
        BuildUi();
    }

    private void BuildUi()
    {
        Text = _mode switch
        {
            MasterPasswordMode.Unlock => "解锁账号库",
            MasterPasswordMode.SetNew => "设置主密码",
            MasterPasswordMode.Change => "修改主密码",
            _ => "主密码",
        };
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(320, _mode == MasterPasswordMode.Unlock ? 140 : 200);
        Padding = new Padding(16);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        if (_mode == MasterPasswordMode.Change)
        {
            layout.RowCount = row + 1;
            layout.Controls.Add(new Label { Text = "旧密码：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
            _txtOld = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            layout.Controls.Add(_txtOld, 1, row);
            row++;
        }

        string newLabel = _mode == MasterPasswordMode.Unlock ? "主密码：" : "新密码：";
        layout.Controls.Add(new Label { Text = newLabel, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _txtNew = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
        layout.Controls.Add(_txtNew, 1, row);
        row++;

        if (_mode != MasterPasswordMode.Unlock)
        {
            layout.Controls.Add(new Label { Text = "确认密码：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
            _txtConfirm = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            layout.Controls.Add(_txtConfirm, 1, row);
            row++;
        }

        layout.RowCount = row + 1;

        _lblError = new Label
        {
            Text = string.Empty,
            ForeColor = Color.Firebrick,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 32,
        };

        var btnOk = new Button { Text = "确定", DialogResult = DialogResult.None, Width = 80 };
        var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 };
        btnOk.Click += BtnOk_Click;

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 40,
            AutoSize = false,
        };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        Controls.Add(layout);
        Controls.Add(_lblError);
        Controls.Add(buttonPanel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Shown += (_, _) => (_mode == MasterPasswordMode.Change ? _txtOld : _txtNew)?.Focus();
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        _lblError.Text = string.Empty;

        if (_mode == MasterPasswordMode.Unlock)
        {
            string pwd = _txtNew.Text;
            if (string.IsNullOrEmpty(pwd))
            {
                _lblError.Text = "请输入主密码。";
                return;
            }

            if (_unlockValidator != null && !_unlockValidator(pwd))
            {
                _lblError.Text = "主密码不正确，请重试。";
                _txtNew.SelectAll();
                _txtNew.Focus();
                return;
            }

            NewPasswordInput = pwd;
            OldPasswordInput = pwd;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (_mode == MasterPasswordMode.Change)
        {
            string oldPwd = _txtOld!.Text;
            if (string.IsNullOrEmpty(oldPwd))
            {
                _lblError.Text = "请输入旧密码。";
                return;
            }

            if (_unlockValidator != null && !_unlockValidator(oldPwd))
            {
                _lblError.Text = "旧密码不正确。";
                return;
            }

            OldPasswordInput = oldPwd;
        }

        string newPwd = _txtNew.Text;
        string confirm = _txtConfirm!.Text;

        if (string.IsNullOrEmpty(newPwd))
        {
            _lblError.Text = "请输入新密码。";
            return;
        }

        if (newPwd != confirm)
        {
            _lblError.Text = "两次输入的密码不一致。";
            return;
        }

        NewPasswordInput = newPwd;
        DialogResult = DialogResult.OK;
        Close();
    }
}

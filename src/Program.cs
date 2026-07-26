using System.Windows.Forms;

namespace PwdTool;

internal static class Program
{
    /// <summary>命名 Mutex 防止程序重复启动多个实例（多开会导致热键注册互相冲突）。</summary>
    private const string MutexName = "Local\\PwdTool.SingleInstance.Mutex";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("PwdTool 已经在运行中，请在系统托盘查看。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var context = TrayAppContext.TryCreate();
        if (context == null)
        {
            // 用户取消了主密码解锁，或账号库无法读取，直接退出。
            return;
        }

        Application.Run(context);
    }
}

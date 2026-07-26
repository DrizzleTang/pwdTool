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

        RegisterGlobalExceptionHandlers();

        ApplicationConfiguration.Initialize();

        var context = TrayAppContext.TryCreate();
        if (context == null)
        {
            // 用户取消了主密码解锁，或账号库无法读取，直接退出。
            return;
        }

        Application.Run(context);

        // 显式释放，语义上更清晰；进程退出时 using 也会自动处理未显式释放的情况。
        mutex.ReleaseMutex();
    }

    /// <summary>
    /// 全局异常兜底：UI 线程上的未处理异常（例如某个按钮点击事件里遗漏的 try/catch）
    /// 默认会导致 WinForms 弹出一个不太友好的系统对话框甚至直接崩溃退出；这里统一
    /// 捕获并给出更清晰的提示，同时避免因为个别操作的异常导致整个程序意外终止、
    /// 丢失用户尚未保存的操作。
    /// </summary>
    private static void RegisterGlobalExceptionHandlers()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            MessageBox.Show(
                $"发生了一个未预期的错误：{e.Exception.Message}\n\n程序会尝试继续运行，但建议尽快保存重要操作后重启 PwdTool。",
                "PwdTool 出错了", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            string message = (e.ExceptionObject as Exception)?.Message ?? e.ExceptionObject?.ToString() ?? "未知错误";
            MessageBox.Show(
                $"发生了严重错误，程序即将退出：{message}",
                "PwdTool 严重错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
    }
}

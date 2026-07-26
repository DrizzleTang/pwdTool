using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PwdTool.Services;

/// <summary>
/// 密码复制到剪贴板的统一入口：复制后启动一次性定时器，若过一段时间剪贴板内容仍是
/// 这段密码（用户没有主动复制别的内容覆盖它），就自动清空，降低 Windows 剪贴板历史
/// （Win+V）/跨设备剪贴板同步长期留存明文密码的风险。
/// </summary>
public static class ClipboardHelper
{
    public static void CopyPasswordWithAutoClear(string password, int clearAfterSeconds = 30)
    {
        Clipboard.SetText(password);

        var timer = new System.Windows.Forms.Timer { Interval = clearAfterSeconds * 1000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            TryClearIfUnchanged(password);
        };
        timer.Start();
    }

    private static void TryClearIfUnchanged(string password)
    {
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == password)
            {
                Clipboard.Clear();
            }
        }
        catch (ExternalException)
        {
            // 剪贴板被其它进程占用时会抛这个异常，不是关键路径，忽略即可。
        }
    }
}

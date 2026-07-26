using System.Drawing;

namespace PwdTool.Services;

/// <summary>
/// 提供程序自身的图标（托盘图标、设置窗口标题栏图标等），而不是到处写死
/// SystemIcons.Application（系统默认图标，观感上和其它未打磨的小工具没有区分度）。
/// 用 Icon.ExtractAssociatedIcon 直接从当前可执行文件里提取通过
/// csproj 的 &lt;ApplicationIcon&gt; 嵌入的图标资源，不需要额外打包/部署单独的 .ico 文件。
/// </summary>
public static class AppIconProvider
{
    private static Icon? _cached;

    public static Icon Load()
    {
        if (_cached != null)
        {
            return _cached;
        }

        try
        {
            string? path = Environment.ProcessPath;
            if (path != null)
            {
                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    _cached = icon;
                    return icon;
                }
            }
        }
        catch
        {
            // 提取失败（例如极少见的资源缺失情况）时退回系统默认图标，不影响功能。
        }

        return SystemIcons.Application;
    }
}

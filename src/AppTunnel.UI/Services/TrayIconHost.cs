using System.Drawing;
using System.Windows.Forms;

namespace AppTunnel.UI.Services;

public sealed class TrayIconHost : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayIconHost(
        Action showWindow,
        Func<Task> refresh,
        Func<Task> exportLogs,
        Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(exportLogs);
        ArgumentNullException.ThrowIfNull(exitApplication);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open App Tunnel", null, (_, _) => showWindow());
        menu.Items.Add("Refresh service state", null, async (_, _) => await refresh());
        menu.Items.Add("Export logs", null, async (_, _) => await exportLogs());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "App Tunnel",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _notifyIcon.DoubleClick += (_, _) => showWindow();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}

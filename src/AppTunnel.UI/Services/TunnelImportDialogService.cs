using System.Windows;
using AppTunnel.UI.Views;
using WpfApplication = System.Windows.Application;

namespace AppTunnel.UI.Services;

public sealed record OpenVpnImportDialogResult(
    string DisplayName,
    string? Username,
    string? Password);

public sealed class TunnelImportDialogService
{
    public OpenVpnImportDialogResult? ShowOpenVpnImportDialog(string sourcePath)
    {
        var dialog = new OpenVpnImportDialog(sourcePath)
        {
            Owner = WpfApplication.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true
            ? dialog.Result
            : null;
    }
}

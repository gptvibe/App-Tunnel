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
    public OpenVpnImportDialogResult? ShowOpenVpnImportDialog(
        string sourcePath,
        OpenVpnImportDialogResult? initialValues = null,
        string? validationMessage = null)
    {
        var dialog = new OpenVpnImportDialog(sourcePath, initialValues, validationMessage)
        {
            Owner = WpfApplication.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true
            ? dialog.Result
            : null;
    }
}

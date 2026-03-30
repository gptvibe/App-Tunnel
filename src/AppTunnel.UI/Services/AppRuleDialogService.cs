using System.Windows;
using AppTunnel.UI.ViewModels;
using AppTunnel.UI.Views;
using WpfApplication = System.Windows.Application;

namespace AppTunnel.UI.Services;

public sealed record AddWin32AppDialogResult(string ExecutablePath, string? DisplayName);

public sealed record AssignAppRuleDialogResult(
    Guid? ProfileId,
    bool IsEnabled,
    bool LaunchOnConnect,
    bool KillAppTrafficOnTunnelDrop,
    bool IncludeChildProcesses);

public sealed class AppRuleDialogService(ExecutableIconService executableIconService)
{
    public AddWin32AppDialogResult? ShowAddWin32AppDialog()
    {
        var dialog = new AddAppDialog(executableIconService)
        {
            Owner = WpfApplication.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true
            ? dialog.Result
            : null;
    }

    public AssignAppRuleDialogResult? ShowAssignAppRuleDialog(
        AppRuleItemViewModel appRule,
        IReadOnlyList<TunnelProfileItemViewModel> profiles)
    {
        var dialog = new AssignProfileDialog(appRule, profiles)
        {
            Owner = WpfApplication.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true
            ? dialog.Result
            : null;
    }
}
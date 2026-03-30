using System.Collections.ObjectModel;
using System.Windows;
using AppTunnel.UI.Services;
using AppTunnel.UI.ViewModels;
using WpfWindow = System.Windows.Window;

namespace AppTunnel.UI.Views;

public partial class AssignProfileDialog : WpfWindow
{
    public AssignProfileDialog(
        AppRuleItemViewModel appRule,
        IReadOnlyList<TunnelProfileItemViewModel> profiles)
    {
        InitializeComponent();

        RuleDisplayName = appRule.DisplayName;
        TargetSummary = appRule.TargetSummary;
        ProfileOptions.Add(new ProfileOption(null, "No tunnel assigned"));
        foreach (var profile in profiles.OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            ProfileOptions.Add(new ProfileOption(profile.Id, profile.DisplayName));
        }

        DataContext = this;
        ProfileComboBox.ItemsSource = ProfileOptions;
        ProfileComboBox.SelectedValue = appRule.ProfileId;
        EnabledCheckBox.IsChecked = appRule.IsEnabled;
        LaunchOnConnectCheckBox.IsChecked = appRule.LaunchOnConnect;
        KillTrafficOnDropCheckBox.IsChecked = appRule.KillAppTrafficOnTunnelDrop;
        IncludeChildProcessesCheckBox.IsChecked = appRule.IncludeChildProcesses;
    }

    public string RuleDisplayName { get; }

    public string TargetSummary { get; }

    public ObservableCollection<ProfileOption> ProfileOptions { get; } = [];

    public AssignAppRuleDialogResult? Result { get; private set; }

    private void SaveButtonOnClick(object sender, RoutedEventArgs e)
    {
        Result = new AssignAppRuleDialogResult(
            ProfileComboBox.SelectedValue as Guid?,
            EnabledCheckBox.IsChecked ?? true,
            LaunchOnConnectCheckBox.IsChecked ?? false,
            KillTrafficOnDropCheckBox.IsChecked ?? false,
            IncludeChildProcessesCheckBox.IsChecked ?? false);

        DialogResult = true;
    }

    private void CancelButtonOnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public sealed record ProfileOption(Guid? ProfileId, string Label);
}
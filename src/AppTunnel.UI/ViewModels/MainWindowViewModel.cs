using System.Collections.ObjectModel;
using System.Globalization;
using AppTunnel.Core.Domain;
using AppTunnel.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppTunnel.UI.ViewModels;

public partial class MainWindowViewModel(AppTunnelControlClient controlClient) : ObservableObject
{
    [ObservableProperty]
    private string serviceStatus = "Waiting for service handshake";

    [ObservableProperty]
    private string serviceRunState = "Unknown";

    [ObservableProperty]
    private string protocolVersion = "Unavailable";

    [ObservableProperty]
    private string lastHandshakeUtc = "No handshake yet";

    [ObservableProperty]
    private string lastError = "None";

    [ObservableProperty]
    private string preferredRoutingBackend = "Unknown";

    [ObservableProperty]
    private string tunnelManagerState = "Unavailable";

    [ObservableProperty]
    private string routerManagerState = "Unavailable";

    [ObservableProperty]
    private string startedAtUtc = "Unavailable";

    [ObservableProperty]
    private string loadedProfilesSummary = "0 profile(s) loaded";

    [ObservableProperty]
    private string loadedRulesSummary = "0 app rule(s) loaded";

    [ObservableProperty]
    private string dataRootDirectory = "Unavailable";

    [ObservableProperty]
    private string configurationFilePath = "Unavailable";

    [ObservableProperty]
    private string logsDirectory = "Unavailable";

    [ObservableProperty]
    private string exportsDirectory = "Unavailable";

    [ObservableProperty]
    private string pipeName = "Unavailable";

    [ObservableProperty]
    private string refreshIntervalSeconds = "Unavailable";

    [ObservableProperty]
    private string lastExportPath = "No bundle exported yet";

    public ObservableCollection<TunnelProfile> Profiles { get; } = [];

    public ObservableCollection<AppRule> AppRules { get; } = [];

    public ObservableCollection<StructuredLogEntry> RecentLogs { get; } = [];

    public ObservableCollection<string> TunnelEngines { get; } = [];

    public ObservableCollection<string> RouterBackends { get; } = [];

    public ObservableCollection<string> KnownGaps { get; } = [];

    public Task InitializeAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var ping = await controlClient.PingAsync();
            var overview = await controlClient.GetOverviewAsync();

            ServiceStatus = "Connected to App Tunnel Service";
            ServiceRunState = ping.RunState.ToString();
            ProtocolVersion = ping.ProtocolVersion;
            LastHandshakeUtc = ping.TimestampUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            LastError = "None";
            PreferredRoutingBackend = overview.Settings.PreferredRoutingBackend.ToString();
            TunnelManagerState = overview.SessionState.TunnelManagerState;
            RouterManagerState = overview.SessionState.RouterManagerState;
            StartedAtUtc = overview.SessionState.StartedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            LoadedProfilesSummary = $"{overview.SessionState.LoadedProfileCount} profile(s) loaded";
            LoadedRulesSummary = $"{overview.SessionState.LoadedAppRuleCount} app rule(s) loaded";
            DataRootDirectory = overview.Storage.RootDirectory;
            ConfigurationFilePath = overview.Storage.ConfigurationFilePath;
            LogsDirectory = overview.Storage.LogsDirectory;
            ExportsDirectory = overview.Storage.ExportsDirectory;
            PipeName = overview.Settings.PipeName;
            RefreshIntervalSeconds = overview.Settings.RefreshIntervalSeconds.ToString(CultureInfo.InvariantCulture);

            ReplaceItems(Profiles, overview.Profiles.OrderBy(profile => profile.DisplayName));
            ReplaceItems(AppRules, overview.AppRules.OrderBy(rule => rule.DisplayName));
            ReplaceItems(RecentLogs, overview.RecentLogs.OrderByDescending(entry => entry.TimestampUtc));
            ReplaceItems(TunnelEngines, overview.TunnelEngines.Select(DescribeTunnelEngine));
            ReplaceItems(RouterBackends, overview.RouterBackends.Select(DescribeRouter));
            ReplaceItems(KnownGaps, overview.KnownGaps);
        }
        catch (Exception ex)
        {
            ServiceStatus = "Service unreachable";
            ServiceRunState = "Unavailable";
            ProtocolVersion = "Unavailable";
            LastHandshakeUtc = "Failed";
            PreferredRoutingBackend = "Unavailable";
            TunnelManagerState = "Unavailable";
            RouterManagerState = "Unavailable";
            StartedAtUtc = "Unavailable";
            LoadedProfilesSummary = "0 profile(s) loaded";
            LoadedRulesSummary = "0 app rule(s) loaded";
            DataRootDirectory = "Unavailable";
            ConfigurationFilePath = "Unavailable";
            LogsDirectory = "Unavailable";
            ExportsDirectory = "Unavailable";
            PipeName = "Unavailable";
            RefreshIntervalSeconds = "Unavailable";
            LastError = ex.Message;

            ReplaceItems(Profiles, []);
            ReplaceItems(AppRules, []);
            ReplaceItems(RecentLogs, []);
            ReplaceItems(TunnelEngines, []);
            ReplaceItems(RouterBackends, []);
            ReplaceItems(KnownGaps, []);
        }
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        try
        {
            var bundle = await controlClient.ExportLogBundleAsync();
            LastExportPath = $"Exported bundle: {bundle.BundlePath}";
            LastError = "None";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastExportPath = "Export failed";
        }
    }

    private static void ReplaceItems<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();

        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    private static string DescribeTunnelEngine(TunnelEngineStatus status) =>
        $"{status.DisplayName} [{status.Readiness}] - {status.Notes}";

    private static string DescribeRouter(RouterBackendStatus status) =>
        $"{status.DisplayName} [{status.Readiness}] - {status.Notes}";
}

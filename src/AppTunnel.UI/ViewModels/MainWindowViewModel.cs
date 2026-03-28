using System.Collections.ObjectModel;
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
    private string protocolVersion = "Unavailable";

    [ObservableProperty]
    private string lastHandshakeUtc = "No handshake yet";

    [ObservableProperty]
    private string distributionMode = "Unknown";

    [ObservableProperty]
    private string preferredRouter = "Unknown";

    [ObservableProperty]
    private string lastError = "None";

    public ObservableCollection<string> SupportedAppKinds { get; } = [];

    public ObservableCollection<string> PlannedAppKinds { get; } = [];

    public ObservableCollection<string> SupportedProfileKinds { get; } = [];

    public ObservableCollection<string> PlannedProfileKinds { get; } = [];

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
            ProtocolVersion = ping.ProtocolVersion;
            LastHandshakeUtc = ping.TimestampUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            DistributionMode = overview.DistributionMode.ToString();
            PreferredRouter = overview.PreferredRouter.ToString();
            LastError = "None";

            ReplaceItems(SupportedAppKinds, overview.SupportedAppKinds.Select(kind => kind.ToString()));
            ReplaceItems(PlannedAppKinds, overview.PlannedAppKinds.Select(kind => kind.ToString()));
            ReplaceItems(SupportedProfileKinds, overview.SupportedProfileKinds.Select(kind => kind.ToString()));
            ReplaceItems(PlannedProfileKinds, overview.PlannedProfileKinds.Select(kind => kind.ToString()));
            ReplaceItems(TunnelEngines, overview.TunnelEngines.Select(DescribeTunnelEngine));
            ReplaceItems(RouterBackends, overview.RouterBackends.Select(DescribeRouter));
            ReplaceItems(KnownGaps, overview.KnownGaps);
        }
        catch (Exception ex)
        {
            ServiceStatus = "Service unreachable";
            ProtocolVersion = "Unavailable";
            LastHandshakeUtc = "Failed";
            DistributionMode = "Unavailable";
            PreferredRouter = "Unavailable";
            LastError = ex.Message;

            ReplaceItems(SupportedAppKinds, []);
            ReplaceItems(PlannedAppKinds, []);
            ReplaceItems(SupportedProfileKinds, []);
            ReplaceItems(PlannedProfileKinds, []);
            ReplaceItems(TunnelEngines, []);
            ReplaceItems(RouterBackends, []);
            ReplaceItems(KnownGaps, []);
        }
    }

    private static void ReplaceItems(ObservableCollection<string> collection, IEnumerable<string> values)
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
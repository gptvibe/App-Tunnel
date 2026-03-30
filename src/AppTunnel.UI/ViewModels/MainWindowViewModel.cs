using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using AppTunnel.Core.Domain;
using AppTunnel.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace AppTunnel.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AppTunnelControlClient controlClient;
    private readonly AppRuleDialogService appRuleDialogService;
    private readonly ExecutableIconService executableIconService;
    private CancellationTokenSource? _autoRefreshCancellationSource;
    private Task? _autoRefreshTask;

    public MainWindowViewModel(
        AppTunnelControlClient controlClient,
        AppRuleDialogService appRuleDialogService,
        ExecutableIconService executableIconService)
    {
        this.controlClient = controlClient;
        this.appRuleDialogService = appRuleDialogService;
        this.executableIconService = executableIconService;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ExportLogsCommand = new AsyncRelayCommand(ExportLogsAsync);
        ImportWireGuardProfileCommand = new AsyncRelayCommand(ImportWireGuardProfileAsync);
        ConnectProfileCommand = new AsyncRelayCommand(ConnectSelectedProfileAsync, CanConnectSelectedProfile);
        DisconnectProfileCommand = new AsyncRelayCommand(DisconnectSelectedProfileAsync, CanDisconnectSelectedProfile);
        AddAppRuleCommand = new AsyncRelayCommand(AddAppRuleAsync);
        AssignAppRuleCommand = new AsyncRelayCommand(AssignSelectedAppRuleAsync, CanAssignSelectedAppRule);
    }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProfile))]
    private TunnelProfileItemViewModel? selectedProfile;

    [ObservableProperty]
    private string profileActionMessage = "Import a WireGuard .conf profile to begin.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAppRule))]
    private AppRuleItemViewModel? selectedAppRule;

    [ObservableProperty]
    private string appActionMessage = "Add a Win32 .exe to create a rule.";

    public ObservableCollection<TunnelProfileItemViewModel> Profiles { get; } = [];

    public ObservableCollection<AppRuleItemViewModel> AppRules { get; } = [];

    public ObservableCollection<StructuredLogEntry> RecentLogs { get; } = [];

    public ObservableCollection<string> TunnelEngines { get; } = [];

    public ObservableCollection<string> RouterBackends { get; } = [];

    public ObservableCollection<string> KnownGaps { get; } = [];

    public bool HasSelectedProfile => SelectedProfile is not null;

    public bool HasSelectedAppRule => SelectedAppRule is not null;

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ExportLogsCommand { get; }

    public AsyncRelayCommand ImportWireGuardProfileCommand { get; }

    public AsyncRelayCommand ConnectProfileCommand { get; }

    public AsyncRelayCommand DisconnectProfileCommand { get; }

    public AsyncRelayCommand AddAppRuleCommand { get; }

    public AsyncRelayCommand AssignAppRuleCommand { get; }

    public async Task InitializeAsync()
    {
        await RefreshAsync();
        StartAutoRefresh();
    }

    public async Task RefreshAsync()
    {
        await RefreshCoreAsync(SelectedProfile?.Id, SelectedAppRule?.Id);
    }

    public void StopAutoRefresh()
    {
        _autoRefreshCancellationSource?.Cancel();
    }

    public async Task ExportLogsAsync()
    {
        try
        {
            var bundle = await controlClient.ExportLogBundleAsync();
            LastExportPath = $"Exported bundle: {bundle.BundlePath}";
            LastError = "None";
            ProfileActionMessage = "Log bundle exported.";
            AppActionMessage = "Log bundle exported.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastExportPath = "Export failed";
            ProfileActionMessage = ex.Message;
            AppActionMessage = ex.Message;
        }
    }

    public async Task ImportWireGuardProfileAsync()
    {
        var openFileDialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "WireGuard config (*.conf)|*.conf",
            Title = "Import WireGuard profile",
        };

        if (openFileDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var importedProfile = await controlClient.ImportProfileAsync(
                openFileDialog.FileName,
                Path.GetFileNameWithoutExtension(openFileDialog.FileName));

            LastError = "None";
            ProfileActionMessage = $"Imported '{importedProfile.DisplayName}'.";
            await RefreshCoreAsync(importedProfile.Id, SelectedAppRule?.Id);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ProfileActionMessage = ex.Message;
        }
    }

    public async Task ConnectSelectedProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        try
        {
            var status = await controlClient.ConnectProfileAsync(SelectedProfile.Id);
            LastError = string.IsNullOrWhiteSpace(status.ErrorMessage) ? "None" : status.ErrorMessage;
            ProfileActionMessage = status.Summary;
            await RefreshCoreAsync(SelectedProfile.Id, SelectedAppRule?.Id);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ProfileActionMessage = ex.Message;
        }
    }

    public async Task DisconnectSelectedProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        try
        {
            var status = await controlClient.DisconnectProfileAsync(SelectedProfile.Id);
            LastError = string.IsNullOrWhiteSpace(status.ErrorMessage) ? "None" : status.ErrorMessage;
            ProfileActionMessage = status.Summary;
            await RefreshCoreAsync(SelectedProfile.Id, SelectedAppRule?.Id);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ProfileActionMessage = ex.Message;
        }
    }

    public async Task AddAppRuleAsync()
    {
        var dialogResult = appRuleDialogService.ShowAddWin32AppDialog();
        if (dialogResult is null)
        {
            return;
        }

        try
        {
            var appRule = await controlClient.AddAppRuleAsync(new AppRuleCreateRequest(
                AppKind.Win32Exe,
                dialogResult.DisplayName,
                dialogResult.ExecutablePath,
                PackageFamilyName: null,
                PackageIdentity: null));

            LastError = "None";
            AppActionMessage = $"Added '{appRule.DisplayName}'.";
            await RefreshCoreAsync(SelectedProfile?.Id, appRule.Id);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppActionMessage = ex.Message;
        }
    }

    public async Task AssignSelectedAppRuleAsync()
    {
        if (SelectedAppRule is null)
        {
            return;
        }

        var dialogResult = appRuleDialogService.ShowAssignAppRuleDialog(SelectedAppRule, Profiles.ToArray());
        if (dialogResult is null)
        {
            return;
        }

        try
        {
            var appRule = await controlClient.UpdateAppRuleAsync(new AppRuleUpdateRequest(
                SelectedAppRule.Id,
                dialogResult.ProfileId,
                dialogResult.IsEnabled,
                dialogResult.LaunchOnConnect,
                dialogResult.KillAppTrafficOnTunnelDrop,
                dialogResult.IncludeChildProcesses));

            LastError = "None";
            AppActionMessage = $"Updated '{appRule.DisplayName}'.";
            await RefreshCoreAsync(SelectedProfile?.Id, appRule.Id);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppActionMessage = ex.Message;
        }
    }

    partial void OnSelectedProfileChanged(TunnelProfileItemViewModel? value)
    {
        ConnectProfileCommand.NotifyCanExecuteChanged();
        DisconnectProfileCommand.NotifyCanExecuteChanged();

        if (value is null)
        {
            return;
        }

        ProfileActionMessage = value.ErrorMessage == "None"
            ? value.ConnectionSummary
            : value.ErrorMessage;
    }

    partial void OnSelectedAppRuleChanged(AppRuleItemViewModel? value)
    {
        AssignAppRuleCommand.NotifyCanExecuteChanged();

        if (value is null)
        {
            return;
        }

        AppActionMessage = value.StatusSummary;
    }

    private async Task RefreshCoreAsync(Guid? preferredProfileId, Guid? preferredAppRuleId)
    {
        try
        {
            var ping = await controlClient.PingAsync();
            var overview = await controlClient.GetOverviewAsync();
            var profilesById = overview.Profiles.ToDictionary(profile => profile.Id);
            var statusesByProfileId = overview.TunnelStatuses.ToDictionary(status => status.ProfileId);
            var profileItems = overview.Profiles
                .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(profile => new TunnelProfileItemViewModel(
                    profile,
                    statusesByProfileId.GetValueOrDefault(profile.Id)))
                .ToArray();
            var appRuleItems = overview.AppRules
                .OrderBy(rule => rule.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(rule => new AppRuleItemViewModel(
                    rule,
                    profilesById,
                    statusesByProfileId,
                    executableIconService))
                .ToArray();

            ServiceStatus = "Connected to App Tunnel Service";
            ServiceRunState = ping.RunState.ToString();
            ProtocolVersion = ping.ProtocolVersion;
            LastHandshakeUtc = ping.TimestampUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            PreferredRoutingBackend = overview.Settings.PreferredRoutingBackend.ToString();
            TunnelManagerState = overview.SessionState.TunnelManagerState;
            RouterManagerState = overview.SessionState.RouterManagerState;
            StartedAtUtc = overview.SessionState.StartedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            LoadedProfilesSummary = $"{overview.SessionState.LoadedProfileCount} profile(s) loaded, {overview.SessionState.ConnectedProfileCount} connected";
            LoadedRulesSummary = $"{overview.SessionState.LoadedAppRuleCount} app rule(s) loaded";
            DataRootDirectory = overview.Storage.RootDirectory;
            ConfigurationFilePath = overview.Storage.ConfigurationFilePath;
            LogsDirectory = overview.Storage.LogsDirectory;
            ExportsDirectory = overview.Storage.ExportsDirectory;
            PipeName = overview.Settings.PipeName;
            RefreshIntervalSeconds = overview.Settings.RefreshIntervalSeconds.ToString(CultureInfo.InvariantCulture);
            LastError = "None";

            ReplaceItems(Profiles, profileItems);
            ReplaceItems(AppRules, appRuleItems);
            ReplaceItems(RecentLogs, overview.RecentLogs.OrderByDescending(entry => entry.TimestampUtc));
            ReplaceItems(TunnelEngines, overview.TunnelEngines.Select(DescribeTunnelEngine));
            ReplaceItems(RouterBackends, overview.RouterBackends.Select(DescribeRouter));
            ReplaceItems(KnownGaps, overview.KnownGaps);

            SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == preferredProfileId)
                ?? Profiles.FirstOrDefault(profile => profile.Id == SelectedProfile?.Id)
                ?? Profiles.FirstOrDefault();
            SelectedAppRule = AppRules.FirstOrDefault(rule => rule.Id == preferredAppRuleId)
                ?? AppRules.FirstOrDefault(rule => rule.Id == SelectedAppRule?.Id)
                ?? AppRules.FirstOrDefault();

            if (SelectedProfile is null)
            {
                ProfileActionMessage = "Import a WireGuard .conf profile to begin.";
            }

            if (SelectedAppRule is null)
            {
                AppActionMessage = "Add a Win32 .exe to create a rule.";
            }
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
            ProfileActionMessage = ex.Message;

            ReplaceItems(Profiles, []);
            ReplaceItems(AppRules, []);
            ReplaceItems(RecentLogs, []);
            ReplaceItems(TunnelEngines, []);
            ReplaceItems(RouterBackends, []);
            ReplaceItems(KnownGaps, []);
            SelectedProfile = null;
            SelectedAppRule = null;
            AppActionMessage = ex.Message;
        }
    }

    private void StartAutoRefresh()
    {
        if (_autoRefreshTask is not null)
        {
            return;
        }

        _autoRefreshCancellationSource = new CancellationTokenSource();
        _autoRefreshTask = RunAutoRefreshLoopAsync(_autoRefreshCancellationSource.Token);
    }

    private async Task RunAutoRefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(GetRefreshInterval(), cancellationToken);
                await RefreshCoreAsync(SelectedProfile?.Id, SelectedAppRule?.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // RefreshCoreAsync already updates the surface state when it fails.
            }
        }
    }

    private TimeSpan GetRefreshInterval() =>
        int.TryParse(RefreshIntervalSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(5);

    private bool CanConnectSelectedProfile() => SelectedProfile?.CanConnect ?? false;

    private bool CanDisconnectSelectedProfile() => SelectedProfile?.CanDisconnect ?? false;

    private bool CanAssignSelectedAppRule() => SelectedAppRule is not null;

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

using System.Globalization;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;
using AppTunnel.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AppTunnel.Service.Runtime;

public sealed class AppTunnelRuntime(
    ILogger<AppTunnelRuntime> logger,
    IAppTunnelConfigurationStore configurationStore,
    IApplicationDiscoveryService applicationDiscoveryService,
    IStructuredLogService structuredLogService,
    ILogBundleExporter logBundleExporter,
    IWfpBackendControl wfpBackendControl,
    ServiceTunnelManager tunnelManager,
    RouterManager routerManager,
    IEnumerable<ITunnelEngine> tunnelEngines,
    IEnumerable<IRouterBackend> routerBackends,
    AppTunnelPaths paths) : IHostedService, IAppTunnelControlService
{
    private static readonly AppTunnelConfiguration EmptyConfiguration = new(
        [],
        [],
        new AppTunnelSettings(
            AppTunnelPipeNames.Control,
            RoutingBackendKind.DryRun,
            AppTunnelPaths.GetDefaultRootDirectory(),
            RefreshIntervalSeconds: 5,
            StartMinimizedToTray: false,
            DistributionMode: DistributionMode.Installer));

    private static readonly string[] KnownGaps =
    [
        "OpenVPN currently wraps openvpn.exe from the Windows service; an embedded OpenVPN 3 Core backend is still a future migration.",
        "WinDivert routing is IPv4-first and currently targets TCP/UDP flows only.",
        "Child-process inheritance is best-effort; exact parent/child tracking is not yet complete.",
        "Named-pipe ACL hardening is still development-grade.",
        "Live WireGuard sessions require the official WireGuard for Windows runtime; mock mode is used when it is unavailable.",
        "The OpenVPN MVP blocks script and management directives during import and force-stops the managed process on disconnect.",
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IReadOnlyList<ITunnelEngine> _tunnelEngines = tunnelEngines.OrderBy(engine => engine.DisplayName).ToArray();
    private readonly IReadOnlyList<IRouterBackend> _routerBackends = routerBackends.OrderBy(backend => backend.DisplayName).ToArray();

    private AppTunnelConfiguration _configuration = EmptyConfiguration;
    private SessionState _sessionState = new(
        ServiceRunState.Stopped,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        0,
        0,
        0,
        "Tunnel manager is stopped.",
        "Router manager is stopped.",
        RoutingBackendKind.DryRun);
    private DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _startedAtUtc = DateTimeOffset.UtcNow;
            _sessionState = CreateSessionState(ServiceRunState.Starting);

            logger.LogInformation("App Tunnel runtime starting.");
            await structuredLogService.WriteAsync(
                "Information",
                nameof(AppTunnelRuntime),
                "App Tunnel runtime starting.",
                properties: null,
                cancellationToken);

            _configuration = await configurationStore.LoadAsync(cancellationToken);
            await tunnelManager.LoadAsync(_configuration.Profiles, cancellationToken);
            await ApplyRoutingPlanAsync(cancellationToken);

            _sessionState = CreateSessionState(ServiceRunState.Running);

            await structuredLogService.WriteAsync(
                "Information",
                nameof(AppTunnelRuntime),
                "App Tunnel runtime started.",
                new Dictionary<string, string>
                {
                    ["profileCount"] = _configuration.Profiles.Count.ToString(CultureInfo.InvariantCulture),
                    ["appRuleCount"] = _configuration.AppRules.Count.ToString(CultureInfo.InvariantCulture),
                    ["preferredRoutingBackend"] = _configuration.Settings.PreferredRoutingBackend.ToString(),
                },
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _sessionState = CreateSessionState(ServiceRunState.Stopping);

            await structuredLogService.WriteAsync(
                "Information",
                nameof(AppTunnelRuntime),
                "App Tunnel runtime stopping.",
                properties: null,
                cancellationToken);

            await tunnelManager.StopAsync(_configuration.Profiles, cancellationToken);
            await routerManager.StopAsync(cancellationToken);

            _sessionState = CreateSessionState(ServiceRunState.Stopped);

            await structuredLogService.WriteAsync(
                "Information",
                nameof(AppTunnelRuntime),
                "App Tunnel runtime stopped.",
                properties: null,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<PingReply> PingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new PingReply(
            "App Tunnel Service",
            DateTimeOffset.UtcNow,
            "1.0.0",
            _sessionState.RunState));
    }

    public async Task<ServiceOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<TunnelStatusSnapshot> tunnelStatuses;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            tunnelStatuses = await tunnelManager.RefreshStatusesAsync(_configuration.Profiles, cancellationToken);
            await routerManager.ApplyAsync(CreateRoutingPlan(tunnelStatuses), cancellationToken);
            _sessionState = CreateSessionState(_sessionState.RunState);
        }
        finally
        {
            _gate.Release();
        }

        var logs = await structuredLogService.ReadRecentAsync(200, cancellationToken);

        return new ServiceOverview(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ServiceVersion: typeof(AppTunnelRuntime).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Settings: _configuration.Settings,
            SessionState: CreateSessionState(_sessionState.RunState),
            RouterDiagnostics: routerManager.Diagnostics,
            WfpDiagnostics: await wfpBackendControl.GetDiagnosticsAsync(cancellationToken),
            Storage: paths.ToSnapshot(),
            TunnelEngines: BuildTunnelEngineStatuses(),
            RouterBackends: BuildRouterBackendStatuses(),
            Profiles: _configuration.Profiles,
            TunnelStatuses: tunnelStatuses,
            AppRules: _configuration.AppRules,
            RecentLogs: logs,
            KnownGaps: KnownGaps);
    }

    public async Task<AppTunnelSettings> UpdateSettingsAsync(
        AppTunnelSettingsUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            _configuration = _configuration with
            {
                Settings = _configuration.Settings with
                {
                    PreferredRoutingBackend = request.PreferredRoutingBackend,
                },
            };

            await configurationStore.SaveAsync(_configuration, cancellationToken);
            await ApplyRoutingPlanAsync(cancellationToken);
            _sessionState = CreateSessionState(_sessionState.RunState);

            await structuredLogService.WriteAsync(
                "Information",
                nameof(AppTunnelRuntime),
                "Updated App Tunnel settings.",
                new Dictionary<string, string>
                {
                    ["preferredRoutingBackend"] = request.PreferredRoutingBackend.ToString(),
                },
                cancellationToken);

            return _configuration.Settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TunnelProfile> ImportProfileAsync(ProfileImportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var fullPath = Path.GetFullPath(request.SourcePath);
            if (_configuration.Profiles.Any(profile =>
                string.Equals(profile.ImportedConfigPath, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("A tunnel profile has already been imported from this configuration file.");
            }

            var importedProfile = await tunnelManager.ImportAsync(
                request with { SourcePath = fullPath },
                cancellationToken);

            _configuration = _configuration with
            {
                Profiles = _configuration.Profiles
                    .Append(importedProfile)
                    .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            };

            await configurationStore.SaveAsync(_configuration, cancellationToken);
            await ApplyRoutingPlanAsync(cancellationToken);
            _sessionState = CreateSessionState(_sessionState.RunState);

            await structuredLogService.WriteAsync(
                "Information",
                nameof(AppTunnelRuntime),
                "Persisted imported tunnel profile.",
                new Dictionary<string, string>
                {
                    ["profileId"] = importedProfile.Id.ToString("D"),
                    ["displayName"] = importedProfile.DisplayName,
                    ["sourcePath"] = importedProfile.ImportedConfigPath,
                },
                cancellationToken);

            return importedProfile;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppRule> AddAppRuleAsync(AppRuleCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var discoveredApplication = await applicationDiscoveryService.InspectAsync(
                request.AppKind,
                request.ExecutablePath,
                request.PackageFamilyName,
                request.PackageIdentity,
                request.DisplayName,
                cancellationToken);

            if (discoveredApplication.AppKind != AppKind.Win32Exe)
            {
                throw new NotSupportedException("Only Win32 .exe rules can be added right now.");
            }

            if (_configuration.AppRules.Any(rule =>
                rule.AppKind == AppKind.Win32Exe
                && string.Equals(rule.ExecutablePath, discoveredApplication.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("An app rule already exists for this executable path.");
            }

            var appRule = new AppRule(
                Guid.NewGuid(),
                discoveredApplication.AppKind,
                discoveredApplication.DisplayName,
                discoveredApplication.ExecutablePath,
                discoveredApplication.PackageFamilyName,
                discoveredApplication.PackageIdentity,
                profileId: null,
                isEnabled: true,
                launchOnConnect: false,
                killAppTrafficOnTunnelDrop: false,
                includeChildProcesses: false,
                updatedAtUtc: DateTimeOffset.UtcNow);

            await SaveAppRuleAsync(appRule, cancellationToken);

            await structuredLogService.WriteAsync(
                "Information",
                nameof(AppTunnelRuntime),
                "Added app rule.",
                BuildAppRuleProperties(appRule),
                cancellationToken);

            return appRule;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppRule> UpdateAppRuleAsync(AppRuleUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (request.ProfileId.HasValue)
            {
                _ = GetProfile(request.ProfileId.Value);
            }

            var existingRule = GetAppRule(request.RuleId);
            var updatedRule = new AppRule(
                existingRule.Id,
                existingRule.AppKind,
                existingRule.DisplayName,
                existingRule.ExecutablePath,
                existingRule.PackageFamilyName,
                existingRule.PackageIdentity,
                request.ProfileId,
                request.IsEnabled,
                request.LaunchOnConnect,
                request.KillAppTrafficOnTunnelDrop,
                request.IncludeChildProcesses,
                DateTimeOffset.UtcNow);

            await SaveAppRuleAsync(updatedRule, cancellationToken);

            await structuredLogService.WriteAsync(
                "Information",
                nameof(AppTunnelRuntime),
                "Updated app rule assignment.",
                BuildAppRuleProperties(updatedRule),
                cancellationToken);

            return updatedRule;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TunnelStatusSnapshot> ConnectProfileAsync(Guid profileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var status = await tunnelManager.ConnectAsync(GetProfile(profileId), cancellationToken);
            await ApplyRoutingPlanAsync(cancellationToken);
            _sessionState = CreateSessionState(_sessionState.RunState);
            return status;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TunnelStatusSnapshot> DisconnectProfileAsync(Guid profileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var status = await tunnelManager.DisconnectAsync(GetProfile(profileId), cancellationToken);
            await ApplyRoutingPlanAsync(cancellationToken);
            _sessionState = CreateSessionState(_sessionState.RunState);
            return status;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExportedLogBundle> ExportLogBundleAsync(
        string? destinationDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await structuredLogService.WriteAsync(
            "Information",
            nameof(AppTunnelRuntime),
            "Exporting log bundle.",
            new Dictionary<string, string>
            {
                ["destinationDirectory"] = destinationDirectory ?? paths.ExportsDirectory,
            },
            cancellationToken);

        return await logBundleExporter.ExportAsync(destinationDirectory, cancellationToken);
    }

    public Task<WfpOperationResult> InstallWfpBackendAsync(CancellationToken cancellationToken) =>
        wfpBackendControl.InstallAsync(cancellationToken);

    public Task<WfpOperationResult> UninstallWfpBackendAsync(CancellationToken cancellationToken) =>
        wfpBackendControl.UninstallAsync(cancellationToken);

    public Task<WfpOperationResult> SetWfpFiltersEnabledAsync(bool isEnabled, CancellationToken cancellationToken) =>
        wfpBackendControl.SetFiltersEnabledAsync(isEnabled, cancellationToken);

    public async Task<WfpOperationResult> AddWfpAppRuleAsync(
        WfpAppRuleRegistration request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ProfileId == Guid.Empty)
        {
            throw new ArgumentException("WFP app rules require a profile ID.", nameof(request));
        }

        _ = GetProfile(request.ProfileId);
        return await wfpBackendControl.AddAppRuleAsync(request, cancellationToken);
    }

    public Task<WfpOperationResult> RemoveWfpAppRuleAsync(Guid ruleId, CancellationToken cancellationToken) =>
        wfpBackendControl.RemoveAppRuleAsync(ruleId, cancellationToken);

    public Task<WfpBackendDiagnostics> GetWfpDiagnosticsAsync(CancellationToken cancellationToken) =>
        wfpBackendControl.GetDiagnosticsAsync(cancellationToken);

    private SessionState CreateSessionState(ServiceRunState runState) =>
        new(
            runState,
            _startedAtUtc,
            DateTimeOffset.UtcNow,
            _configuration.Profiles.Count,
            tunnelManager.ConnectedProfileCount,
            _configuration.AppRules.Count,
            tunnelManager.State,
            routerManager.State,
            routerManager.ActiveBackend);

    private IReadOnlyList<TunnelEngineStatus> BuildTunnelEngineStatuses() =>
        _tunnelEngines
            .Select(engine => new TunnelEngineStatus(
                engine.TunnelKind,
                engine.DisplayName,
                engine.Readiness,
                engine.Readiness switch
                {
                    BackendReadiness.DryRun => "Mock backend is registered because the official runtime is not active.",
                    BackendReadiness.Planned => "Implementation has not started.",
                    BackendReadiness.Mvp => "Service-managed backend is available.",
                    _ => "Backend is registered in the scaffold.",
                }))
            .ToArray();

    private IReadOnlyList<RouterBackendStatus> BuildRouterBackendStatuses()
    {
        var statuses = new List<RouterBackendStatus>
        {
            new(
                RoutingBackendKind.DryRun,
                "Dry-Run Router Backend",
                BackendReadiness.DryRun,
                routerManager.ActiveBackend == RoutingBackendKind.DryRun
                    ? routerManager.State
                    : "Available as a non-enforcing fallback."),
        };

        statuses.AddRange(_routerBackends.Select(backend => new RouterBackendStatus(
            backend.Kind,
            backend.DisplayName,
            backend.Readiness,
            routerManager.ActiveBackend == backend.Kind
                ? routerManager.State
                : backend.RequiresElevation
                    ? "Requires elevation and a running tunnel to enforce routing."
                    : "Registered.")));

        if (statuses.All(status => status.BackendKind != RoutingBackendKind.Wfp))
        {
            statuses.Add(new RouterBackendStatus(
                RoutingBackendKind.Wfp,
                "WFP Backend",
                BackendReadiness.Planned,
                "Native WFP bridge and driver are not implemented yet."));
        }

        return statuses
            .OrderBy(status => status.DisplayName)
            .ToArray();
    }

    private async Task ApplyRoutingPlanAsync(CancellationToken cancellationToken)
    {
        var tunnelStatuses = await tunnelManager.RefreshStatusesAsync(_configuration.Profiles, cancellationToken);
        await routerManager.ApplyAsync(CreateRoutingPlan(tunnelStatuses), cancellationToken);
    }

    private RoutingPlan CreateRoutingPlan(IReadOnlyList<TunnelStatusSnapshot> tunnelStatuses) =>
        new(
            DateTimeOffset.UtcNow,
            _configuration.Settings.PreferredRoutingBackend,
            _configuration.Profiles,
            tunnelStatuses,
            _configuration.AppRules);

    private TunnelProfile GetProfile(Guid profileId) =>
        _configuration.Profiles.FirstOrDefault(profile => profile.Id == profileId)
        ?? throw new KeyNotFoundException($"Tunnel profile '{profileId:D}' was not found.");

    private AppRule GetAppRule(Guid ruleId) =>
        _configuration.AppRules.FirstOrDefault(rule => rule.Id == ruleId)
        ?? throw new KeyNotFoundException($"App rule '{ruleId:D}' was not found.");

    private async Task SaveAppRuleAsync(AppRule appRule, CancellationToken cancellationToken)
    {
        var appRules = _configuration.AppRules
            .Where(rule => rule.Id != appRule.Id)
            .Append(appRule)
            .OrderBy(rule => rule.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.ExecutablePath ?? rule.PackageFamilyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _configuration = _configuration with
        {
            AppRules = appRules,
        };

        await configurationStore.SaveAsync(_configuration, cancellationToken);
        await ApplyRoutingPlanAsync(cancellationToken);
        await SynchronizeWfpRulesAsync(cancellationToken);
        _sessionState = CreateSessionState(_sessionState.RunState);
    }

    private async Task SynchronizeWfpRulesAsync(CancellationToken cancellationToken)
    {
        var eligibleRules = _configuration.AppRules
            .Where(rule => rule.IsEnabled && rule.ProfileId.HasValue)
            .Select(BuildWfpRegistration)
            .ToDictionary(rule => rule.RuleId);

        var diagnostics = await wfpBackendControl.GetDiagnosticsAsync(cancellationToken);
        var installedRuleIds = diagnostics.RegisteredRules
            .Select(rule => rule.RuleId)
            .ToHashSet();

        foreach (var existingRuleId in installedRuleIds.Where(id => !eligibleRules.ContainsKey(id)))
        {
            await wfpBackendControl.RemoveAppRuleAsync(existingRuleId, cancellationToken);
        }

        foreach (var registration in eligibleRules.Values)
        {
            await wfpBackendControl.AddAppRuleAsync(registration, cancellationToken);
        }
    }

    private static WfpAppRuleRegistration BuildWfpRegistration(AppRule appRule)
    {
        if (!appRule.ProfileId.HasValue)
        {
            throw new InvalidOperationException($"App rule '{appRule.Id:D}' is not assigned to a profile.");
        }

        return new WfpAppRuleRegistration(
            appRule.Id,
            appRule.AppKind,
            appRule.DisplayName,
            appRule.ExecutablePath,
            appRule.PackageFamilyName,
            appRule.PackageIdentity,
            appRule.ProfileId.Value,
            appRule.KillAppTrafficOnTunnelDrop,
            appRule.IncludeChildProcesses);
    }

    private static Dictionary<string, string> BuildAppRuleProperties(AppRule appRule)
    {
        var properties = new Dictionary<string, string>
        {
            ["ruleId"] = appRule.Id.ToString("D"),
            ["displayName"] = appRule.DisplayName,
            ["appKind"] = appRule.AppKind.ToString(),
            ["isEnabled"] = appRule.IsEnabled.ToString(),
            ["launchOnConnect"] = appRule.LaunchOnConnect.ToString(),
            ["killAppTrafficOnTunnelDrop"] = appRule.KillAppTrafficOnTunnelDrop.ToString(),
            ["includeChildProcesses"] = appRule.IncludeChildProcesses.ToString(),
        };

        if (!string.IsNullOrWhiteSpace(appRule.ExecutablePath))
        {
            properties["executablePath"] = appRule.ExecutablePath;
        }

        if (appRule.ProfileId.HasValue)
        {
            properties["profileId"] = appRule.ProfileId.Value.ToString("D");
        }

        if (!string.IsNullOrWhiteSpace(appRule.PackageFamilyName))
        {
            properties["packageFamilyName"] = appRule.PackageFamilyName;
        }

        if (!string.IsNullOrWhiteSpace(appRule.PackageIdentity))
        {
            properties["packageIdentity"] = appRule.PackageIdentity;
        }

        return properties;
    }
}

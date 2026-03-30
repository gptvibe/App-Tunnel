using System.Globalization;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;
using AppTunnel.Core.Services;

namespace AppTunnel.Service.Runtime;

public sealed class AppTunnelRuntime(
    ILogger<AppTunnelRuntime> logger,
    IAppTunnelConfigurationStore configurationStore,
    IApplicationDiscoveryService applicationDiscoveryService,
    IStructuredLogService structuredLogService,
    ILogBundleExporter logBundleExporter,
    ServiceTunnelManager tunnelManager,
    DryRunRouterManager dryRunRouterManager,
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
            StartMinimizedToTray: false));

    private static readonly string[] KnownGaps =
    [
        "OpenVPN remains a placeholder behind the shared tunnel-engine contract.",
        "Routing backends do not yet modify live packet flow.",
        "Launch-on-connect and tunnel-drop enforcement settings are persisted but not executed yet.",
        "Named-pipe ACL hardening is still development-grade.",
        "Live WireGuard sessions require the official WireGuard for Windows runtime; mock mode is used when it is unavailable.",
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
        "Dry-run router manager is stopped.",
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
            await dryRunRouterManager.LoadAsync(
                _configuration.Settings.PreferredRoutingBackend,
                _configuration.AppRules,
                cancellationToken);

            _sessionState = CreateSessionState(ServiceRunState.Running);

            await structuredLogService.WriteAsync(
                "Information",
                nameof(AppTunnelRuntime),
                "App Tunnel runtime started.",
                new Dictionary<string, string>
                {
                    ["profileCount"] = _configuration.Profiles.Count.ToString(CultureInfo.InvariantCulture),
                    ["appRuleCount"] = _configuration.AppRules.Count.ToString(CultureInfo.InvariantCulture),
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

            await dryRunRouterManager.StopAsync(cancellationToken);
            await tunnelManager.StopAsync(_configuration.Profiles, cancellationToken);

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
            "scaffold-v2",
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
            _sessionState = CreateSessionState(_sessionState.RunState);
        }
        finally
        {
            _gate.Release();
        }

        var logs = await structuredLogService.ReadRecentAsync(200, cancellationToken);

        return new ServiceOverview(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ServiceVersion: typeof(AppTunnelRuntime).Assembly.GetName().Version?.ToString() ?? "0.1.0-scaffold",
            Settings: _configuration.Settings,
            SessionState: CreateSessionState(_sessionState.RunState),
            Storage: paths.ToSnapshot(),
            TunnelEngines: BuildTunnelEngineStatuses(),
            RouterBackends: BuildRouterBackendStatuses(),
            Profiles: _configuration.Profiles,
            TunnelStatuses: tunnelStatuses,
            AppRules: _configuration.AppRules,
            RecentLogs: logs,
            KnownGaps: KnownGaps);
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
                throw new InvalidOperationException("A tunnel profile has already been imported from this .conf file.");
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

    private SessionState CreateSessionState(ServiceRunState runState) =>
        new(
            runState,
            _startedAtUtc,
            DateTimeOffset.UtcNow,
            _configuration.Profiles.Count,
            tunnelManager.ConnectedProfileCount,
            _configuration.AppRules.Count,
            tunnelManager.State,
            dryRunRouterManager.State,
            dryRunRouterManager.ActiveBackend);

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
                "Dry-Run Router Manager",
                BackendReadiness.DryRun,
                dryRunRouterManager.State),
        };

        statuses.AddRange(_routerBackends.Select(backend => new RouterBackendStatus(
            backend.Kind,
            backend.DisplayName,
            backend.Readiness,
            backend.Readiness switch
            {
                BackendReadiness.Planned => "Registered placeholder only.",
                BackendReadiness.DryRun => "Dry-run backend registered.",
                _ => "Backend is registered in the scaffold.",
            })));

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
        await dryRunRouterManager.LoadAsync(
            _configuration.Settings.PreferredRoutingBackend,
            _configuration.AppRules,
            cancellationToken);
        _sessionState = CreateSessionState(_sessionState.RunState);
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

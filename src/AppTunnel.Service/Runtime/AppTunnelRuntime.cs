using System.Globalization;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;
using AppTunnel.Core.Services;

namespace AppTunnel.Service.Runtime;

public sealed class AppTunnelRuntime(
    ILogger<AppTunnelRuntime> logger,
    IAppTunnelConfigurationStore configurationStore,
    IStructuredLogService structuredLogService,
    ILogBundleExporter logBundleExporter,
    DryRunTunnelManager dryRunTunnelManager,
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
        "Real VPN session activation remains a dry-run placeholder in this milestone.",
        "Routing backends do not yet modify live packet flow.",
        "Named-pipe ACL hardening is still development-grade.",
        "Profile import UX and service install flows are not wired yet.",
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
        "Dry-run tunnel manager is stopped.",
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
            await dryRunTunnelManager.LoadAsync(_configuration.Profiles, cancellationToken);
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
            await dryRunTunnelManager.StopAsync(cancellationToken);

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
            AppRules: _configuration.AppRules,
            RecentLogs: logs,
            KnownGaps: KnownGaps);
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
            _configuration.AppRules.Count,
            dryRunTunnelManager.State,
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
                    BackendReadiness.DryRun => "Dry-run placeholder is registered.",
                    BackendReadiness.Planned => "Implementation has not started.",
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
}

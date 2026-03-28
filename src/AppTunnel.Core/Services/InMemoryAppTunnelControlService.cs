using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Services;

public sealed class InMemoryAppTunnelControlService : IAppTunnelControlService
{
    private static readonly string[] ScaffoldKnownGaps =
    [
        "Real WireGuard tunnel activation is not implemented.",
        "OpenVPN remains a placeholder behind ITunnelEngine.",
        "Selective routing backends do not yet modify live traffic.",
        "Named-pipe security is suitable only for same-user development runs today.",
        "Persistent storage and diagnostics bundle export are still TODO.",
    ];

    private readonly AppTunnelCatalog _catalog = new();
    private readonly IReadOnlyList<ITunnelEngine> _tunnelEngines;
    private readonly IReadOnlyList<IRouterBackend> _routerBackends;

    public InMemoryAppTunnelControlService(
        IEnumerable<ITunnelEngine> tunnelEngines,
        IEnumerable<IRouterBackend> routerBackends)
    {
        _tunnelEngines = tunnelEngines.OrderBy(engine => engine.DisplayName).ToArray();
        _routerBackends = routerBackends.OrderBy(router => router.DisplayName).ToArray();
    }

    public Task<PingReply> PingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new PingReply(
            ServiceName: "App Tunnel Service",
            TimestampUtc: DateTimeOffset.UtcNow,
            ProtocolVersion: "scaffold-v1"));
    }

    public Task<ServiceOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var supportedProfiles = _tunnelEngines
            .Where(engine => engine.Readiness != BackendReadiness.Planned)
            .Select(engine => engine.ProviderKind)
            .Distinct()
            .ToArray();

        var plannedProfiles = _tunnelEngines
            .Where(engine => engine.Readiness == BackendReadiness.Planned)
            .Select(engine => engine.ProviderKind)
            .Distinct()
            .ToArray();

        var overview = new ServiceOverview(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ServiceVersion: typeof(InMemoryAppTunnelControlService).Assembly.GetName().Version?.ToString() ?? "0.1.0-scaffold",
            DistributionMode: _catalog.DistributionMode,
            PreferredRouter: _catalog.PreferredRouter,
            SupportedAppKinds: [AppKind.Win32Exe],
            PlannedAppKinds: [AppKind.PackagedApp],
            SupportedProfileKinds: supportedProfiles,
            PlannedProfileKinds: plannedProfiles,
            TunnelEngines: _tunnelEngines
                .Select(engine => new TunnelEngineStatus(
                    engine.ProviderKind,
                    engine.DisplayName,
                    engine.Readiness,
                    engine.Readiness == BackendReadiness.Planned
                        ? "Interface placeholder only."
                        : "Scaffolded for later implementation."))
                .ToArray(),
            RouterBackends: BuildRouterBackends(),
            Apps: _catalog.Apps.ToArray(),
            Profiles: _catalog.Profiles.ToArray(),
            Assignments: _catalog.Assignments.ToArray(),
            KnownGaps: ScaffoldKnownGaps);

        return Task.FromResult(overview);
    }

    private IReadOnlyList<RouterBackendStatus> BuildRouterBackends()
    {
        var statuses = _routerBackends
            .Select(router => new RouterBackendStatus(
                router.Kind,
                router.DisplayName,
                router.Readiness,
                router.Readiness == BackendReadiness.Mvp
                    ? "WinDivert MVP path."
                    : "Backend registered."))
            .ToList();

        if (statuses.All(status => status.BackendKind != RouterBackendKind.ProdRouter))
        {
            statuses.Add(new RouterBackendStatus(
                RouterBackendKind.ProdRouter,
                "WFP Production Router",
                BackendReadiness.Planned,
                "Native WFP driver and bridge are not implemented yet."));
        }

        return statuses
            .OrderBy(status => status.DisplayName)
            .ToArray();
    }
}
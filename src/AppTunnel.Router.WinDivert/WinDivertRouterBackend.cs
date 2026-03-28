using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Router.WinDivert;

public sealed class WinDivertRouterBackend : IRouterBackend
{
    public RouterBackendKind Kind => RouterBackendKind.MvpRouter;

    public string DisplayName => "WinDivert MVP Router";

    public BackendReadiness Readiness => BackendReadiness.Mvp;

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException("TODO: Load WinDivert, open filters, and bootstrap the MVP router.");

    public Task ApplyRoutingPlanAsync(RoutingPlan routingPlan, CancellationToken cancellationToken) =>
        throw new NotImplementedException("TODO: Apply app-scoped selective-routing rules through WinDivert.");
}
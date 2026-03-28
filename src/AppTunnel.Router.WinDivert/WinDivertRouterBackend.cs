using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Router.WinDivert;

public sealed class WinDivertRouterBackend : IRouterBackend
{
    public RoutingBackendKind Kind => RoutingBackendKind.WinDivert;

    public string DisplayName => "WinDivert Backend";

    public BackendReadiness Readiness => BackendReadiness.DryRun;

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ApplyRoutingPlanAsync(RoutingPlan routingPlan, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

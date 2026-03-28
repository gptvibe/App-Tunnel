using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Contracts;

public interface IRouterBackend
{
    RouterBackendKind Kind { get; }

    string DisplayName { get; }

    BackendReadiness Readiness { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task ApplyRoutingPlanAsync(RoutingPlan routingPlan, CancellationToken cancellationToken);
}
using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Contracts;

public interface IRouterBackend
{
    RoutingBackendKind Kind { get; }

    string DisplayName { get; }

    BackendReadiness Readiness { get; }

    bool RequiresElevation { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<RouterApplyResult> ApplyRoutingPlanAsync(RoutingPlan routingPlan, CancellationToken cancellationToken);

    RouterDiagnosticsSnapshot GetDiagnosticsSnapshot();

    Task StopAsync(CancellationToken cancellationToken);
}

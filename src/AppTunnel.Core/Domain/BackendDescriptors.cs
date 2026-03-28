namespace AppTunnel.Core.Domain;

public sealed record TunnelEngineStatus(
    TunnelKind TunnelKind,
    string DisplayName,
    BackendReadiness Readiness,
    string Notes);

public sealed record RouterBackendStatus(
    RoutingBackendKind BackendKind,
    string DisplayName,
    BackendReadiness Readiness,
    string Notes);

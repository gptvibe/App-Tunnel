namespace AppTunnel.Core.Domain;

public sealed record TunnelEngineStatus(
    VpnProviderKind ProviderKind,
    string DisplayName,
    BackendReadiness Readiness,
    string Notes);

public sealed record RouterBackendStatus(
    RouterBackendKind BackendKind,
    string DisplayName,
    BackendReadiness Readiness,
    string Notes);
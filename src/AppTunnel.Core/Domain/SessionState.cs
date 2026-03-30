namespace AppTunnel.Core.Domain;

public sealed record SessionState(
    ServiceRunState RunState,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int LoadedProfileCount,
    int ConnectedProfileCount,
    int LoadedAppRuleCount,
    string TunnelManagerState,
    string RouterManagerState,
    RoutingBackendKind ActiveRoutingBackend);

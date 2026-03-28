namespace AppTunnel.Core.Domain;

public sealed record PingReply(string ServiceName, DateTimeOffset TimestampUtc, string ProtocolVersion);

public sealed record ProfileImportRequest(string DisplayName, string SourcePath);

public sealed record RoutingPlan(Guid ProfileId, IReadOnlyList<Guid> AppIds);

public sealed record StoredSecretReference(
    string SecretId,
    string DisplayName,
    SecretPurpose Purpose,
    DateTimeOffset UpdatedAtUtc);

public sealed record ServiceOverview(
    DateTimeOffset GeneratedAtUtc,
    string ServiceVersion,
    DistributionMode DistributionMode,
    RouterBackendKind PreferredRouter,
    IReadOnlyList<AppKind> SupportedAppKinds,
    IReadOnlyList<AppKind> PlannedAppKinds,
    IReadOnlyList<VpnProviderKind> SupportedProfileKinds,
    IReadOnlyList<VpnProviderKind> PlannedProfileKinds,
    IReadOnlyList<TunnelEngineStatus> TunnelEngines,
    IReadOnlyList<RouterBackendStatus> RouterBackends,
    IReadOnlyList<AppDefinition> Apps,
    IReadOnlyList<TunnelProfile> Profiles,
    IReadOnlyList<AppTunnelAssignment> Assignments,
    IReadOnlyList<string> KnownGaps);
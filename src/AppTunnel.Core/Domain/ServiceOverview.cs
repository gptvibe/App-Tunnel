namespace AppTunnel.Core.Domain;

public sealed record PingReply(
    string ServiceName,
    DateTimeOffset TimestampUtc,
    string ProtocolVersion,
    ServiceRunState RunState);

public sealed record ProfileImportRequest(string DisplayName, string SourcePath);

public sealed record RoutingPlan(Guid ProfileId, IReadOnlyList<Guid> AppRuleIds);

public sealed record StoredSecretReference(
    string SecretId,
    string DisplayName,
    SecretPurpose Purpose,
    DateTimeOffset UpdatedAtUtc);

public sealed record AppTunnelSettings(
    string PipeName,
    RoutingBackendKind PreferredRoutingBackend,
    string DataRootDirectory,
    int RefreshIntervalSeconds,
    bool StartMinimizedToTray);

public sealed record StorageSnapshot(
    string RootDirectory,
    string ConfigurationFilePath,
    string LogsDirectory,
    string SecretsDirectory,
    string ExportsDirectory);

public sealed record StructuredLogEntry(
    Guid Id,
    DateTimeOffset TimestampUtc,
    string Level,
    string Source,
    string Message,
    IReadOnlyDictionary<string, string> Properties);

public sealed record ExportedLogBundle(
    string BundlePath,
    DateTimeOffset ExportedAtUtc,
    int IncludedFileCount);

public sealed record AppTunnelConfiguration(
    IReadOnlyList<TunnelProfile> Profiles,
    IReadOnlyList<AppRule> AppRules,
    AppTunnelSettings Settings);

public sealed record ServiceOverview(
    DateTimeOffset GeneratedAtUtc,
    string ServiceVersion,
    AppTunnelSettings Settings,
    SessionState SessionState,
    StorageSnapshot Storage,
    IReadOnlyList<TunnelEngineStatus> TunnelEngines,
    IReadOnlyList<RouterBackendStatus> RouterBackends,
    IReadOnlyList<TunnelProfile> Profiles,
    IReadOnlyList<AppRule> AppRules,
    IReadOnlyList<StructuredLogEntry> RecentLogs,
    IReadOnlyList<string> KnownGaps);

namespace AppTunnel.Core.Domain;

public sealed record RoutingPlan(
    DateTimeOffset GeneratedAtUtc,
    RoutingBackendKind PreferredBackend,
    IReadOnlyList<TunnelProfile> Profiles,
    IReadOnlyList<TunnelStatusSnapshot> TunnelStatuses,
    IReadOnlyList<AppRule> AppRules);

public sealed record RouterApplyResult(
    RoutingBackendKind ActiveBackend,
    string State,
    RouterDiagnosticsSnapshot Diagnostics);

public sealed record RouterDiagnosticsSnapshot(
    RoutingBackendKind BackendKind,
    bool RequiresElevation,
    bool IsElevated,
    string ActiveTunnel,
    IReadOnlyList<SelectedProcessActivity> SelectedProcesses,
    IReadOnlyList<FlowMappingSnapshot> MappedFlows,
    IReadOnlyList<PacketDropCounter> DroppedPackets,
    IReadOnlyList<string> ErrorStates,
    DateTimeOffset UpdatedAtUtc)
{
    public static RouterDiagnosticsSnapshot CreateDefault(RoutingBackendKind backendKind) =>
        new(
            backendKind,
            RequiresElevation: backendKind != RoutingBackendKind.DryRun,
            IsElevated: false,
            ActiveTunnel: "None",
            SelectedProcesses: [],
            MappedFlows: [],
            DroppedPackets: [],
            ErrorStates: [],
            UpdatedAtUtc: DateTimeOffset.UtcNow);
}

public sealed record SelectedProcessActivity(
    int ProcessId,
    Guid RuleId,
    string DisplayName,
    string ExecutablePath,
    string AssignedTunnel,
    string State,
    DateTimeOffset LastSeenUtc);

public sealed record FlowMappingSnapshot(
    Guid RuleId,
    int ProcessId,
    string DisplayName,
    string OriginalFlow,
    string EffectiveFlow,
    string Tunnel,
    string State,
    DateTimeOffset LastSeenUtc);

public sealed record PacketDropCounter(
    string Reason,
    long Count);

public sealed record AppTunnelSettingsUpdateRequest(
    RoutingBackendKind PreferredRoutingBackend);

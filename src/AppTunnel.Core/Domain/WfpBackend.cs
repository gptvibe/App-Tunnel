namespace AppTunnel.Core.Domain;

public enum WfpBackendInstallState
{
    NotInstalled,
    Installed,
    Faulted,
}

public sealed record WfpAppRuleRegistration(
    Guid RuleId,
    AppKind AppKind,
    string DisplayName,
    string? ExecutablePath,
    string? PackageFamilyName,
    string? PackageIdentity,
    Guid ProfileId,
    bool KillAppTrafficOnTunnelDrop,
    bool IncludeChildProcesses);

public sealed record WfpRuleDiagnostic(
    Guid RuleId,
    AppKind AppKind,
    string DisplayName,
    string MatchDescriptor,
    string TunnelDescriptor,
    bool KillOnTunnelDrop,
    bool IncludeChildProcesses,
    DateTimeOffset UpdatedAtUtc);

public sealed record WfpFlowDiagnostic(
    Guid RuleId,
    int ProcessId,
    string DisplayName,
    string Direction,
    string OriginalFlow,
    string EffectiveFlow,
    string Decision,
    DateTimeOffset LastSeenUtc);

public sealed record WfpBackendDiagnostics(
    WfpBackendInstallState InstallState,
    bool DriverServiceInstalled,
    bool BridgeReachable,
    bool FiltersEnabled,
    string DriverServiceName,
    string DriverDisplayName,
    string DriverBinaryPath,
    string BridgeBinaryPath,
    int RegisteredRuleCount,
    int ActiveFlowCount,
    IReadOnlyList<WfpRuleDiagnostic> RegisteredRules,
    IReadOnlyList<WfpFlowDiagnostic> ActiveFlows,
    IReadOnlyList<string> Messages,
    DateTimeOffset UpdatedAtUtc)
{
    public static WfpBackendDiagnostics CreateDefault(
        string driverServiceName = "AppTunnelWfp",
        string driverDisplayName = "App Tunnel WFP Driver",
        string? driverBinaryPath = null,
        string? bridgeBinaryPath = null) =>
        new(
            WfpBackendInstallState.NotInstalled,
            DriverServiceInstalled: false,
            BridgeReachable: false,
            FiltersEnabled: false,
            driverServiceName,
            driverDisplayName,
            driverBinaryPath ?? string.Empty,
            bridgeBinaryPath ?? string.Empty,
            RegisteredRuleCount: 0,
            ActiveFlowCount: 0,
            RegisteredRules: [],
            ActiveFlows: [],
            Messages: [],
            UpdatedAtUtc: DateTimeOffset.UtcNow);
}

public sealed record WfpOperationResult(
    bool Succeeded,
    string Operation,
    string Message,
    WfpBackendDiagnostics Diagnostics,
    DateTimeOffset CompletedAtUtc);

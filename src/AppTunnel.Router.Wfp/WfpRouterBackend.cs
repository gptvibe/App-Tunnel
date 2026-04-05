using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Router.Wfp;

public sealed class WfpRouterBackend(IWfpBackendControl wfpBackendControl) : IRouterBackend
{
    private RouterDiagnosticsSnapshot _diagnostics = RouterDiagnosticsSnapshot.CreateDefault(RoutingBackendKind.Wfp);

    public RoutingBackendKind Kind => RoutingBackendKind.Wfp;

    public string DisplayName => "WFP Routing Backend";

    public BackendReadiness Readiness => BackendReadiness.ProductionReady;

    public bool RequiresElevation => true;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task<RouterApplyResult> ApplyRoutingPlanAsync(RoutingPlan routingPlan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);
        var hasConnectedTunnel = routingPlan.TunnelStatuses.Any(status => status.State == TunnelConnectionState.Connected);

        var selectedRules = routingPlan.AppRules
            .Where(rule => rule.IsEnabled && rule.ProfileId.HasValue)
            .Select(rule => new WfpAppRuleRegistration(
                rule.Id,
                rule.AppKind,
                rule.DisplayName,
                rule.ExecutablePath,
                rule.PackageFamilyName,
                rule.PackageIdentity,
                rule.ProfileId!.Value,
                rule.KillAppTrafficOnTunnelDrop,
                rule.IncludeChildProcesses))
            .ToArray();

        var installResult = await wfpBackendControl.InstallAsync(cancellationToken);
        if (!installResult.Succeeded)
        {
            _diagnostics = CreateDiagnosticsSnapshot(routingPlan, installResult.Diagnostics, ["WFP backend install failed."]);
            return new RouterApplyResult(Kind, installResult.Message, _diagnostics);
        }

        var backendDiagnostics = installResult.Diagnostics;
        var installedRuleIds = backendDiagnostics.RegisteredRules
            .Select(rule => rule.RuleId)
            .ToHashSet();
        var selectedRuleIds = selectedRules
            .Select(rule => rule.RuleId)
            .ToHashSet();

        foreach (var existingRuleId in installedRuleIds.Where(ruleId => !selectedRuleIds.Contains(ruleId)))
        {
            var removeResult = await wfpBackendControl.RemoveAppRuleAsync(existingRuleId, cancellationToken);
            backendDiagnostics = removeResult.Diagnostics;
        }

        foreach (var selectedRule in selectedRules)
        {
            var addResult = await wfpBackendControl.AddAppRuleAsync(selectedRule, cancellationToken);
            backendDiagnostics = addResult.Diagnostics;
        }

        var tunnelStateResult = await wfpBackendControl.SetTunnelStateAsync(hasConnectedTunnel, cancellationToken);
        backendDiagnostics = tunnelStateResult.Diagnostics;

        var filtersShouldBeEnabled = selectedRules.Length > 0;
        var filterResult = await wfpBackendControl.SetFiltersEnabledAsync(filtersShouldBeEnabled, cancellationToken);
        backendDiagnostics = filterResult.Diagnostics;

        var errors = new List<string>();
        if (!backendDiagnostics.DriverServiceInstalled)
        {
            errors.Add("The WFP driver service is not installed.");
        }

        if (selectedRules.Length > 0 && !backendDiagnostics.FiltersEnabled)
        {
            errors.Add("WFP filters are disabled, so selected applications are not being enforced.");
        }

        if (selectedRules.Any(rule => rule.KillAppTrafficOnTunnelDrop)
            && !hasConnectedTunnel)
        {
            errors.Add("Leak prevention remains armed: selected kill-on-drop applications stay blocked until a tunnel reconnects.");
        }

        _diagnostics = CreateDiagnosticsSnapshot(routingPlan, backendDiagnostics, errors);

        var state = selectedRules.Length == 0
            ? "WFP backend is idle because no app rules are assigned."
            : backendDiagnostics.FiltersEnabled && hasConnectedTunnel
                ? $"WFP backend enforcing {selectedRules.Length} selected app rule(s)."
                : backendDiagnostics.FiltersEnabled
                    ? $"WFP backend staged {selectedRules.Length} selected app rule(s) and is holding leak-prevention policy until a tunnel is active."
                    : $"WFP backend staged {selectedRules.Length} selected app rule(s) but filters are not active.";

        return new RouterApplyResult(Kind, state, _diagnostics);
    }

    public RouterDiagnosticsSnapshot GetDiagnosticsSnapshot() => _diagnostics;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await wfpBackendControl.SetTunnelStateAsync(false, cancellationToken);
        var result = await wfpBackendControl.SetFiltersEnabledAsync(false, cancellationToken);
        _diagnostics = RouterDiagnosticsSnapshot.CreateDefault(RoutingBackendKind.Wfp) with
        {
            ErrorStates = result.Succeeded ? [] : [result.Message],
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static RouterDiagnosticsSnapshot CreateDiagnosticsSnapshot(
        RoutingPlan routingPlan,
        WfpBackendDiagnostics diagnostics,
        IReadOnlyList<string> extraErrors)
    {
        var activeTunnel = routingPlan.TunnelStatuses
            .Where(status => status.State == TunnelConnectionState.Connected)
            .Join(
                routingPlan.Profiles,
                status => status.ProfileId,
                profile => profile.Id,
                (status, profile) => $"{profile.DisplayName} ({status.BackendName})")
            .DefaultIfEmpty("None")
            .First();

        return new RouterDiagnosticsSnapshot(
            RoutingBackendKind.Wfp,
            RequiresElevation: true,
            IsElevated: Environment.UserInteractive ? IsAdministrator() : true,
            activeTunnel,
            SelectedProcesses: diagnostics.RegisteredRules
                .Select(rule => new SelectedProcessActivity(
                    ProcessId: 0,
                    rule.RuleId,
                    rule.DisplayName,
                    rule.MatchDescriptor,
                    rule.TunnelDescriptor,
                    diagnostics.FiltersEnabled ? "Registered" : "Queued",
                    rule.UpdatedAtUtc))
                .ToArray(),
            MappedFlows: diagnostics.ActiveFlows
                .Select(flow => new FlowMappingSnapshot(
                    flow.RuleId,
                    flow.ProcessId,
                    flow.DisplayName,
                    flow.OriginalFlow,
                    flow.EffectiveFlow,
                    activeTunnel,
                    flow.Decision,
                    flow.LastSeenUtc))
                .ToArray(),
            DroppedPackets: diagnostics.Messages
                .Where(message => message.Contains("drop", StringComparison.OrdinalIgnoreCase))
                .Select(message => new PacketDropCounter(message, 1))
                .ToArray(),
            ErrorStates: diagnostics.Messages
                .Concat(extraErrors)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            UpdatedAtUtc: diagnostics.UpdatedAtUtc);
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}

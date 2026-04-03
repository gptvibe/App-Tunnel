using System.Globalization;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Service.Runtime;

public sealed class RouterManager(
    IStructuredLogService structuredLogService,
    IEnumerable<IRouterBackend> routerBackends)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IReadOnlyDictionary<RoutingBackendKind, IRouterBackend> _backends = routerBackends
        .GroupBy(backend => backend.Kind)
        .ToDictionary(group => group.Key, group => group.Last());

    private RoutingBackendKind? _currentBackendKind;

    public RoutingBackendKind ActiveBackend { get; private set; } = RoutingBackendKind.DryRun;

    public string State { get; private set; } = "Router manager is running in dry-run mode.";

    public RouterDiagnosticsSnapshot Diagnostics { get; private set; } =
        RouterDiagnosticsSnapshot.CreateDefault(RoutingBackendKind.DryRun);

    public async Task ApplyAsync(RoutingPlan routingPlan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (routingPlan.PreferredBackend == RoutingBackendKind.DryRun)
            {
                await StopCurrentBackendAsync(cancellationToken);
                SetDryRunState(routingPlan);
                return;
            }

            if (!_backends.TryGetValue(routingPlan.PreferredBackend, out var backend))
            {
                await StopCurrentBackendAsync(cancellationToken);
                SetErrorState(
                    routingPlan.PreferredBackend,
                    $"Requested routing backend '{routingPlan.PreferredBackend}' is not registered.");
                return;
            }

            if (_currentBackendKind.HasValue && _currentBackendKind.Value != backend.Kind)
            {
                await StopCurrentBackendAsync(cancellationToken);
            }

            await backend.InitializeAsync(cancellationToken);

            var result = await backend.ApplyRoutingPlanAsync(routingPlan, cancellationToken);
            _currentBackendKind = backend.Kind;
            ActiveBackend = result.ActiveBackend;
            State = result.State;
            Diagnostics = result.Diagnostics;

            await structuredLogService.WriteAsync(
                "Information",
                nameof(RouterManager),
                "Applied routing plan.",
                new Dictionary<string, string>
                {
                    ["preferredBackend"] = routingPlan.PreferredBackend.ToString(),
                    ["activeBackend"] = ActiveBackend.ToString(),
                    ["appRuleCount"] = routingPlan.AppRules.Count.ToString(CultureInfo.InvariantCulture),
                    ["profileCount"] = routingPlan.Profiles.Count.ToString(CultureInfo.InvariantCulture),
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            SetErrorState(routingPlan.PreferredBackend, ex.Message);

            await structuredLogService.WriteAsync(
                "Error",
                nameof(RouterManager),
                "Applying routing plan failed.",
                new Dictionary<string, string>
                {
                    ["preferredBackend"] = routingPlan.PreferredBackend.ToString(),
                    ["error"] = ex.Message,
                },
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await StopCurrentBackendAsync(cancellationToken);
            SetDryRunState(null);

            await structuredLogService.WriteAsync(
                "Information",
                nameof(RouterManager),
                "Router manager stopped.",
                properties: null,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopCurrentBackendAsync(CancellationToken cancellationToken)
    {
        if (_currentBackendKind.HasValue
            && _backends.TryGetValue(_currentBackendKind.Value, out var backend))
        {
            await backend.StopAsync(cancellationToken);
        }

        _currentBackendKind = null;
    }

    private void SetDryRunState(RoutingPlan? routingPlan)
    {
        ActiveBackend = RoutingBackendKind.DryRun;
        State = routingPlan is null
            ? "Router manager stopped."
            : routingPlan.AppRules.Count == 0
                ? "Dry-run backend active with no selected app rules."
                : $"Dry-run backend active with {routingPlan.AppRules.Count} app rule(s).";

        Diagnostics = RouterDiagnosticsSnapshot.CreateDefault(RoutingBackendKind.DryRun) with
        {
            ActiveTunnel = routingPlan is null
                ? "None"
                : DescribeActiveTunnel(routingPlan),
            ErrorStates = routingPlan is null
                ? []
                : ["Dry-run backend does not enforce per-app routing."],
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private void SetErrorState(RoutingBackendKind backendKind, string error)
    {
        ActiveBackend = backendKind;
        State = $"Routing backend '{backendKind}' failed: {error}";
        Diagnostics = RouterDiagnosticsSnapshot.CreateDefault(backendKind) with
        {
            ErrorStates = [error],
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static string DescribeActiveTunnel(RoutingPlan routingPlan)
    {
        var activeProfile = routingPlan.TunnelStatuses
            .FirstOrDefault(status => status.State == TunnelConnectionState.Connected);

        if (activeProfile is null)
        {
            return "None";
        }

        var profile = routingPlan.Profiles.FirstOrDefault(candidate => candidate.Id == activeProfile.ProfileId);
        return profile is null
            ? activeProfile.ProfileId.ToString("D")
            : $"{profile.DisplayName} ({activeProfile.BackendName})";
    }
}

using System.Globalization;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Service.Runtime;

public sealed class DryRunRouterManager(IStructuredLogService structuredLogService)
{
    public RoutingBackendKind ActiveBackend { get; private set; } = RoutingBackendKind.DryRun;

    public int LoadedRuleCount { get; private set; }

    public string State { get; private set; } = "Dry-run router manager has not loaded any app rules yet.";

    public async Task LoadAsync(
        RoutingBackendKind preferredBackend,
        IReadOnlyList<AppRule> appRules,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LoadedRuleCount = appRules.Count;
        ActiveBackend = RoutingBackendKind.DryRun;
        State = preferredBackend == RoutingBackendKind.DryRun
            ? $"Dry-run router manager loaded {LoadedRuleCount} app rule(s)."
            : $"Requested routing backend '{preferredBackend}', but the scaffold is running the dry-run router with {LoadedRuleCount} rule(s).";

        await structuredLogService.WriteAsync(
            "Information",
            nameof(DryRunRouterManager),
            "Loaded app rules into the dry-run router manager.",
            new Dictionary<string, string>
            {
                ["preferredBackend"] = preferredBackend.ToString(),
                ["loadedRuleCount"] = LoadedRuleCount.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        State = "Dry-run router manager stopped.";
        ActiveBackend = RoutingBackendKind.DryRun;

        await structuredLogService.WriteAsync(
            "Information",
            nameof(DryRunRouterManager),
            "Dry-run router manager stopped.",
            properties: null,
            cancellationToken);
    }
}

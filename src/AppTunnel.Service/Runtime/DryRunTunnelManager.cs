using System.Globalization;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Service.Runtime;

public sealed class DryRunTunnelManager(IStructuredLogService structuredLogService)
{
    public int LoadedProfileCount { get; private set; }

    public string State { get; private set; } = "Dry-run tunnel manager has not loaded any profiles yet.";

    public async Task LoadAsync(IReadOnlyList<TunnelProfile> profiles, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LoadedProfileCount = profiles.Count;
        State = LoadedProfileCount == 0
            ? "Dry-run tunnel manager loaded zero profiles."
            : $"Dry-run tunnel manager loaded {LoadedProfileCount} profile(s). No live VPN sessions are started in this scaffold.";

        await structuredLogService.WriteAsync(
            "Information",
            nameof(DryRunTunnelManager),
            "Loaded tunnel profiles into the dry-run manager.",
            new Dictionary<string, string>
            {
                ["profileCount"] = LoadedProfileCount.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        State = "Dry-run tunnel manager stopped.";

        await structuredLogService.WriteAsync(
            "Information",
            nameof(DryRunTunnelManager),
            "Dry-run tunnel manager stopped.",
            properties: null,
            cancellationToken);
    }
}

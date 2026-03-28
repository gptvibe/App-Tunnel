using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Vpn.WireGuard;

public sealed class WireGuardTunnelEngine : ITunnelEngine
{
    public TunnelKind TunnelKind => TunnelKind.WireGuard;

    public string DisplayName => "WireGuard";

    public BackendReadiness Readiness => BackendReadiness.DryRun;

    public Task<TunnelProfile> ImportProfileAsync(ProfileImportRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Path.GetExtension(request.SourcePath).Equals(".conf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("WireGuard imports require a .conf profile.");
        }

        return Task.FromResult(new TunnelProfile(
            Guid.NewGuid(),
            request.DisplayName,
            TunnelKind,
            request.SourcePath,
            secretReferenceId: null,
            isEnabled: true,
            importedAtUtc: DateTimeOffset.UtcNow));
    }

    public Task ConnectAsync(Guid profileId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DisconnectAsync(Guid profileId, CancellationToken cancellationToken) => Task.CompletedTask;
}

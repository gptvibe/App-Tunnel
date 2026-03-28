using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Vpn.WireGuard;

public sealed class WireGuardTunnelEngine : ITunnelEngine
{
    public VpnProviderKind ProviderKind => VpnProviderKind.WireGuard;

    public string DisplayName => "WireGuard";

    public BackendReadiness Readiness => BackendReadiness.Mvp;

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
            ProviderKind,
            request.SourcePath,
            secretReferenceId: null,
            importedAtUtc: DateTimeOffset.UtcNow));
    }

    public Task ConnectAsync(Guid profileId, CancellationToken cancellationToken) =>
        throw new NotImplementedException("TODO: Launch and supervise WireGuard tunnel sessions.");

    public Task DisconnectAsync(Guid profileId, CancellationToken cancellationToken) =>
        throw new NotImplementedException("TODO: Tear down WireGuard tunnel sessions and adapters.");
}
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Vpn.OpenVpn;

public sealed class OpenVpnTunnelEngine : ITunnelEngine
{
    public TunnelKind TunnelKind => TunnelKind.OpenVpn;

    public string DisplayName => "OpenVPN";

    public BackendReadiness Readiness => BackendReadiness.DryRun;

    public Task<TunnelProfile> ImportProfileAsync(ProfileImportRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Path.GetExtension(request.SourcePath).Equals(".ovpn", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OpenVPN imports require a .ovpn profile.");
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

using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Vpn.OpenVpn;

public sealed class OpenVpnTunnelEngine : ITunnelEngine
{
    public VpnProviderKind ProviderKind => VpnProviderKind.OpenVpn;

    public string DisplayName => "OpenVPN";

    public BackendReadiness Readiness => BackendReadiness.Planned;

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
            ProviderKind,
            request.SourcePath,
            secretReferenceId: null,
            importedAtUtc: DateTimeOffset.UtcNow));
    }

    public Task ConnectAsync(Guid profileId, CancellationToken cancellationToken) =>
        throw new NotImplementedException("TODO: Launch and supervise OpenVPN processes.");

    public Task DisconnectAsync(Guid profileId, CancellationToken cancellationToken) =>
        throw new NotImplementedException("TODO: Tear down OpenVPN sessions and routes.");
}
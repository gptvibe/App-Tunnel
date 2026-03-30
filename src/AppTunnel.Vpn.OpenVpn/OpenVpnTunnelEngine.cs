using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Vpn.OpenVpn;

public sealed class OpenVpnTunnelEngine : ITunnelEngine
{
    public TunnelKind TunnelKind => TunnelKind.OpenVpn;

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
            TunnelKind,
            request.SourcePath,
            secretReferenceId: null,
            isEnabled: true,
            importedAtUtc: DateTimeOffset.UtcNow));
    }

    public Task<TunnelStatusSnapshot> ConnectAsync(TunnelProfile profile, CancellationToken cancellationToken) =>
        throw new NotSupportedException("OpenVPN is not implemented yet.");

    public Task<TunnelStatusSnapshot> DisconnectAsync(TunnelProfile profile, CancellationToken cancellationToken) =>
        throw new NotSupportedException("OpenVPN is not implemented yet.");

    public Task<TunnelStatusSnapshot> GetStatusAsync(TunnelProfile profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new TunnelStatusSnapshot(
            profile.Id,
            TunnelConnectionState.Disconnected,
            "OpenVPN backend is not implemented yet.",
            ErrorMessage: null,
            BackendName: DisplayName,
            IsMock: true,
            UpdatedAtUtc: DateTimeOffset.UtcNow));
    }
}

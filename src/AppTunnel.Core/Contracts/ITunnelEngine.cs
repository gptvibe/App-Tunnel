using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Contracts;

public interface ITunnelEngine
{
    TunnelKind TunnelKind { get; }

    string DisplayName { get; }

    BackendReadiness Readiness { get; }

    Task<TunnelProfile> ImportProfileAsync(ProfileImportRequest request, CancellationToken cancellationToken);

    Task<TunnelStatusSnapshot> ConnectAsync(TunnelProfile profile, CancellationToken cancellationToken);

    Task<TunnelStatusSnapshot> DisconnectAsync(TunnelProfile profile, CancellationToken cancellationToken);

    Task<TunnelStatusSnapshot> GetStatusAsync(TunnelProfile profile, CancellationToken cancellationToken);
}

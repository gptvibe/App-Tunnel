using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Contracts;

public interface ITunnelEngine
{
    VpnProviderKind ProviderKind { get; }

    string DisplayName { get; }

    BackendReadiness Readiness { get; }

    Task<TunnelProfile> ImportProfileAsync(ProfileImportRequest request, CancellationToken cancellationToken);

    Task ConnectAsync(Guid profileId, CancellationToken cancellationToken);

    Task DisconnectAsync(Guid profileId, CancellationToken cancellationToken);
}
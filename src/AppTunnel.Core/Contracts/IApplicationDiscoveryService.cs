using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Contracts;

public interface IApplicationDiscoveryService
{
    Task<DiscoveredApplication> InspectAsync(
        AppKind appKind,
        string? executablePath,
        string? packageFamilyName,
        string? packageIdentity,
        string? displayName,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DiscoveredApplication>> EnumeratePackagedApplicationsAsync(CancellationToken cancellationToken);
}
using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Contracts;

public interface IAppTunnelControlService
{
    Task<PingReply> PingAsync(CancellationToken cancellationToken);

    Task<ServiceOverview> GetOverviewAsync(CancellationToken cancellationToken);

    Task<AppTunnelSettings> UpdateSettingsAsync(AppTunnelSettingsUpdateRequest request, CancellationToken cancellationToken);

    Task<TunnelProfile> ImportProfileAsync(ProfileImportRequest request, CancellationToken cancellationToken);

    Task<AppRule> AddAppRuleAsync(AppRuleCreateRequest request, CancellationToken cancellationToken);

    Task<AppRule> UpdateAppRuleAsync(AppRuleUpdateRequest request, CancellationToken cancellationToken);

    Task<TunnelStatusSnapshot> ConnectProfileAsync(Guid profileId, CancellationToken cancellationToken);

    Task<TunnelStatusSnapshot> DisconnectProfileAsync(Guid profileId, CancellationToken cancellationToken);

    Task<ExportedLogBundle> ExportLogBundleAsync(
        string? destinationDirectory,
        CancellationToken cancellationToken);
}

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

    Task<WfpOperationResult> InstallWfpBackendAsync(CancellationToken cancellationToken);

    Task<WfpOperationResult> UninstallWfpBackendAsync(CancellationToken cancellationToken);

    Task<WfpOperationResult> SetWfpFiltersEnabledAsync(bool isEnabled, CancellationToken cancellationToken);

    Task<WfpOperationResult> AddWfpAppRuleAsync(WfpAppRuleRegistration request, CancellationToken cancellationToken);

    Task<WfpOperationResult> RemoveWfpAppRuleAsync(Guid ruleId, CancellationToken cancellationToken);

    Task<WfpBackendDiagnostics> GetWfpDiagnosticsAsync(CancellationToken cancellationToken);
}

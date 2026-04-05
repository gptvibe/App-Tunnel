using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Contracts;

public interface IWfpBackendControl
{
    Task<WfpOperationResult> InstallAsync(CancellationToken cancellationToken);

    Task<WfpOperationResult> UninstallAsync(CancellationToken cancellationToken);

    Task<WfpOperationResult> SetFiltersEnabledAsync(bool isEnabled, CancellationToken cancellationToken);

    Task<WfpOperationResult> SetTunnelStateAsync(bool isConnected, CancellationToken cancellationToken);

    Task<WfpOperationResult> AddAppRuleAsync(WfpAppRuleRegistration request, CancellationToken cancellationToken);

    Task<WfpOperationResult> RemoveAppRuleAsync(Guid ruleId, CancellationToken cancellationToken);

    Task<WfpBackendDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken);
}

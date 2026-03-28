using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Contracts;

public interface IAppTunnelConfigurationStore
{
    Task<AppTunnelConfiguration> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppTunnelConfiguration configuration, CancellationToken cancellationToken);
}

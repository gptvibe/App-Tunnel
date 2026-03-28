using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Contracts;

public interface IAppTunnelControlService
{
    Task<PingReply> PingAsync(CancellationToken cancellationToken);

    Task<ServiceOverview> GetOverviewAsync(CancellationToken cancellationToken);
}
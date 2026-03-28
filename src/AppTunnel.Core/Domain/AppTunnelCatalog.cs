namespace AppTunnel.Core.Domain;

public sealed class AppTunnelCatalog
{
    public DistributionMode DistributionMode { get; init; } = DistributionMode.Installer;

    public RouterBackendKind PreferredRouter { get; init; } = RouterBackendKind.MvpRouter;

    public List<AppDefinition> Apps { get; } = [];

    public List<TunnelProfile> Profiles { get; } = [];

    public List<AppTunnelAssignment> Assignments { get; } = [];
}
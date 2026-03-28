using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Services;

namespace AppTunnel.Core.Tests;

public sealed class InMemoryAppTunnelControlServiceTests
{
    [Fact]
    public async Task OverviewIncludesSupportedAndPlannedBackends()
    {
        var service = new InMemoryAppTunnelControlService(
            [new FakeTunnelEngine(VpnProviderKind.WireGuard, BackendReadiness.Mvp), new FakeTunnelEngine(VpnProviderKind.OpenVpn, BackendReadiness.Planned)],
            [new FakeRouterBackend(RouterBackendKind.MvpRouter, BackendReadiness.Mvp)]);

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Contains(VpnProviderKind.WireGuard, overview.SupportedProfileKinds);
        Assert.Contains(VpnProviderKind.OpenVpn, overview.PlannedProfileKinds);
        Assert.Contains(overview.RouterBackends, status => status.BackendKind == RouterBackendKind.ProdRouter);
    }

    private sealed class FakeTunnelEngine : ITunnelEngine
    {
        public FakeTunnelEngine(VpnProviderKind providerKind, BackendReadiness readiness)
        {
            ProviderKind = providerKind;
            DisplayName = providerKind.ToString();
            Readiness = readiness;
        }

        public VpnProviderKind ProviderKind { get; }

        public string DisplayName { get; }

        public BackendReadiness Readiness { get; }

        public Task<TunnelProfile> ImportProfileAsync(ProfileImportRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new TunnelProfile(Guid.NewGuid(), request.DisplayName, ProviderKind, request.SourcePath, null, DateTimeOffset.UtcNow));

        public Task ConnectAsync(Guid profileId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DisconnectAsync(Guid profileId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRouterBackend : IRouterBackend
    {
        public FakeRouterBackend(RouterBackendKind kind, BackendReadiness readiness)
        {
            Kind = kind;
            DisplayName = kind.ToString();
            Readiness = readiness;
        }

        public RouterBackendKind Kind { get; }

        public string DisplayName { get; }

        public BackendReadiness Readiness { get; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ApplyRoutingPlanAsync(RoutingPlan routingPlan, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
using AppTunnel.Core.Domain;

namespace AppTunnel.Vpn.WireGuard;

public sealed class MockWireGuardBackend : IWireGuardBackend
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, TunnelConnectionState> _states = [];

    public string BackendName => "Mock WireGuard backend";

    public BackendReadiness Readiness => BackendReadiness.DryRun;

    public bool IsMock => true;

    public async Task<WireGuardBackendResult> ConnectAsync(
        WireGuardServiceContext context,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _states[context.ProfileId] = TunnelConnectionState.Connected;
        }
        finally
        {
            _gate.Release();
        }

        return CreateResult(TunnelConnectionState.Connected, $"Mock tunnel '{context.DisplayName}' is connected.");
    }

    public async Task<WireGuardBackendResult> DisconnectAsync(
        WireGuardServiceContext context,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _states[context.ProfileId] = TunnelConnectionState.Disconnected;
        }
        finally
        {
            _gate.Release();
        }

        return CreateResult(TunnelConnectionState.Disconnected, $"Mock tunnel '{context.DisplayName}' is disconnected.");
    }

    public async Task<WireGuardBackendResult> GetStatusAsync(
        WireGuardServiceContext context,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var state = _states.GetValueOrDefault(context.ProfileId, TunnelConnectionState.Disconnected);
            var summary = state == TunnelConnectionState.Connected
                ? $"Mock tunnel '{context.DisplayName}' is connected."
                : $"Mock tunnel '{context.DisplayName}' is disconnected.";

            return CreateResult(state, summary);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static WireGuardBackendResult CreateResult(TunnelConnectionState state, string summary) =>
        new(
            state,
            summary,
            ErrorMessage: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
}
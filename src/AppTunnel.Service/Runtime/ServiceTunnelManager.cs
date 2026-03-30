using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Service.Runtime;

public sealed class ServiceTunnelManager(IEnumerable<ITunnelEngine> tunnelEngines)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IReadOnlyDictionary<TunnelKind, ITunnelEngine> _engines = tunnelEngines
        .GroupBy(engine => engine.TunnelKind)
        .ToDictionary(group => group.Key, group => group.Last());
    private readonly Dictionary<Guid, TunnelStatusSnapshot> _statuses = [];

    public int ConnectedProfileCount { get; private set; }

    public string State { get; private set; } = "Tunnel manager has not loaded any profiles yet.";

    public async Task LoadAsync(IReadOnlyList<TunnelProfile> profiles, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _statuses.Clear();
            foreach (var profile in profiles)
            {
                _statuses[profile.Id] = await GetStatusCoreAsync(profile, cancellationToken);
            }

            UpdateState(profiles.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TunnelProfile> ImportAsync(ProfileImportRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!string.Equals(Path.GetExtension(request.SourcePath), ".conf", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only WireGuard .conf imports are supported right now.");
            }

            var engine = GetEngine(TunnelKind.WireGuard);
            var profile = await engine.ImportProfileAsync(request, cancellationToken);
            _statuses[profile.Id] = await engine.GetStatusAsync(profile, cancellationToken);
            UpdateState(_statuses.Count);
            return profile;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TunnelStatusSnapshot> ConnectAsync(TunnelProfile profile, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var status = await GetEngine(profile.TunnelKind).ConnectAsync(profile, cancellationToken);
            _statuses[profile.Id] = status;
            UpdateState(_statuses.Count);
            return status;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TunnelStatusSnapshot> DisconnectAsync(TunnelProfile profile, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var status = await GetEngine(profile.TunnelKind).DisconnectAsync(profile, cancellationToken);
            _statuses[profile.Id] = status;
            UpdateState(_statuses.Count);
            return status;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<TunnelStatusSnapshot>> RefreshStatusesAsync(
        IReadOnlyList<TunnelProfile> profiles,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            foreach (var profile in profiles)
            {
                _statuses[profile.Id] = await GetStatusCoreAsync(profile, cancellationToken);
            }

            foreach (var profileId in _statuses.Keys.Except(profiles.Select(profile => profile.Id)).ToArray())
            {
                _statuses.Remove(profileId);
            }

            UpdateState(profiles.Count);
            return _statuses.Values
                .OrderBy(status => status.ProfileId)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(IReadOnlyList<TunnelProfile> profiles, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            foreach (var profile in profiles)
            {
                if (_statuses.TryGetValue(profile.Id, out var status)
                    && status.State is not TunnelConnectionState.Connected and not TunnelConnectionState.Connecting)
                {
                    continue;
                }

                _statuses[profile.Id] = await GetEngine(profile.TunnelKind).DisconnectAsync(profile, cancellationToken);
            }

            UpdateState(profiles.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TunnelStatusSnapshot> GetStatusCoreAsync(TunnelProfile profile, CancellationToken cancellationToken) =>
        await GetEngine(profile.TunnelKind).GetStatusAsync(profile, cancellationToken);

    private ITunnelEngine GetEngine(TunnelKind tunnelKind) =>
        _engines.TryGetValue(tunnelKind, out var engine)
            ? engine
            : throw new InvalidOperationException($"No tunnel engine is registered for '{tunnelKind}'.");

    private void UpdateState(int loadedProfileCount)
    {
        ConnectedProfileCount = _statuses.Values.Count(status => status.State == TunnelConnectionState.Connected);
        var faultedCount = _statuses.Values.Count(status => status.State == TunnelConnectionState.Faulted);
        var connectingCount = _statuses.Values.Count(status =>
            status.State is TunnelConnectionState.Connecting or TunnelConnectionState.Disconnecting);

        State = loadedProfileCount == 0
            ? "Tunnel manager has no imported profiles."
            : $"Tunnel manager tracking {loadedProfileCount} profile(s): {ConnectedProfileCount} connected, {connectingCount} transitioning, {faultedCount} faulted.";
    }
}
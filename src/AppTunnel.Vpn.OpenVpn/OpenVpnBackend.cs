using AppTunnel.Core.Domain;

namespace AppTunnel.Vpn.OpenVpn;

public sealed record OpenVpnBackendOptions(
    string? OpenVpnExePath,
    int ConnectTimeoutSeconds)
{
    public TimeSpan ConnectTimeout =>
        TimeSpan.FromSeconds(Math.Clamp(ConnectTimeoutSeconds, 5, 120));
}

public sealed record OpenVpnServiceContext(
    Guid ProfileId,
    string DisplayName,
    string RuntimeDirectory,
    string ConfigPath);

public sealed record OpenVpnBackendResult(
    TunnelConnectionState State,
    string Summary,
    string? ErrorMessage,
    DateTimeOffset UpdatedAtUtc);

public interface IOpenVpnBackend
{
    string BackendName { get; }

    BackendReadiness Readiness { get; }

    bool IsMock { get; }

    Task<OpenVpnBackendResult> ConnectAsync(OpenVpnServiceContext context, CancellationToken cancellationToken);

    Task<OpenVpnBackendResult> DisconnectAsync(OpenVpnServiceContext context, CancellationToken cancellationToken);

    Task<OpenVpnBackendResult> GetStatusAsync(OpenVpnServiceContext context, CancellationToken cancellationToken);
}

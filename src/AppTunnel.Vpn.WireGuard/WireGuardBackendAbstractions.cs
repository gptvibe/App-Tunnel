using AppTunnel.Core.Domain;

namespace AppTunnel.Vpn.WireGuard;

public enum WireGuardBackendMode
{
    Auto,
    OfficialService,
    Mock,
}

public sealed record WireGuardBackendOptions(
    WireGuardBackendMode Mode,
    string? WireGuardExePath);

public interface IWireGuardBackend
{
    string BackendName { get; }

    BackendReadiness Readiness { get; }

    bool IsMock { get; }

    Task<WireGuardBackendResult> ConnectAsync(
        WireGuardServiceContext context,
        CancellationToken cancellationToken);

    Task<WireGuardBackendResult> DisconnectAsync(
        WireGuardServiceContext context,
        CancellationToken cancellationToken);

    Task<WireGuardBackendResult> GetStatusAsync(
        WireGuardServiceContext context,
        CancellationToken cancellationToken);
}

public sealed record WireGuardServiceContext(
    Guid ProfileId,
    string DisplayName,
    string TunnelName,
    string ServiceName,
    string ConfigPath);

public sealed record WireGuardBackendResult(
    TunnelConnectionState State,
    string Summary,
    string? ErrorMessage,
    DateTimeOffset UpdatedAtUtc);

public static class WireGuardBackendFactory
{
    public static IWireGuardBackend Create(WireGuardBackendOptions options)
    {
        var resolvedMode = options.Mode;
        var resolvedExePath = ResolveWireGuardExePath(options.WireGuardExePath);

        if (resolvedMode == WireGuardBackendMode.Mock)
        {
            return new MockWireGuardBackend();
        }

        if (resolvedMode == WireGuardBackendMode.OfficialService)
        {
            if (string.IsNullOrWhiteSpace(resolvedExePath))
            {
                throw new InvalidOperationException(
                    "WireGuard official-service mode requires wireguard.exe. Set AppTunnel:WireGuard:WireGuardExePath or install WireGuard for Windows.");
            }

            return new OfficialWireGuardServiceBackend(resolvedExePath);
        }

        return string.IsNullOrWhiteSpace(resolvedExePath)
            ? new MockWireGuardBackend()
            : new OfficialWireGuardServiceBackend(resolvedExePath);
    }

    private static string? ResolveWireGuardExePath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullConfiguredPath = Path.GetFullPath(configuredPath);
            return File.Exists(fullConfiguredPath) ? fullConfiguredPath : null;
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WireGuard", "wireguard.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
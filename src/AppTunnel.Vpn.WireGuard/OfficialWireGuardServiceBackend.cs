using System.Diagnostics;
using System.ServiceProcess;
using AppTunnel.Core.Domain;

namespace AppTunnel.Vpn.WireGuard;

public sealed class OfficialWireGuardServiceBackend(string wireGuardExePath) : IWireGuardBackend
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(15);

    public string BackendName => "Official WireGuard tunnel service";

    public BackendReadiness Readiness => BackendReadiness.Mvp;

    public bool IsMock => false;

    public async Task<WireGuardBackendResult> ConnectAsync(
        WireGuardServiceContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryGetController(context.ServiceName, out var controller) && controller is not null)
        {
            using (controller)
            {
                controller.Refresh();

                if (controller.Status == ServiceControllerStatus.Running)
                {
                    return CreateResult(TunnelConnectionState.Connected, $"WireGuard tunnel service '{context.TunnelName}' is already running.");
                }

                if (controller.Status != ServiceControllerStatus.StartPending)
                {
                    controller.Start();
                }

                await WaitForStatusAsync(context.ServiceName, ServiceControllerStatus.Running, cancellationToken);
                return CreateResult(TunnelConnectionState.Connected, $"WireGuard tunnel service '{context.TunnelName}' is running.");
            }
        }

        await RunWireGuardAsync("/installtunnelservice", context.ConfigPath, cancellationToken);
        await WaitForStatusAsync(context.ServiceName, ServiceControllerStatus.Running, cancellationToken);

        return CreateResult(TunnelConnectionState.Connected, $"WireGuard tunnel service '{context.TunnelName}' started.");
    }

    public async Task<WireGuardBackendResult> DisconnectAsync(
        WireGuardServiceContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetController(context.ServiceName, out var controller) || controller is null)
        {
            DeleteConfigFile(context.ConfigPath);
            return CreateResult(TunnelConnectionState.Disconnected, $"WireGuard tunnel service '{context.TunnelName}' is not installed.");
        }

        using (controller)
        {
            controller.Refresh();
        }

        await RunWireGuardAsync("/uninstalltunnelservice", context.TunnelName, cancellationToken);
        await WaitForRemovalAsync(context.ServiceName, cancellationToken);
        DeleteConfigFile(context.ConfigPath);

        return CreateResult(TunnelConnectionState.Disconnected, $"WireGuard tunnel service '{context.TunnelName}' stopped.");
    }

    public Task<WireGuardBackendResult> GetStatusAsync(
        WireGuardServiceContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetController(context.ServiceName, out var controller) || controller is null)
        {
            return Task.FromResult(CreateResult(
                TunnelConnectionState.Disconnected,
                $"WireGuard tunnel service '{context.TunnelName}' is not installed."));
        }

        using (controller)
        {
            controller.Refresh();

            var state = controller.Status switch
            {
                ServiceControllerStatus.Running => TunnelConnectionState.Connected,
                ServiceControllerStatus.StartPending => TunnelConnectionState.Connecting,
                ServiceControllerStatus.StopPending => TunnelConnectionState.Disconnecting,
                ServiceControllerStatus.Stopped => TunnelConnectionState.Disconnected,
                _ => TunnelConnectionState.Unknown,
            };

            var summary = $"WireGuard tunnel service '{context.TunnelName}' is {controller.Status}.";
            return Task.FromResult(CreateResult(state, summary));
        }
    }

    private async Task RunWireGuardAsync(string command, string argument, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(wireGuardExePath)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {Path.GetFileName(wireGuardExePath)}.");

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode == 0)
        {
            return;
        }

        var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
        var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorText = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;

        throw new InvalidOperationException(
            $"WireGuard command '{command}' failed with exit code {process.ExitCode}: {errorText.Trim()}".Trim());
    }

    private static bool TryGetController(string serviceName, out ServiceController? controller)
    {
        controller = null;

        try
        {
            controller = new ServiceController(serviceName);
            _ = controller.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            controller?.Dispose();
            controller = null;
            return false;
        }
    }

    private static async Task WaitForStatusAsync(
        string serviceName,
        ServiceControllerStatus desiredStatus,
        CancellationToken cancellationToken)
    {
        var deadlineUtc = DateTimeOffset.UtcNow.Add(StatusTimeout);

        while (DateTimeOffset.UtcNow <= deadlineUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetController(serviceName, out var controller) && controller is not null)
            {
                using (controller)
                {
                    controller.Refresh();
                    if (controller.Status == desiredStatus)
                    {
                        return;
                    }
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new System.TimeoutException($"Timed out waiting for service '{serviceName}' to reach status '{desiredStatus}'.");
    }

    private static async Task WaitForRemovalAsync(string serviceName, CancellationToken cancellationToken)
    {
        var deadlineUtc = DateTimeOffset.UtcNow.Add(StatusTimeout);

        while (DateTimeOffset.UtcNow <= deadlineUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetController(serviceName, out var controller) || controller is null)
            {
                return;
            }

            using (controller)
            {
                controller.Refresh();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new System.TimeoutException($"Timed out waiting for service '{serviceName}' to be removed.");
    }

    private static void DeleteConfigFile(string configPath)
    {
        if (File.Exists(configPath))
        {
            File.Delete(configPath);
        }
    }

    private static WireGuardBackendResult CreateResult(TunnelConnectionState state, string summary) =>
        new(
            state,
            summary,
            ErrorMessage: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
}
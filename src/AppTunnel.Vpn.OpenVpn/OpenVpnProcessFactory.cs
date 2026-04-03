using System.Diagnostics;

namespace AppTunnel.Vpn.OpenVpn;

public sealed record OpenVpnProcessStartInfo(
    string FileName,
    string Arguments,
    string WorkingDirectory);

public sealed record OpenVpnProcessOutputLine(
    string StreamName,
    string Line);

public interface IOpenVpnProcess : IAsyncDisposable
{
    int Id { get; }

    bool HasExited { get; }

    int? ExitCode { get; }

    void Kill(bool entireProcessTree);

    Task WaitForExitAsync(CancellationToken cancellationToken);
}

public interface IOpenVpnProcessFactory
{
    IOpenVpnProcess Start(
        OpenVpnProcessStartInfo startInfo,
        Action<OpenVpnProcessOutputLine> onOutput,
        Action<int> onExited);
}

public sealed class SystemOpenVpnProcessFactory : IOpenVpnProcessFactory
{
    public IOpenVpnProcess Start(
        OpenVpnProcessStartInfo startInfo,
        Action<OpenVpnProcessOutputLine> onOutput,
        Action<int> onExited)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(onOutput);
        ArgumentNullException.ThrowIfNull(onExited);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = startInfo.FileName,
                Arguments = startInfo.Arguments,
                WorkingDirectory = startInfo.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                onOutput(new OpenVpnProcessOutputLine("stdout", args.Data));
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                onOutput(new OpenVpnProcessOutputLine("stderr", args.Data));
            }
        };
        process.Exited += (_, _) => onExited(process.ExitCode);

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The OpenVPN process could not be started.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return new SystemOpenVpnProcess(process);
    }

    private sealed class SystemOpenVpnProcess(Process process) : IOpenVpnProcess
    {
        public int Id => process.Id;

        public bool HasExited => process.HasExited;

        public int? ExitCode => process.HasExited ? process.ExitCode : null;

        public void Kill(bool entireProcessTree)
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree);
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

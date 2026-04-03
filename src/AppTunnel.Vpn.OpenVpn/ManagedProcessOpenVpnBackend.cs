using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Vpn.OpenVpn;

public sealed class ManagedProcessOpenVpnBackend(
    IStructuredLogService structuredLogService,
    OpenVpnBackendOptions options,
    IOpenVpnProcessFactory processFactory) : IOpenVpnBackend
{
    private static readonly string[] KnownExecutableLocations =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenVPN", "bin", "openvpn.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenVPN", "bin", "openvpn.exe"),
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, ManagedSession> _sessions = [];

    public string BackendName => "OpenVPN (managed process)";

    public BackendReadiness Readiness =>
        ResolveExecutablePath() is null
            ? BackendReadiness.Planned
            : BackendReadiness.Mvp;

    public bool IsMock => false;

    public async Task<OpenVpnBackendResult> ConnectAsync(OpenVpnServiceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ManagedSession session;
        OpenVpnBackendResult initialResult;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_sessions.TryGetValue(context.ProfileId, out session!)
                && session.IsAlive)
            {
                return session.Current;
            }

            var executablePath = ResolveExecutablePath()
                ?? throw new FileNotFoundException(
                    "OpenVPN support requires openvpn.exe. Configure AppTunnel:OpenVpn:OpenVpnExePath or install the community OpenVPN runtime.",
                    options.OpenVpnExePath ?? KnownExecutableLocations[0]);

            Directory.CreateDirectory(context.RuntimeDirectory);
            session = new ManagedSession(context);
            _sessions[context.ProfileId] = session;

            var process = processFactory.Start(
                new OpenVpnProcessStartInfo(
                    executablePath,
                    $"--config \"{context.ConfigPath}\" --verb 3 --auth-nocache --suppress-timestamps",
                    context.RuntimeDirectory),
                output => OnProcessOutput(session, output),
                exitCode => OnProcessExited(session, exitCode));
            session.AttachProcess(process);

            initialResult = session.Current;
        }
        finally
        {
            _gate.Release();
        }

        await WriteLifecycleLogAsync(
            "Information",
            session.Context,
            "OpenVPN process launched.",
            new Dictionary<string, string>
            {
                ["state"] = initialResult.State.ToString(),
                ["summary"] = initialResult.Summary,
                ["processId"] = session.ProcessId?.ToString() ?? "unknown",
            });

        var completedTask = await Task.WhenAny(
            session.ConnectionTransition.Task,
            Task.Delay(options.ConnectTimeout, cancellationToken));

        return completedTask == session.ConnectionTransition.Task
            ? await session.ConnectionTransition.Task
            : session.Current;
    }

    public async Task<OpenVpnBackendResult> DisconnectAsync(OpenVpnServiceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ManagedSession? session;
        IOpenVpnProcess? process = null;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!_sessions.TryGetValue(context.ProfileId, out session))
            {
                return CreateResult(TunnelConnectionState.Disconnected, "OpenVPN tunnel is not running.", errorMessage: null);
            }

            session.MarkStopping();
            process = session.Process;
        }
        finally
        {
            _gate.Release();
        }

        if (process is not null && !process.HasExited)
        {
            process.Kill(entireProcessTree: true);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                session.SetState(CreateResult(
                    TunnelConnectionState.Disconnected,
                    "OpenVPN process stop requested; final exit confirmation was not available.",
                    errorMessage: null));
            }
        }

        var result = session.Current.State == TunnelConnectionState.Disconnecting
            ? CreateResult(TunnelConnectionState.Disconnected, "OpenVPN tunnel stopped.", errorMessage: null)
            : session.Current;
        session.SetState(result);

        await WriteLifecycleLogAsync(
            "Information",
            session.Context,
            "OpenVPN process stopped.",
            new Dictionary<string, string>
            {
                ["state"] = result.State.ToString(),
                ["summary"] = result.Summary,
            });

        return result;
    }

    public async Task<OpenVpnBackendResult> GetStatusAsync(OpenVpnServiceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            return _sessions.TryGetValue(context.ProfileId, out var session)
                ? session.Current
                : CreateResult(TunnelConnectionState.Disconnected, "OpenVPN tunnel is not running.", errorMessage: null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnProcessOutput(ManagedSession session, OpenVpnProcessOutputLine output)
    {
        session.RecordOutput();
        _ = WriteLifecycleLogAsync(
            output.StreamName == "stderr" ? "Warning" : "Information",
            session.Context,
            output.Line,
            new Dictionary<string, string>
            {
                ["stream"] = output.StreamName,
                ["state"] = session.Current.State.ToString(),
            });

        if (Contains(output.Line, "Initialization Sequence Completed"))
        {
            session.SetState(CreateResult(TunnelConnectionState.Connected, "OpenVPN tunnel connected.", errorMessage: null));
            return;
        }

        if (Contains(output.Line, "AUTH_FAILED"))
        {
            session.SetState(CreateResult(TunnelConnectionState.Faulted, "OpenVPN authentication failed.", output.Line));
            return;
        }

        if (Contains(output.Line, "Options error")
            || Contains(output.Line, "Exiting due to fatal error")
            || Contains(output.Line, "Cannot open")
            || Contains(output.Line, "All TAP-Windows adapters")
            || Contains(output.Line, "There are no TAP-Windows"))
        {
            session.SetState(CreateResult(TunnelConnectionState.Faulted, "OpenVPN reported a startup error.", output.Line));
        }
    }

    private void OnProcessExited(ManagedSession session, int exitCode)
    {
        session.RecordExitCode(exitCode);

        OpenVpnBackendResult result;
        if (session.IsStopping)
        {
            result = CreateResult(TunnelConnectionState.Disconnected, "OpenVPN tunnel stopped.", errorMessage: null);
        }
        else if (session.Current.State == TunnelConnectionState.Faulted)
        {
            result = session.Current;
        }
        else
        {
            result = CreateResult(
                TunnelConnectionState.Faulted,
                "OpenVPN process exited unexpectedly.",
                $"openvpn.exe exited with code {exitCode}.");
        }

        session.SetState(result);

        _ = WriteLifecycleLogAsync(
            result.State == TunnelConnectionState.Disconnected ? "Information" : "Error",
            session.Context,
            "OpenVPN process exited.",
            new Dictionary<string, string>
            {
                ["state"] = result.State.ToString(),
                ["summary"] = result.Summary,
                ["exitCode"] = exitCode.ToString(),
                ["error"] = result.ErrorMessage ?? "None",
            });
    }

    private async Task WriteLifecycleLogAsync(
        string level,
        OpenVpnServiceContext context,
        string message,
        IReadOnlyDictionary<string, string> properties)
    {
        try
        {
            var enrichedProperties = new Dictionary<string, string>(properties, StringComparer.Ordinal)
            {
                ["profileId"] = context.ProfileId.ToString("D"),
                ["displayName"] = context.DisplayName,
            };

            await structuredLogService.WriteAsync(
                level,
                nameof(ManagedProcessOpenVpnBackend),
                message,
                enrichedProperties,
                CancellationToken.None);
        }
        catch
        {
            // Logging should never tear down the managed process path.
        }
    }

    private string? ResolveExecutablePath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(options.OpenVpnExePath)
            ? null
            : Path.GetFullPath(options.OpenVpnExePath);

        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        return KnownExecutableLocations.FirstOrDefault(File.Exists);
    }

    private static bool Contains(string line, string value) =>
        line.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static OpenVpnBackendResult CreateResult(
        TunnelConnectionState state,
        string summary,
        string? errorMessage) =>
        new(
            state,
            summary,
            errorMessage,
            DateTimeOffset.UtcNow);

    private sealed class ManagedSession
    {
        private readonly object _sync = new();
        private OpenVpnBackendResult _current = CreateResult(
            TunnelConnectionState.Connecting,
            "Launching OpenVPN process.",
            errorMessage: null);

        public ManagedSession(OpenVpnServiceContext context)
        {
            Context = context;
            ConnectionTransition = new TaskCompletionSource<OpenVpnBackendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public OpenVpnServiceContext Context { get; }

        public TaskCompletionSource<OpenVpnBackendResult> ConnectionTransition { get; }

        public IOpenVpnProcess? Process { get; private set; }

        public int? ProcessId { get; private set; }

        public bool IsStopping { get; private set; }

        public bool IsAlive => Process is { HasExited: false };

        public OpenVpnBackendResult Current
        {
            get
            {
                lock (_sync)
                {
                    return _current;
                }
            }
        }

        public void AttachProcess(IOpenVpnProcess process)
        {
            lock (_sync)
            {
                Process = process;
                ProcessId = process.Id;
                _current = CreateResult(
                    TunnelConnectionState.Connecting,
                    $"Launching OpenVPN process (PID {process.Id}).",
                    errorMessage: null);
            }
        }

        public void MarkStopping()
        {
            lock (_sync)
            {
                IsStopping = true;
                _current = CreateResult(
                    TunnelConnectionState.Disconnecting,
                    "Stopping OpenVPN process.",
                    errorMessage: null);
                ConnectionTransition.TrySetResult(_current);
            }
        }

        public void RecordOutput()
        {
            lock (_sync)
            {
                if (_current.State == TunnelConnectionState.Connecting)
                {
                    _current = CreateResult(
                        TunnelConnectionState.Connecting,
                        "OpenVPN process is still initializing.",
                        errorMessage: null);
                }
            }
        }

        public void RecordExitCode(int exitCode)
        {
            lock (_sync)
            {
                ProcessId ??= Process?.Id;
            }
        }

        public void SetState(OpenVpnBackendResult result)
        {
            lock (_sync)
            {
                _current = result;

                if (result.State is TunnelConnectionState.Connected or TunnelConnectionState.Faulted)
                {
                    ConnectionTransition.TrySetResult(result);
                }
            }
        }
    }
}

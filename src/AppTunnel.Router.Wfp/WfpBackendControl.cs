using System.Diagnostics;
using System.Text.Json;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;
using AppTunnel.Core.Services;

namespace AppTunnel.Router.Wfp;

public sealed class WfpBackendControl : IWfpBackendControl
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AppTunnelPaths _paths;
    private readonly WfpDeploymentPaths _deploymentPaths;

    public WfpBackendControl(AppTunnelPaths paths)
    {
        _paths = paths;
        _deploymentPaths = WfpDeploymentPaths.Resolve();
    }

    public async Task<WfpOperationResult> InstallAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!File.Exists(_deploymentPaths.DriverBinaryPath))
            {
                state = state with
                {
                    InstallState = WfpBackendInstallState.Faulted,
                    DriverServiceInstalled = false,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Messages = MergeMessages(
                        state.Messages,
                        "Install requested but no staged .sys driver binary was found. Use test-signed binaries only for local validation and stage a release-signed package for production shipping."),
                };

                await SaveStateAsync(state, cancellationToken);
                return CreateResult(false, "install", "WFP driver binary is not staged.", state);
            }

            var bridgeResult = await RunBridgeCommandAsync(
                "install",
                [_deploymentPaths.DriverBinaryPath],
                cancellationToken);

            state = await RefreshStateAfterBridgeCommandAsync(
                state,
                bridgeResult,
                bridgeResult.Succeeded
                    ? "WFP backend installed. Production release still requires EV code signing plus Microsoft attestation or HLK signing for the driver package."
                    : null,
                bridgeResult.Succeeded
                    ? WfpBackendInstallState.Installed
                    : WfpBackendInstallState.Faulted,
                cancellationToken);

            return CreateResult(bridgeResult.Succeeded, "install", bridgeResult.Message, state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WfpOperationResult> UninstallAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var state = await LoadStateAsync(cancellationToken);
            var bridgeResult = await RunBridgeCommandAsync("uninstall", [], cancellationToken);

            state = state with
            {
                InstallState = WfpBackendInstallState.NotInstalled,
                DriverServiceInstalled = false,
                FiltersEnabled = false,
                TunnelConnected = false,
                ActiveFlows = [],
                RegisteredRules = [],
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Messages = MergeMessages(
                    state.Messages,
                    bridgeResult.Message,
                    bridgeResult.Succeeded
                        ? "WFP backend state has been cleared from the service control plane."
                        : null),
            };

            await SaveStateAsync(state, cancellationToken);
            return CreateResult(bridgeResult.Succeeded, "uninstall", bridgeResult.Message, state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WfpOperationResult> SetFiltersEnabledAsync(bool isEnabled, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (isEnabled && state.InstallState != WfpBackendInstallState.Installed)
            {
                return CreateResult(false, "set-filters", "Filters cannot be enabled until the WFP backend is installed.", state);
            }

            var bridgeResult = await RunBridgeCommandAsync(
                isEnabled ? "enable-filters" : "disable-filters",
                [],
                cancellationToken);

            state = await RefreshStateAfterBridgeCommandAsync(
                state with { FiltersEnabled = isEnabled, UpdatedAtUtc = DateTimeOffset.UtcNow },
                bridgeResult,
                null,
                state.InstallState,
                cancellationToken);

            return CreateResult(
                bridgeResult.Succeeded,
                "set-filters",
                bridgeResult.Message,
                state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WfpOperationResult> SetTunnelStateAsync(bool isConnected, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (isConnected && state.InstallState != WfpBackendInstallState.Installed)
            {
                return CreateResult(false, "set-tunnel-state", "Tunnel state cannot be updated until the WFP backend is installed.", state);
            }

            var bridgeResult = await RunBridgeCommandAsync(
                "set-tunnel-state",
                [isConnected ? "connected" : "disconnected"],
                cancellationToken);

            state = await RefreshStateAfterBridgeCommandAsync(
                state with
                {
                    TunnelConnected = isConnected,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                },
                bridgeResult,
                null,
                state.InstallState,
                cancellationToken);

            return CreateResult(
                bridgeResult.Succeeded,
                "set-tunnel-state",
                bridgeResult.Message,
                state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WfpOperationResult> AddAppRuleAsync(WfpAppRuleRegistration request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var state = await LoadStateAsync(cancellationToken);
            var bridgeResult = await RunBridgeCommandAsync(
                "add-rule",
                BuildAddRuleArguments(request),
                cancellationToken);

            if (bridgeResult.Succeeded)
            {
                var diagnostics = CreateRuleDiagnostic(request);
                state = state with
                {
                    RegisteredRules = state.RegisteredRules
                        .Where(rule => rule.RuleId != request.RuleId)
                        .Append(diagnostics)
                        .OrderBy(rule => rule.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
            }

            state = await RefreshStateAfterBridgeCommandAsync(
                state,
                bridgeResult,
                null,
                state.InstallState,
                cancellationToken);

            return CreateResult(
                bridgeResult.Succeeded,
                "add-rule",
                bridgeResult.Succeeded
                    ? $"WFP rule '{request.DisplayName}' synchronized."
                    : bridgeResult.Message,
                state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WfpOperationResult> RemoveAppRuleAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var state = await LoadStateAsync(cancellationToken);
            var bridgeResult = await RunBridgeCommandAsync(
                "remove-rule",
                [ruleId.ToString("D")],
                cancellationToken);

            if (bridgeResult.Succeeded)
            {
                state = state with
                {
                    RegisteredRules = state.RegisteredRules
                        .Where(rule => rule.RuleId != ruleId)
                        .ToArray(),
                    ActiveFlows = state.ActiveFlows
                        .Where(flow => flow.RuleId != ruleId)
                        .ToArray(),
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
            }

            state = await RefreshStateAfterBridgeCommandAsync(
                state,
                bridgeResult,
                null,
                state.InstallState,
                cancellationToken);

            return CreateResult(
                bridgeResult.Succeeded,
                "remove-rule",
                bridgeResult.Succeeded
                    ? $"WFP rule '{ruleId:D}' removed."
                    : bridgeResult.Message,
                state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WfpBackendDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var state = await LoadStateAsync(cancellationToken);
            var bridgeDiagnostics = await TryGetBridgeDiagnosticsAsync(cancellationToken);
            if (bridgeDiagnostics is not null)
            {
                state = await ApplyBridgeDiagnosticsAsync(state, bridgeDiagnostics, state.InstallState, cancellationToken);
            }

            return ToDiagnostics(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WfpBackendState> RefreshStateAfterBridgeCommandAsync(
        WfpBackendState state,
        BridgeCommandResult bridgeResult,
        string? additionalMessage,
        WfpBackendInstallState fallbackInstallState,
        CancellationToken cancellationToken)
    {
        var nextState = state with
        {
            InstallState = bridgeResult.Succeeded ? fallbackInstallState : WfpBackendInstallState.Faulted,
            Messages = MergeMessages(state.Messages, bridgeResult.Message, additionalMessage),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var bridgeDiagnostics = await TryGetBridgeDiagnosticsAsync(cancellationToken);
        if (bridgeDiagnostics is not null)
        {
            nextState = ApplyBridgeDiagnostics(nextState, bridgeDiagnostics, bridgeResult.Succeeded ? fallbackInstallState : WfpBackendInstallState.Faulted);
        }

        await SaveStateAsync(nextState, cancellationToken);
        return nextState;
    }

    private async Task<WfpBackendState> ApplyBridgeDiagnosticsAsync(
        WfpBackendState state,
        BridgeDiagnosticsSnapshot bridgeDiagnostics,
        WfpBackendInstallState fallbackInstallState,
        CancellationToken cancellationToken)
    {
        var nextState = ApplyBridgeDiagnostics(state, bridgeDiagnostics, fallbackInstallState);
        await SaveStateAsync(nextState, cancellationToken);
        return nextState;
    }

    private WfpBackendState ApplyBridgeDiagnostics(
        WfpBackendState state,
        BridgeDiagnosticsSnapshot bridgeDiagnostics,
        WfpBackendInstallState fallbackInstallState)
    {
        var installState = bridgeDiagnostics.DriverServiceInstalled
            ? fallbackInstallState
            : state.RegisteredRules.Count == 0 && !state.FiltersEnabled
                ? WfpBackendInstallState.NotInstalled
                : fallbackInstallState;

        return state with
        {
            InstallState = installState,
            DriverServiceInstalled = bridgeDiagnostics.DriverServiceInstalled,
            FiltersEnabled = bridgeDiagnostics.FiltersEnabled,
            TunnelConnected = bridgeDiagnostics.TunnelConnected,
            Messages = MergeMessages(
                state.Messages,
                bridgeDiagnostics.Messages
                    .Append($"Driver installed rule count: {bridgeDiagnostics.InstalledRuleCount}.")
                    .Append($"Driver active flow count: {bridgeDiagnostics.ActiveFlowCount}.")
                    .Append($"Dropped outbound connects: {bridgeDiagnostics.DroppedConnectCount}.")
                    .Append($"Dropped inbound accepts: {bridgeDiagnostics.DroppedRecvAcceptCount}.")
                    .Append($"Redirect decisions recorded: {bridgeDiagnostics.TunnelRedirectCount}.")
                    .ToArray()),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private async Task<BridgeDiagnosticsSnapshot?> TryGetBridgeDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var bridgeResult = await RunBridgeCommandAsync("diagnostics", [], cancellationToken, allowMissingBridge: true);
        if (!bridgeResult.Succeeded || string.IsNullOrWhiteSpace(bridgeResult.JsonPayload))
        {
            return null;
        }

        using var document = JsonDocument.Parse(bridgeResult.JsonPayload);
        var root = document.RootElement;

        return new BridgeDiagnosticsSnapshot(
            DriverServiceInstalled: ReadBoolean(root, "driverServiceInstalled"),
            FiltersEnabled: ReadBoolean(root, "filtersEnabled"),
            TunnelConnected: ReadBoolean(root, "tunnelConnected"),
            InstalledRuleCount: ReadInt32(root, "installedRuleCount"),
            ActiveFlowCount: ReadInt32(root, "activeFlowCount"),
            DroppedConnectCount: ReadInt32(root, "droppedConnectCount"),
            DroppedRecvAcceptCount: ReadInt32(root, "droppedRecvAcceptCount"),
            TunnelRedirectCount: ReadInt32(root, "tunnelRedirectCount"),
            Messages: ReadMessages(root));
    }

    private async Task<BridgeCommandResult> RunBridgeCommandAsync(
        string command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool allowMissingBridge = false)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_deploymentPaths.BridgeBinaryPath))
        {
            return new BridgeCommandResult(
                Succeeded: allowMissingBridge,
                command,
                allowMissingBridge
                    ? "Native WFP bridge executable is not staged."
                    : "Native WFP bridge executable is not staged.",
                JsonPayload: null);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _deploymentPaths.BridgeBinaryPath,
            WorkingDirectory = Path.GetDirectoryName(_deploymentPaths.BridgeBinaryPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add(command);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new BridgeCommandResult(false, command, "Failed to launch the native WFP bridge.", null);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var payload = GetLastJsonLine(stdout);

        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                var message = root.TryGetProperty("message", out var messageProperty)
                    ? messageProperty.GetString() ?? string.Empty
                    : string.IsNullOrWhiteSpace(stderr)
                        ? $"{command} completed."
                        : stderr.Trim();
                var succeeded = root.TryGetProperty("succeeded", out var succeededProperty)
                    && succeededProperty.ValueKind == JsonValueKind.True;

                return new BridgeCommandResult(succeeded, command, message, payload);
            }
            catch (JsonException)
            {
            }
        }

        return new BridgeCommandResult(
            process.ExitCode == 0,
            command,
            string.IsNullOrWhiteSpace(stderr)
                ? $"{command} exited with code {process.ExitCode}."
                : stderr.Trim(),
            payload);
    }

    private async Task<WfpBackendState> LoadStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _paths.EnsureDirectories();

        if (!File.Exists(_deploymentPaths.StateFilePath))
        {
            return WfpBackendState.CreateDefault();
        }

        await using var stream = File.OpenRead(_deploymentPaths.StateFilePath);
        var state = await JsonSerializer.DeserializeAsync<WfpBackendState>(stream, AppTunnelJson.Default, cancellationToken);
        return state ?? WfpBackendState.CreateDefault();
    }

    private async Task SaveStateAsync(WfpBackendState state, CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        Directory.CreateDirectory(Path.GetDirectoryName(_deploymentPaths.StateFilePath)!);

        await using var stream = new FileStream(
            _deploymentPaths.StateFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);

        await JsonSerializer.SerializeAsync(
            stream,
            state,
            new JsonSerializerOptions(AppTunnelJson.Default)
            {
                WriteIndented = true,
            },
            cancellationToken);
    }

    private WfpBackendDiagnostics ToDiagnostics(WfpBackendState state) =>
        new(
            state.InstallState,
            state.DriverServiceInstalled,
            BridgeReachable: File.Exists(_deploymentPaths.BridgeBinaryPath),
            state.FiltersEnabled,
            _deploymentPaths.DriverServiceName,
            _deploymentPaths.DriverDisplayName,
            _deploymentPaths.DriverBinaryPath,
            _deploymentPaths.BridgeBinaryPath,
            state.RegisteredRules.Count,
            state.ActiveFlows.Count,
            state.RegisteredRules,
            state.ActiveFlows,
            MergeMessages(
                state.Messages,
                !File.Exists(_deploymentPaths.BridgeBinaryPath)
                    ? "Native WFP bridge executable is not staged; diagnostics are coming from the managed control plane."
                    : null,
                state.TunnelConnected
                    ? "Tunnel state reported by WFP backend: connected."
                    : "Tunnel state reported by WFP backend: disconnected."),
            state.UpdatedAtUtc);

    private WfpOperationResult CreateResult(bool succeeded, string operation, string message, WfpBackendState state) =>
        new(succeeded, operation, message, ToDiagnostics(state), DateTimeOffset.UtcNow);

    private static IReadOnlyList<string> BuildAddRuleArguments(WfpAppRuleRegistration request)
    {
        var matchKind = request.AppKind == AppKind.Win32Exe
            ? "win32"
            : "packaged";
        var flags = 0;
        if (request.KillAppTrafficOnTunnelDrop)
        {
            flags |= 0x00000001;
        }

        if (request.IncludeChildProcesses)
        {
            flags |= 0x00000002;
        }

        return
        [
            request.RuleId.ToString("D"),
            request.ProfileId.ToString("D"),
            matchKind,
            flags.ToString(),
            request.DisplayName,
            request.ExecutablePath ?? "-",
            request.PackageFamilyName ?? "-",
            request.PackageIdentity ?? "-",
        ];
    }

    private static WfpRuleDiagnostic CreateRuleDiagnostic(WfpAppRuleRegistration request)
    {
        var matchDescriptor = request.AppKind == AppKind.Win32Exe
            ? request.ExecutablePath ?? string.Empty
            : $"{request.PackageFamilyName}|{request.PackageIdentity}";

        return new WfpRuleDiagnostic(
            request.RuleId,
            request.AppKind,
            request.DisplayName,
            matchDescriptor,
            request.ProfileId.ToString("D"),
            request.KillAppTrafficOnTunnelDrop,
            request.IncludeChildProcesses,
            DateTimeOffset.UtcNow);
    }

    private static string? GetLastJsonLine(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.StartsWith('{'));

    private static bool ReadBoolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : false;

    private static int ReadInt32(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property)
        && property.TryGetInt32(out var value)
            ? value
            : 0;

    private static IReadOnlyList<string> ReadMessages(JsonElement root)
    {
        var messages = new List<string>();
        if (root.TryGetProperty("message", out var messageProperty)
            && messageProperty.ValueKind == JsonValueKind.String)
        {
            var message = messageProperty.GetString();
            if (!string.IsNullOrWhiteSpace(message))
            {
                messages.Add(message);
            }
        }

        if (root.TryGetProperty("messages", out var messagesProperty)
            && messagesProperty.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in messagesProperty.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var message = item.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    messages.Add(message);
                }
            }
        }

        return messages
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> MergeMessages(
        IEnumerable<string> messages,
        params string?[] additionalMessages) =>
        MergeMessages(messages, additionalMessages.AsEnumerable());

    private static IReadOnlyList<string> MergeMessages(
        IEnumerable<string> messages,
        IEnumerable<string?> additionalMessages) =>
        messages
            .Concat(additionalMessages
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Select(message => message!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed record BridgeCommandResult(
        bool Succeeded,
        string Operation,
        string Message,
        string? JsonPayload);

    private sealed record BridgeDiagnosticsSnapshot(
        bool DriverServiceInstalled,
        bool FiltersEnabled,
        bool TunnelConnected,
        int InstalledRuleCount,
        int ActiveFlowCount,
        int DroppedConnectCount,
        int DroppedRecvAcceptCount,
        int TunnelRedirectCount,
        IReadOnlyList<string> Messages);

    private sealed record WfpBackendState(
        WfpBackendInstallState InstallState,
        bool DriverServiceInstalled,
        bool FiltersEnabled,
        bool TunnelConnected,
        IReadOnlyList<WfpRuleDiagnostic> RegisteredRules,
        IReadOnlyList<WfpFlowDiagnostic> ActiveFlows,
        IReadOnlyList<string> Messages,
        DateTimeOffset UpdatedAtUtc)
    {
        public static WfpBackendState CreateDefault() =>
            new(
                WfpBackendInstallState.NotInstalled,
                DriverServiceInstalled: false,
                FiltersEnabled: false,
                TunnelConnected: false,
                RegisteredRules: [],
                ActiveFlows: [],
                Messages: [],
                UpdatedAtUtc: DateTimeOffset.UtcNow);
    }
}

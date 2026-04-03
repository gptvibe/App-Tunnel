using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Router.WinDivert;

public sealed class WinDivertRouterBackend : IRouterBackend
{
    private const short Priority = 100;
    private const int PacketBufferSize = 0xFFFF;

    private readonly object _stateGate = new();
    private readonly ConcurrentDictionary<int, ProcessSelection> _selectedProcesses = [];
    private readonly ConcurrentDictionary<ulong, FlowBinding> _flowsByEndpoint = [];
    private readonly ConcurrentDictionary<FlowKey, FlowBinding> _flowsByOriginalKey = [];
    private readonly ConcurrentDictionary<FlowKey, FlowBinding> _flowsByEffectiveInboundKey = [];
    private readonly ConcurrentDictionary<string, long> _dropCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _errors = [];

    private CancellationTokenSource? _captureCancellationTokenSource;
    private Task? _socketLoopTask;
    private Task? _flowLoopTask;
    private Task? _packetLoopTask;
    private SafeWinDivertHandle? _socketHandle;
    private SafeWinDivertHandle? _flowHandle;
    private SafeWinDivertHandle? _packetHandle;
    private WinDivertResolvedPlan _resolvedPlan = new(
        new Dictionary<Guid, WinDivertRuleBinding>(),
        new Dictionary<Guid, WinDivertTunnelBinding>(),
        [],
        "None");
    private RouterDiagnosticsSnapshot _diagnostics = RouterDiagnosticsSnapshot.CreateDefault(RoutingBackendKind.WinDivert);
    private bool _initialized;

    public RoutingBackendKind Kind => RoutingBackendKind.WinDivert;

    public string DisplayName => "WinDivert Routing Backend";

    public BackendReadiness Readiness => BackendReadiness.Mvp;

    public bool RequiresElevation => true;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _initialized = true;
        return Task.CompletedTask;
    }

    public async Task<RouterApplyResult> ApplyRoutingPlanAsync(RoutingPlan routingPlan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }

        _resolvedPlan = WinDivertRoutingPolicy.Resolve(routingPlan);

        lock (_stateGate)
        {
            _errors.Clear();
            _errors.AddRange(_resolvedPlan.Errors);
        }

        if (_resolvedPlan.RuleBindings.Count == 0)
        {
            await StopAsync(cancellationToken);
            UpdateDiagnostics();
            return new RouterApplyResult(Kind, "WinDivert backend is idle because no Win32 app rules are assigned.", GetDiagnosticsSnapshot());
        }

        if (!IsElevated())
        {
            AddError("Administrator elevation is required before WinDivert can capture or rewrite traffic.");
            await StopCaptureLoopsAsync(cancellationToken);
            UpdateDiagnostics();
            return new RouterApplyResult(Kind, "WinDivert backend is blocked until the service runs elevated.", GetDiagnosticsSnapshot());
        }

        try
        {
            await EnsureCaptureLoopsStartedAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or Win32Exception or InvalidOperationException)
        {
            AddError(ex.Message);
            await StopCaptureLoopsAsync(cancellationToken);
        }

        UpdateDiagnostics();

        var state = _diagnostics.ErrorStates.Count == 0
            ? $"WinDivert backend enforcing {_resolvedPlan.RuleBindings.Count} selected app rule(s)."
            : $"WinDivert backend encountered {_diagnostics.ErrorStates.Count} error state(s).";

        return new RouterApplyResult(Kind, state, GetDiagnosticsSnapshot());
    }

    public RouterDiagnosticsSnapshot GetDiagnosticsSnapshot() => _diagnostics with
    {
        SelectedProcesses = _diagnostics.SelectedProcesses.ToArray(),
        MappedFlows = _diagnostics.MappedFlows.ToArray(),
        DroppedPackets = _diagnostics.DroppedPackets.ToArray(),
        ErrorStates = _diagnostics.ErrorStates.ToArray(),
    };

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StopCaptureLoopsAsync(cancellationToken);

        _selectedProcesses.Clear();
        _flowsByEndpoint.Clear();
        _flowsByOriginalKey.Clear();
        _flowsByEffectiveInboundKey.Clear();
        _dropCounters.Clear();

        UpdateDiagnostics();
    }

    private async Task EnsureCaptureLoopsStartedAsync(CancellationToken cancellationToken)
    {
        if (_packetLoopTask is not null)
        {
            return;
        }

        _socketHandle = OpenHandle("true", WinDivertLayer.Socket, WinDivertOpenFlags.Sniff | WinDivertOpenFlags.RecvOnly);
        _flowHandle = OpenHandle("true", WinDivertLayer.Flow, WinDivertOpenFlags.Sniff | WinDivertOpenFlags.RecvOnly);
        _packetHandle = OpenHandle("ip and !impostor", WinDivertLayer.Network, WinDivertOpenFlags.None);
        _captureCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var token = _captureCancellationTokenSource.Token;
        _socketLoopTask = Task.Run(() => RunSocketLoop(token), token);
        _flowLoopTask = Task.Run(() => RunFlowLoop(token), token);
        _packetLoopTask = Task.Run(() => RunPacketLoop(token), token);

        await Task.CompletedTask;
    }

    private async Task StopCaptureLoopsAsync(CancellationToken cancellationToken)
    {
        var tasks = new[] { _socketLoopTask, _flowLoopTask, _packetLoopTask }
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();

        _captureCancellationTokenSource?.Cancel();

        DisposeHandle(ref _socketHandle);
        DisposeHandle(ref _flowHandle);
        DisposeHandle(ref _packetHandle);

        if (tasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch
            {
                // Handle teardown is best-effort during shutdown.
            }
        }

        _captureCancellationTokenSource?.Dispose();
        _captureCancellationTokenSource = null;
        _socketLoopTask = null;
        _flowLoopTask = null;
        _packetLoopTask = null;
    }

    private static SafeWinDivertHandle OpenHandle(string filter, WinDivertLayer layer, WinDivertOpenFlags flags)
    {
        var handle = WinDivertNative.WinDivertOpen(filter, layer, Priority, flags);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"WinDivertOpen failed for layer '{layer}'.");
        }

        return handle;
    }

    private static void DisposeHandle(ref SafeWinDivertHandle? handle)
    {
        handle?.Dispose();
        handle = null;
    }

    private void RunSocketLoop(CancellationToken cancellationToken)
    {
        var emptyBuffer = Array.Empty<byte>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var address = new WinDivertAddress { Reserved = new byte[5] };
            uint readLength = 0;

            if (!TryRecv(_socketHandle, emptyBuffer, ref address, ref readLength))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            HandleFlowMetadata(address, address.Data.Socket);
        }
    }

    private void RunFlowLoop(CancellationToken cancellationToken)
    {
        var emptyBuffer = Array.Empty<byte>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var address = new WinDivertAddress { Reserved = new byte[5] };
            uint readLength = 0;

            if (!TryRecv(_flowHandle, emptyBuffer, ref address, ref readLength))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            HandleFlowMetadata(address, address.Data.Flow);
        }
    }

    private void RunPacketLoop(CancellationToken cancellationToken)
    {
        var packet = new byte[PacketBufferSize];

        while (!cancellationToken.IsCancellationRequested)
        {
            var address = new WinDivertAddress { Reserved = new byte[5] };
            uint readLength = 0;

            if (!TryRecv(_packetHandle, packet, ref address, ref readLength))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            HandlePacket(packet, (int)readLength, ref address);
        }
    }

    private static bool TryRecv(
        SafeWinDivertHandle? handle,
        byte[] packet,
        ref WinDivertAddress address,
        ref uint readLength)
    {
        if (handle is null || handle.IsInvalid)
        {
            return false;
        }

        return WinDivertNative.WinDivertRecv(handle, packet, (uint)packet.Length, ref address, ref readLength);
    }

    private void HandleFlowMetadata(WinDivertAddress address, WinDivertFlowData flowData)
    {
        var eventType = (WinDivertEvent)address.Event;

        if (eventType is WinDivertEvent.FlowDeleted or WinDivertEvent.SocketClose)
        {
            if (_flowsByEndpoint.TryRemove(flowData.EndpointId, out var existing))
            {
                _flowsByOriginalKey.TryRemove(existing.OriginalKey, out _);
                _flowsByEffectiveInboundKey.TryRemove(existing.EffectiveInboundKey, out _);
            }

            UpdateDiagnostics();
            return;
        }

        if (!TryCreateFlowBinding(address, flowData, out var binding))
        {
            return;
        }

        _flowsByEndpoint[binding.EndpointId] = binding;
        _flowsByOriginalKey[binding.OriginalKey] = binding;
        _flowsByEffectiveInboundKey[binding.EffectiveInboundKey] = binding;
        _selectedProcesses[binding.ProcessId] = new ProcessSelection(
            binding.ProcessId,
            binding.RuleBinding.RuleId,
            binding.RuleBinding.DisplayName,
            binding.ExecutablePath,
            binding.TunnelBinding.Summary,
            binding.TunnelBinding.State == TunnelConnectionState.Connected ? "Selected" : "Blocked",
            DateTimeOffset.UtcNow);

        UpdateDiagnostics();
    }

    private bool TryCreateFlowBinding(WinDivertAddress address, WinDivertFlowData flowData, out FlowBinding binding)
    {
        binding = default;

        if (address.IPv6 != 0 || flowData.Protocol is not 6 and not 17)
        {
            return false;
        }

        if (!TryResolveSelectedProcess(unchecked((int)flowData.ProcessId), out var selection))
        {
            return false;
        }

        var originalLocalAddress = ReadAddress(flowData.LocalAddress, ipv6: false);
        var originalRemoteAddress = ReadAddress(flowData.RemoteAddress, ipv6: false);
        if (originalLocalAddress is null || originalRemoteAddress is null)
        {
            return false;
        }

        if (!_resolvedPlan.TunnelBindings.TryGetValue(selection.RuleBinding.ProfileId, out var tunnelBinding))
        {
            AddError($"Profile '{selection.RuleBinding.ProfileId:D}' is missing a resolved tunnel binding.");
            return false;
        }

        var effectiveLocalAddress = tunnelBinding.LocalAddress ?? originalLocalAddress;
        var effectiveRemoteAddress = originalRemoteAddress;

        if (IsDnsFlow(flowData.Protocol, flowData.RemotePort) && tunnelBinding.DnsServers.Count > 0)
        {
            effectiveRemoteAddress = tunnelBinding.DnsServers[0];
        }

        binding = new FlowBinding(
            flowData.EndpointId,
            selection.ProcessId,
            selection.RuleBinding,
            tunnelBinding,
            selection.ExecutablePath,
            FlowKey.Create(originalLocalAddress, flowData.LocalPort, originalRemoteAddress, flowData.RemotePort, flowData.Protocol),
            FlowKey.Create(originalRemoteAddress, flowData.RemotePort, effectiveLocalAddress, flowData.LocalPort, flowData.Protocol),
            originalLocalAddress,
            effectiveLocalAddress,
            originalRemoteAddress,
            effectiveRemoteAddress,
            DateTimeOffset.UtcNow);

        return true;
    }

    private bool TryResolveSelectedProcess(int processId, out ProcessResolution selection)
    {
        selection = default;

        if (_selectedProcesses.TryGetValue(processId, out var existing)
            && _resolvedPlan.RuleBindings.TryGetValue(existing.RuleId, out var cachedRule))
        {
            selection = new ProcessResolution(processId, cachedRule, existing.ExecutablePath);
            return true;
        }

        string executablePath;

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            executablePath = process.MainModule?.FileName
                ?? throw new InvalidOperationException($"Process '{processId}' did not expose a main module path.");
        }
        catch
        {
            return false;
        }

        var ruleBinding = _resolvedPlan.RuleBindings.Values.FirstOrDefault(rule =>
            string.Equals(rule.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));

        if (ruleBinding is null)
        {
            return false;
        }

        selection = new ProcessResolution(processId, ruleBinding, executablePath);
        return true;
    }

    private void HandlePacket(byte[] packet, int packetLength, ref WinDivertAddress address)
    {
        if (!WinDivertPacketRewriter.TryParseIpv4Packet(packet, packetLength, out var descriptor))
        {
            SendPacket(packet, packetLength, ref address);
            return;
        }

        if (address.Outbound != 0)
        {
            var outboundKey = FlowKey.Create(
                descriptor.SourceAddress,
                descriptor.SourcePort,
                descriptor.DestinationAddress,
                descriptor.DestinationPort,
                descriptor.Protocol);

            if (_flowsByOriginalKey.TryGetValue(outboundKey, out var flow))
            {
                if (flow.TunnelBinding.State != TunnelConnectionState.Connected
                    || flow.TunnelBinding.LocalAddress is null
                    || flow.TunnelBinding.InterfaceIndex == 0)
                {
                    IncrementDropCounter("tunnel_down");
                    return;
                }

                WinDivertPacketRewriter.RewritePacket(
                    packet,
                    descriptor,
                    flow.EffectiveLocalAddress,
                    flow.EffectiveRemoteAddress);

                address.Data.Network = new WinDivertNetworkData
                {
                    InterfaceIndex = flow.TunnelBinding.InterfaceIndex,
                    SubInterfaceIndex = flow.TunnelBinding.SubInterfaceIndex,
                };

                SendPacket(packet, packetLength, ref address);
                return;
            }

            SendPacket(packet, packetLength, ref address);
            return;
        }

        var inboundKey = FlowKey.Create(
            descriptor.SourceAddress,
            descriptor.SourcePort,
            descriptor.DestinationAddress,
            descriptor.DestinationPort,
            descriptor.Protocol);

        if (_flowsByEffectiveInboundKey.TryGetValue(inboundKey, out var inboundFlow))
        {
            WinDivertPacketRewriter.RewritePacket(
                packet,
                descriptor,
                inboundFlow.OriginalRemoteAddress,
                inboundFlow.OriginalLocalAddress);
        }

        SendPacket(packet, packetLength, ref address);
    }

    private void SendPacket(byte[] packet, int packetLength, ref WinDivertAddress address)
    {
        if (_packetHandle is null || _packetHandle.IsInvalid)
        {
            return;
        }

        uint writeLength = 0;
        if (!WinDivertNative.WinDivertSend(_packetHandle, packet, (uint)packetLength, ref address, ref writeLength))
        {
            AddError($"WinDivertSend failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    private void IncrementDropCounter(string reason) =>
        _dropCounters.AddOrUpdate(reason, 1, static (_, current) => current + 1);

    private void AddError(string error)
    {
        lock (_stateGate)
        {
            if (_errors.Contains(error, StringComparer.Ordinal))
            {
                return;
            }

            _errors.Add(error);
        }

        UpdateDiagnostics();
    }

    private void UpdateDiagnostics()
    {
        string[] errors;

        lock (_stateGate)
        {
            errors = _errors.ToArray();
        }

        _diagnostics = new RouterDiagnosticsSnapshot(
            Kind,
            RequiresElevation,
            IsElevated(),
            _resolvedPlan.ActiveTunnel,
            _selectedProcesses.Values
                .OrderByDescending(process => process.LastSeenUtc)
                .Select(process => new SelectedProcessActivity(
                    process.ProcessId,
                    process.RuleId,
                    process.DisplayName,
                    process.ExecutablePath,
                    process.AssignedTunnel,
                    process.State,
                    process.LastSeenUtc))
                .ToArray(),
            _flowsByEndpoint.Values
                .OrderByDescending(flow => flow.LastSeenUtc)
                .Select(flow => new FlowMappingSnapshot(
                    flow.RuleBinding.RuleId,
                    flow.ProcessId,
                    flow.RuleBinding.DisplayName,
                    flow.OriginalKey.ToDisplayString(),
                    flow.EffectiveInboundKey.ToDisplayString(),
                    flow.TunnelBinding.Summary,
                    flow.TunnelBinding.State == TunnelConnectionState.Connected ? "Active" : "Blocked",
                    flow.LastSeenUtc))
                .ToArray(),
            _dropCounters
                .OrderByDescending(entry => entry.Value)
                .Select(entry => new PacketDropCounter(entry.Key, entry.Value))
                .ToArray(),
            errors,
            DateTimeOffset.UtcNow);
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool IsDnsFlow(byte protocol, ushort remotePort) =>
        protocol is 6 or 17 && remotePort == 53;

    private static IPAddress? ReadAddress(byte[]? rawAddress, bool ipv6)
    {
        if (rawAddress is null)
        {
            return null;
        }

        var bytes = ipv6
            ? rawAddress[..16]
            : rawAddress[..4];

        return new IPAddress(bytes);
    }

    private readonly record struct ProcessResolution(
        int ProcessId,
        WinDivertRuleBinding RuleBinding,
        string ExecutablePath);

    private readonly record struct ProcessSelection(
        int ProcessId,
        Guid RuleId,
        string DisplayName,
        string ExecutablePath,
        string AssignedTunnel,
        string State,
        DateTimeOffset LastSeenUtc);

    private readonly record struct FlowBinding(
        ulong EndpointId,
        int ProcessId,
        WinDivertRuleBinding RuleBinding,
        WinDivertTunnelBinding TunnelBinding,
        string ExecutablePath,
        FlowKey OriginalKey,
        FlowKey EffectiveInboundKey,
        IPAddress OriginalLocalAddress,
        IPAddress EffectiveLocalAddress,
        IPAddress OriginalRemoteAddress,
        IPAddress EffectiveRemoteAddress,
        DateTimeOffset LastSeenUtc);

    private readonly record struct FlowKey(
        IPAddress SourceAddress,
        ushort SourcePort,
        IPAddress DestinationAddress,
        ushort DestinationPort,
        byte Protocol)
    {
        public static FlowKey Create(
            IPAddress sourceAddress,
            ushort sourcePort,
            IPAddress destinationAddress,
            ushort destinationPort,
            byte protocol) =>
            new(sourceAddress, sourcePort, destinationAddress, destinationPort, protocol);

        public string ToDisplayString() =>
            $"{Protocol}:{SourceAddress}:{SourcePort} -> {DestinationAddress}:{DestinationPort}";
    }
}

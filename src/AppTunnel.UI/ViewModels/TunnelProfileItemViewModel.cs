using System.Globalization;
using AppTunnel.Core.Domain;

namespace AppTunnel.UI.ViewModels;

public sealed class TunnelProfileItemViewModel
{
    public TunnelProfileItemViewModel(TunnelProfile profile, TunnelStatusSnapshot? status)
    {
        Id = profile.Id;
        DisplayName = profile.DisplayName;
        TunnelKind = profile.TunnelKind.ToString();
        InterfaceName = profile.WireGuardProfile?.InterfaceName ?? "Unavailable";
        Addresses = profile.WireGuardProfile is { Addresses.Count: > 0 }
            ? string.Join(", ", profile.WireGuardProfile.Addresses)
            : "Unavailable";
        DnsServers = profile.WireGuardProfile is { DnsServers.Count: > 0 }
            ? string.Join(", ", profile.WireGuardProfile.DnsServers)
            : "None";
        PeerSummary = profile.WireGuardProfile is { Peers.Count: > 0 }
            ? $"{profile.WireGuardProfile.Peers.Count} peer(s)"
            : "0 peer(s)";
        EndpointSummary = profile.WireGuardProfile is { Peers.Count: > 0 }
            ? string.Join("; ", profile.WireGuardProfile.Peers.Select(peer => peer.Endpoint ?? "No endpoint"))
            : "None";
        ImportedConfigPath = profile.ImportedConfigPath;
        ImportedAtUtc = profile.ImportedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        IsEnabled = profile.IsEnabled;

        ConnectionState = status?.State.ToString() ?? TunnelConnectionState.Unknown.ToString();
        ConnectionSummary = status?.Summary ?? "No live status available yet.";
        ErrorMessage = string.IsNullOrWhiteSpace(status?.ErrorMessage) ? "None" : status.ErrorMessage!;
        BackendName = status is null
            ? "Unavailable"
            : $"{status.BackendName}{(status.IsMock ? " (mock)" : string.Empty)}";
        LastStatusUpdateUtc = status?.UpdatedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "Unavailable";
        CanConnect = status is null || status.State is TunnelConnectionState.Disconnected or TunnelConnectionState.Faulted or TunnelConnectionState.Unknown;
        CanDisconnect = status is not null
            && status.State is TunnelConnectionState.Connected or TunnelConnectionState.Connecting or TunnelConnectionState.Disconnecting;
        IsMock = status?.IsMock ?? false;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public string TunnelKind { get; }

    public string InterfaceName { get; }

    public string Addresses { get; }

    public string DnsServers { get; }

    public string PeerSummary { get; }

    public string EndpointSummary { get; }

    public string ImportedConfigPath { get; }

    public string ImportedAtUtc { get; }

    public bool IsEnabled { get; }

    public string ConnectionState { get; }

    public string ConnectionSummary { get; }

    public string ErrorMessage { get; }

    public string BackendName { get; }

    public string LastStatusUpdateUtc { get; }

    public bool CanConnect { get; }

    public bool CanDisconnect { get; }

    public bool IsMock { get; }
}
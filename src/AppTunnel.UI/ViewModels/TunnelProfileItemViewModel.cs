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

        switch (profile.TunnelKind)
        {
            case AppTunnel.Core.Domain.TunnelKind.WireGuard:
                BuildWireGuardDetails(profile);
                break;
            case AppTunnel.Core.Domain.TunnelKind.OpenVpn:
                BuildOpenVpnDetails(profile);
                break;
            default:
                ListSummary = "Tunnel metadata unavailable.";
                ValidationSummary = "Validation results unavailable.";
                Details = [];
                break;
        }
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public string TunnelKind { get; }

    public string ListSummary { get; private set; } = "Unavailable";

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

    public string ValidationSummary { get; private set; } = "Validation results unavailable.";

    public IReadOnlyList<ProfileFactViewModel> Details { get; private set; } = [];

    private void BuildWireGuardDetails(TunnelProfile profile)
    {
        var details = profile.WireGuardProfile;
        ListSummary = details is { Addresses.Count: > 0 }
            ? string.Join(", ", details.Addresses)
            : "WireGuard addresses unavailable.";
        ValidationSummary = "Validated during import by the WireGuard parser.";

        Details =
        [
            new("Kind", "WireGuard"),
            new("Interface", details?.InterfaceName ?? "Unavailable"),
            new("Addresses", details is { Addresses.Count: > 0 } ? string.Join(", ", details.Addresses) : "Unavailable"),
            new("DNS", details is { DnsServers.Count: > 0 } ? string.Join(", ", details.DnsServers) : "None"),
            new("Peers", details is { Peers.Count: > 0 } ? $"{details.Peers.Count} peer(s)" : "0 peer(s)"),
            new("Endpoints", details is { Peers.Count: > 0 } ? string.Join("; ", details.Peers.Select(peer => peer.Endpoint ?? "No endpoint")) : "None"),
            new("Imported from", ImportedConfigPath),
            new("Imported at", ImportedAtUtc),
        ];
    }

    private void BuildOpenVpnDetails(TunnelProfile profile)
    {
        var details = profile.OpenVpnProfile;
        ListSummary = details is { RemoteEndpoints.Count: > 0 }
            ? string.Join(", ", details.RemoteEndpoints)
            : "OpenVPN remotes unavailable.";
        ValidationSummary = BuildOpenVpnValidationSummary(details);

        var materialSummary = details is null
            ? "Unavailable"
            : $"{details.InlineMaterialCount} inline, {details.ExternalMaterialCount} external";
        var credentialSummary = details is null
            ? "Unavailable"
            : details.RequiresUsernamePassword
                ? details.HasStoredCredentials
                    ? "Username/password stored with DPAPI"
                    : "Username/password required"
                : "Profile-auth only";

        Details =
        [
            new("Kind", "OpenVPN"),
            new("Device", details?.Device ?? "Unavailable"),
            new("Protocol", string.IsNullOrWhiteSpace(details?.Protocol) ? "Default" : details.Protocol!),
            new("Remote endpoints", details is { RemoteEndpoints.Count: > 0 } ? string.Join(", ", details.RemoteEndpoints) : "Unavailable"),
            new("Credentials", credentialSummary),
            new("Cert material", materialSummary),
            new("Imported from", ImportedConfigPath),
            new("Imported at", ImportedAtUtc),
        ];
    }

    private static string BuildOpenVpnValidationSummary(OpenVpnProfileDetails? details)
    {
        if (details is null)
        {
            return "Validation metadata unavailable.";
        }

        if (!details.Validation.IsValid && details.Validation.Errors.Count > 0)
        {
            return string.Join(Environment.NewLine, details.Validation.Errors);
        }

        if (details.Validation.Warnings.Count > 0)
        {
            return $"Validated with warning(s): {string.Join(" | ", details.Validation.Warnings)}";
        }

        return "Validated during import and ready for the managed OpenVPN backend.";
    }
}

public sealed record ProfileFactViewModel(
    string Label,
    string Value);

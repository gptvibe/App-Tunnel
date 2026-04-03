namespace AppTunnel.Core.Domain;

public sealed record TunnelProfile
{
    public TunnelProfile(
        Guid id,
        string displayName,
        TunnelKind tunnelKind,
        string importedConfigPath,
        string? secretReferenceId,
        bool isEnabled,
        DateTimeOffset importedAtUtc,
        WireGuardProfileDetails? wireGuardProfile = null,
        OpenVpnProfileDetails? openVpnProfile = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Profile ID must be non-empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(importedConfigPath))
        {
            throw new ArgumentException("Imported config path is required.", nameof(importedConfigPath));
        }

        Id = id;
        DisplayName = displayName;
        TunnelKind = tunnelKind;
        ImportedConfigPath = importedConfigPath;
        SecretReferenceId = secretReferenceId;
        IsEnabled = isEnabled;
        ImportedAtUtc = importedAtUtc;
        WireGuardProfile = wireGuardProfile;
        OpenVpnProfile = openVpnProfile;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public TunnelKind TunnelKind { get; }

    public string ImportedConfigPath { get; }

    public string? SecretReferenceId { get; }

    public bool IsEnabled { get; }

    public DateTimeOffset ImportedAtUtc { get; }

    public WireGuardProfileDetails? WireGuardProfile { get; }

    public OpenVpnProfileDetails? OpenVpnProfile { get; }
}

public sealed record WireGuardProfileDetails(
    string InterfaceName,
    IReadOnlyList<string> Addresses,
    IReadOnlyList<string> DnsServers,
    int? ListenPort,
    int? Mtu,
    IReadOnlyList<WireGuardPeerDetails> Peers);

public sealed record WireGuardPeerDetails(
    string PublicKey,
    string? Endpoint,
    IReadOnlyList<string> AllowedIps,
    bool HasPresharedKey,
    int? PersistentKeepaliveSeconds);

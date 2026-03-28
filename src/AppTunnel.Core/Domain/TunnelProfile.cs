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
        DateTimeOffset importedAtUtc)
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
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public TunnelKind TunnelKind { get; }

    public string ImportedConfigPath { get; }

    public string? SecretReferenceId { get; }

    public bool IsEnabled { get; }

    public DateTimeOffset ImportedAtUtc { get; }
}

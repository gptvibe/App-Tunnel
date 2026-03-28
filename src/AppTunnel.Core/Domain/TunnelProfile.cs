namespace AppTunnel.Core.Domain;

public sealed record TunnelProfile
{
    public TunnelProfile(
        Guid id,
        string displayName,
        VpnProviderKind providerKind,
        string importedConfigPath,
        string? secretReferenceId,
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
        ProviderKind = providerKind;
        ImportedConfigPath = importedConfigPath;
        SecretReferenceId = secretReferenceId;
        ImportedAtUtc = importedAtUtc;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public VpnProviderKind ProviderKind { get; }

    public string ImportedConfigPath { get; }

    public string? SecretReferenceId { get; }

    public DateTimeOffset ImportedAtUtc { get; }
}
namespace AppTunnel.Core.Domain;

public sealed record AppRule
{
    public AppRule(
        Guid id,
        string displayName,
        string executablePath,
        Guid profileId,
        bool isEnabled,
        DateTimeOffset updatedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Rule ID must be non-empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(executablePath));
        }

        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("Profile ID must be non-empty.", nameof(profileId));
        }

        Id = id;
        DisplayName = displayName;
        ExecutablePath = executablePath;
        ProfileId = profileId;
        IsEnabled = isEnabled;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public string ExecutablePath { get; }

    public Guid ProfileId { get; }

    public bool IsEnabled { get; }

    public DateTimeOffset UpdatedAtUtc { get; }
}

namespace AppTunnel.Core.Domain;

public sealed record AppRule
{
    public AppRule(
        Guid id,
        AppKind appKind,
        string displayName,
        string? executablePath,
        string? packageFamilyName,
        string? packageIdentity,
        Guid? profileId,
        bool isEnabled,
        bool launchOnConnect,
        bool killAppTrafficOnTunnelDrop,
        bool includeChildProcesses,
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

        Id = id;
        AppKind = appKind;
        DisplayName = displayName;
        ExecutablePath = NormalizeExecutablePath(appKind, executablePath, packageFamilyName, packageIdentity);
        PackageFamilyName = NormalizeOptionalValue(packageFamilyName);
        PackageIdentity = NormalizeOptionalValue(packageIdentity);
        ProfileId = profileId;
        IsEnabled = isEnabled;
        LaunchOnConnect = launchOnConnect;
        KillAppTrafficOnTunnelDrop = killAppTrafficOnTunnelDrop;
        IncludeChildProcesses = includeChildProcesses;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }

    public AppKind AppKind { get; }

    public string DisplayName { get; }

    public string? ExecutablePath { get; }

    public string? PackageFamilyName { get; }

    public string? PackageIdentity { get; }

    public Guid? ProfileId { get; }

    public bool IsEnabled { get; }

    public bool LaunchOnConnect { get; }

    public bool KillAppTrafficOnTunnelDrop { get; }

    public bool IncludeChildProcesses { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    private static string? NormalizeExecutablePath(
        AppKind appKind,
        string? executablePath,
        string? packageFamilyName,
        string? packageIdentity)
    {
        if (appKind == AppKind.Win32Exe)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new ArgumentException("Executable path is required for Win32 app rules.", nameof(executablePath));
            }

            if (!string.IsNullOrWhiteSpace(packageFamilyName) || !string.IsNullOrWhiteSpace(packageIdentity))
            {
                throw new ArgumentException("Packaged-app identity fields must be empty for Win32 app rules.");
            }

            var fullPath = Path.GetFullPath(executablePath);
            if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Win32 app rules require an .exe path.", nameof(executablePath));
            }

            return fullPath;
        }

        if (string.IsNullOrWhiteSpace(packageFamilyName))
        {
            throw new ArgumentException("Package family name is required for packaged-app rules.", nameof(packageFamilyName));
        }

        if (string.IsNullOrWhiteSpace(packageIdentity))
        {
            throw new ArgumentException("Package identity is required for packaged-app rules.", nameof(packageIdentity));
        }

        return string.IsNullOrWhiteSpace(executablePath)
            ? null
            : Path.GetFullPath(executablePath);
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}

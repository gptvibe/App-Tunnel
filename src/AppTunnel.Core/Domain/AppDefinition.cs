namespace AppTunnel.Core.Domain;

public sealed record AppDefinition
{
    public AppDefinition(Guid id, string displayName, AppKind kind, string? executablePath, string? packageFamilyName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("App ID must be non-empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        if (kind == AppKind.Win32Exe && string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Win32 apps require an executable path.", nameof(executablePath));
        }

        if (kind == AppKind.PackagedApp && string.IsNullOrWhiteSpace(packageFamilyName))
        {
            throw new ArgumentException("Packaged apps require a package family name.", nameof(packageFamilyName));
        }

        Id = id;
        DisplayName = displayName;
        Kind = kind;
        ExecutablePath = executablePath;
        PackageFamilyName = packageFamilyName;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public AppKind Kind { get; }

    public string? ExecutablePath { get; }

    public string? PackageFamilyName { get; }
}
using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Services;

public sealed class AppTunnelPaths
{
    public AppTunnelPaths(string rootDirectory)
        : this(
            rootDirectory,
            Path.Combine(Path.GetFullPath(rootDirectory), "config.json"),
            Path.Combine(Path.GetFullPath(rootDirectory), "logs"),
            Path.Combine(Path.GetFullPath(rootDirectory), "secrets"),
            Path.Combine(Path.GetFullPath(rootDirectory), "exports"),
            DistributionMode.Installer)
    {
    }

    private AppTunnelPaths(
        string rootDirectory,
        string configurationFilePath,
        string logsDirectory,
        string secretsDirectory,
        string exportsDirectory,
        DistributionMode distributionMode)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("The App Tunnel root directory is required.", nameof(rootDirectory));
        }

        RootDirectory = Path.GetFullPath(rootDirectory);
        ConfigurationFilePath = Path.GetFullPath(configurationFilePath);
        LogsDirectory = Path.GetFullPath(logsDirectory);
        SecretsDirectory = Path.GetFullPath(secretsDirectory);
        ExportsDirectory = Path.GetFullPath(exportsDirectory);
        DistributionMode = distributionMode;
    }

    public static string GetDefaultRootDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AppTunnel");

    public static AppTunnelPaths CreatePortable(string portableRootDirectory)
    {
        var rootDirectory = Path.GetFullPath(portableRootDirectory);
        var dataDirectory = Path.Combine(rootDirectory, "data");

        return new AppTunnelPaths(
            rootDirectory,
            Path.Combine(dataDirectory, "config.json"),
            Path.Combine(rootDirectory, "logs"),
            Path.Combine(dataDirectory, "secrets"),
            Path.Combine(dataDirectory, "exports"),
            DistributionMode.PortableZip);
    }

    public string RootDirectory { get; }

    public string ConfigurationFilePath { get; }

    public string LogsDirectory { get; }

    public string SecretsDirectory { get; }

    public string ExportsDirectory { get; }

    public DistributionMode DistributionMode { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationFilePath)!);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(SecretsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
    }

    public StorageSnapshot ToSnapshot() =>
        new(
            RootDirectory,
            ConfigurationFilePath,
            LogsDirectory,
            SecretsDirectory,
            ExportsDirectory);
}

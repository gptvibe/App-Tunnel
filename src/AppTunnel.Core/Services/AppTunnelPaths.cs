using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Services;

public sealed class AppTunnelPaths
{
    public AppTunnelPaths(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("The App Tunnel root directory is required.", nameof(rootDirectory));
        }

        RootDirectory = Path.GetFullPath(rootDirectory);
        ConfigurationFilePath = Path.Combine(RootDirectory, "config.json");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        SecretsDirectory = Path.Combine(RootDirectory, "secrets");
        ExportsDirectory = Path.Combine(RootDirectory, "exports");
    }

    public static string GetDefaultRootDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AppTunnel");

    public string RootDirectory { get; }

    public string ConfigurationFilePath { get; }

    public string LogsDirectory { get; }

    public string SecretsDirectory { get; }

    public string ExportsDirectory { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
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

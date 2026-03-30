using System.Text.Json;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;

namespace AppTunnel.Core.Services;

public sealed class JsonAppTunnelConfigurationStore(AppTunnelPaths paths) : IAppTunnelConfigurationStore
{
    public async Task<AppTunnelConfiguration> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        paths.EnsureDirectories();

        if (!File.Exists(paths.ConfigurationFilePath))
        {
            var seedConfiguration = CreateDefaultConfiguration();
            await SaveAsync(seedConfiguration, cancellationToken);
            return seedConfiguration;
        }

        await using var stream = File.OpenRead(paths.ConfigurationFilePath);
        var configuration = await JsonSerializer.DeserializeAsync<AppTunnelConfiguration>(
            stream,
            AppTunnelJson.Default,
            cancellationToken);

        return configuration ?? CreateDefaultConfiguration();
    }

    public async Task SaveAsync(AppTunnelConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        paths.EnsureDirectories();

        await using var stream = new FileStream(
            paths.ConfigurationFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);

        await JsonSerializer.SerializeAsync(
            stream,
            configuration,
            new JsonSerializerOptions(AppTunnelJson.Default)
            {
                WriteIndented = true,
            },
            cancellationToken);
    }

    private AppTunnelConfiguration CreateDefaultConfiguration()
    {
        return new AppTunnelConfiguration(
            Profiles: [],
            AppRules: [],
            Settings: new AppTunnelSettings(
                AppTunnelPipeNames.Control,
                RoutingBackendKind.DryRun,
                paths.RootDirectory,
                RefreshIntervalSeconds: 5,
                StartMinimizedToTray: false));
    }
}

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
        var sampleProfileId = Guid.Parse("C6F16E7B-3BF0-489A-8D49-31D4EECE7D0F");
        var sampleRuleId = Guid.Parse("F8671028-0678-4B7A-9D31-C4DE2FAF9D5D");
        var now = DateTimeOffset.UtcNow;

        return new AppTunnelConfiguration(
            Profiles:
            [
                new TunnelProfile(
                    sampleProfileId,
                    "Sample WireGuard Profile",
                    TunnelKind.WireGuard,
                    @"C:\VPN\sample-wireguard.conf",
                    secretReferenceId: null,
                    isEnabled: true,
                    importedAtUtc: now)
            ],
            AppRules:
            [
                new AppRule(
                    sampleRuleId,
                    "Notepad via Sample Tunnel",
                    @"C:\Windows\System32\notepad.exe",
                    sampleProfileId,
                    isEnabled: true,
                    updatedAtUtc: now)
            ],
            Settings: new AppTunnelSettings(
                AppTunnelPipeNames.Control,
                RoutingBackendKind.DryRun,
                paths.RootDirectory,
                RefreshIntervalSeconds: 5,
                StartMinimizedToTray: false));
    }
}

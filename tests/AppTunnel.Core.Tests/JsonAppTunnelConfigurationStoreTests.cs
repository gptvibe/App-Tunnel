using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;
using AppTunnel.Core.Services;

namespace AppTunnel.Core.Tests;

public sealed class JsonAppTunnelConfigurationStoreTests
{
    [Fact]
    public async Task LoadCreatesSeedConfigurationWhenFileIsMissing()
    {
        var root = CreateTempDirectory();
        var paths = new AppTunnelPaths(root);
        var store = new JsonAppTunnelConfigurationStore(paths);

        var configuration = await store.LoadAsync(CancellationToken.None);

        Assert.NotEmpty(configuration.Profiles);
        Assert.NotEmpty(configuration.AppRules);
        Assert.True(File.Exists(paths.ConfigurationFilePath));
    }

    [Fact]
    public async Task SaveAndLoadRoundTripConfiguration()
    {
        var root = CreateTempDirectory();
        var paths = new AppTunnelPaths(root);
        var store = new JsonAppTunnelConfigurationStore(paths);
        var now = DateTimeOffset.UtcNow;
        var profileId = Guid.NewGuid();

        var expected = new AppTunnelConfiguration(
            [
                new TunnelProfile(
                    profileId,
                    "Sample profile",
                    TunnelKind.WireGuard,
                    @"C:\vpn\sample.conf",
                    secretReferenceId: "secret-1",
                    isEnabled: true,
                    importedAtUtc: now)
            ],
            [
                new AppRule(
                    Guid.NewGuid(),
                    "Browser rule",
                    @"C:\Program Files\Browser\browser.exe",
                    profileId,
                    isEnabled: true,
                    updatedAtUtc: now)
            ],
            new AppTunnelSettings(
                AppTunnelPipeNames.Control,
                RoutingBackendKind.WinDivert,
                root,
                RefreshIntervalSeconds: 10,
                StartMinimizedToTray: true));

        await store.SaveAsync(expected, CancellationToken.None);

        var actual = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(expected.Settings.PipeName, actual.Settings.PipeName);
        Assert.Equal(expected.Settings.PreferredRoutingBackend, actual.Settings.PreferredRoutingBackend);
        Assert.Single(actual.Profiles);
        Assert.Single(actual.AppRules);
        Assert.Equal(expected.Profiles[0].DisplayName, actual.Profiles[0].DisplayName);
        Assert.Equal(expected.AppRules[0].ExecutablePath, actual.AppRules[0].ExecutablePath);
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "apptunnel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

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

        Assert.Empty(configuration.Profiles);
        Assert.Empty(configuration.AppRules);
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
                    importedAtUtc: now,
                    wireGuardProfile: new WireGuardProfileDetails(
                        "Sample interface",
                        ["10.0.0.2/32"],
                        ["1.1.1.1"],
                        ListenPort: 51820,
                        Mtu: 1380,
                        Peers:
                        [
                            new WireGuardPeerDetails(
                                "public-key",
                                "demo.example.com:51820",
                                ["0.0.0.0/0"],
                                HasPresharedKey: true,
                                PersistentKeepaliveSeconds: 25)
                        ]))
            ],
            [
                new AppRule(
                    Guid.NewGuid(),
                    AppKind.Win32Exe,
                    "Browser rule",
                    @"C:\Program Files\Browser\browser.exe",
                    packageFamilyName: null,
                    packageIdentity: null,
                    profileId: profileId,
                    isEnabled: true,
                    launchOnConnect: true,
                    killAppTrafficOnTunnelDrop: true,
                    includeChildProcesses: true,
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
        Assert.NotNull(actual.Profiles[0].WireGuardProfile);
        Assert.Equal("Sample interface", actual.Profiles[0].WireGuardProfile!.InterfaceName);
        Assert.Equal(expected.AppRules[0].ExecutablePath, actual.AppRules[0].ExecutablePath);
        Assert.Equal(AppKind.Win32Exe, actual.AppRules[0].AppKind);
        Assert.Equal(expected.AppRules[0].ProfileId, actual.AppRules[0].ProfileId);
        Assert.True(actual.AppRules[0].LaunchOnConnect);
        Assert.True(actual.AppRules[0].KillAppTrafficOnTunnelDrop);
        Assert.True(actual.AppRules[0].IncludeChildProcesses);
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "apptunnel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

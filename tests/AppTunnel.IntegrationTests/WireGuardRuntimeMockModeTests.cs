using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Security;
using AppTunnel.Core.Services;
using AppTunnel.Service.Runtime;
using AppTunnel.Vpn.OpenVpn;
using AppTunnel.Vpn.WireGuard;

namespace AppTunnel.IntegrationTests;

public sealed class WireGuardRuntimeMockModeTests
{
    [Fact]
    public async Task RuntimeImportsConnectsAndDisconnectsProfileInMockMode()
    {
        var root = CreateTempDirectory();
        var paths = new AppTunnelPaths(root);
        var configurationStore = new JsonAppTunnelConfigurationStore(paths);
        var structuredLogService = new StructuredLogService(paths, "tests");
        var secretStore = new DpapiSecretStore(paths, new FakeProtector());
        var wireGuardEngine = new WireGuardTunnelEngine(
            secretStore,
            structuredLogService,
            paths,
            new WireGuardConfigParser(),
            new MockWireGuardBackend());
        var openVpnEngine = new OpenVpnTunnelEngine(
            secretStore,
            structuredLogService,
            paths,
            new OpenVpnConfigParser(),
            new StubOpenVpnBackend());
        var tunnelEngines = new ITunnelEngine[]
        {
            wireGuardEngine,
            openVpnEngine,
        };
        var runtime = new AppTunnelRuntime(
            NullLogger<AppTunnelRuntime>.Instance,
            configurationStore,
            new WindowsApplicationDiscoveryService(),
            structuredLogService,
            new FakeLogBundleExporter(),
            new FakeWfpBackendControl(),
            new ServiceTunnelManager(tunnelEngines),
            new RouterManager(structuredLogService, Array.Empty<IRouterBackend>()),
            tunnelEngines,
            Array.Empty<IRouterBackend>(),
            paths);
        var configPath = WriteConfigFile(root, "profile.conf");

        await runtime.StartAsync(CancellationToken.None);

        var importedProfile = await runtime.ImportProfileAsync(
            new ProfileImportRequest("Mock profile", configPath),
            CancellationToken.None);
        var connectedStatus = await runtime.ConnectProfileAsync(importedProfile.Id, CancellationToken.None);
        var overview = await runtime.GetOverviewAsync(CancellationToken.None);
        var disconnectedStatus = await runtime.DisconnectProfileAsync(importedProfile.Id, CancellationToken.None);
        await runtime.StopAsync(CancellationToken.None);

        var savedConfigurationJson = await File.ReadAllTextAsync(paths.ConfigurationFilePath, CancellationToken.None);

        Assert.Equal(TunnelConnectionState.Connected, connectedStatus.State);
        Assert.True(connectedStatus.IsMock);
        Assert.Single(overview.Profiles);
        Assert.Single(overview.TunnelStatuses);
        Assert.Equal(TunnelConnectionState.Connected, overview.TunnelStatuses[0].State);
        Assert.Equal(1, overview.SessionState.ConnectedProfileCount);
        Assert.Equal(TunnelConnectionState.Disconnected, disconnectedStatus.State);
        Assert.DoesNotContain(CreateKey(1), savedConfigurationJson, StringComparison.Ordinal);
        Assert.DoesNotContain(CreateKey(3), savedConfigurationJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeAddsAndUpdatesWin32AppRule()
    {
        var root = CreateTempDirectory();
        var paths = new AppTunnelPaths(root);
        var configurationStore = new JsonAppTunnelConfigurationStore(paths);
        var structuredLogService = new StructuredLogService(paths, "tests");
        var secretStore = new DpapiSecretStore(paths, new FakeProtector());
        var wireGuardEngine = new WireGuardTunnelEngine(
            secretStore,
            structuredLogService,
            paths,
            new WireGuardConfigParser(),
            new MockWireGuardBackend());
        var openVpnEngine = new OpenVpnTunnelEngine(
            secretStore,
            structuredLogService,
            paths,
            new OpenVpnConfigParser(),
            new StubOpenVpnBackend());
        var tunnelEngines = new ITunnelEngine[]
        {
            wireGuardEngine,
            openVpnEngine,
        };
        var runtime = new AppTunnelRuntime(
            NullLogger<AppTunnelRuntime>.Instance,
            configurationStore,
            new WindowsApplicationDiscoveryService(),
            structuredLogService,
            new FakeLogBundleExporter(),
            new FakeWfpBackendControl(),
            new ServiceTunnelManager(tunnelEngines),
            new RouterManager(structuredLogService, Array.Empty<IRouterBackend>()),
            tunnelEngines,
            Array.Empty<IRouterBackend>(),
            paths);
        var configPath = WriteConfigFile(root, "profile.conf");
        var executablePath = WritePlaceholderExecutable(root, "browser.exe");

        await runtime.StartAsync(CancellationToken.None);

        var importedProfile = await runtime.ImportProfileAsync(
            new ProfileImportRequest("Mock profile", configPath),
            CancellationToken.None);
        var createdRule = await runtime.AddAppRuleAsync(
            new AppRuleCreateRequest(
                AppKind.Win32Exe,
                "Browser",
                executablePath,
                PackageFamilyName: null,
                PackageIdentity: null),
            CancellationToken.None);
        var updatedRule = await runtime.UpdateAppRuleAsync(
            new AppRuleUpdateRequest(
                createdRule.Id,
                importedProfile.Id,
                IsEnabled: true,
                LaunchOnConnect: true,
                KillAppTrafficOnTunnelDrop: true,
                IncludeChildProcesses: true),
            CancellationToken.None);
        var overview = await runtime.GetOverviewAsync(CancellationToken.None);
        await runtime.StopAsync(CancellationToken.None);

        Assert.Equal(Path.GetFullPath(executablePath), createdRule.ExecutablePath);
        Assert.Null(createdRule.ProfileId);
        Assert.Equal(importedProfile.Id, updatedRule.ProfileId);
        Assert.True(updatedRule.LaunchOnConnect);
        Assert.True(updatedRule.KillAppTrafficOnTunnelDrop);
        Assert.True(updatedRule.IncludeChildProcesses);
        Assert.Single(overview.AppRules);
        Assert.Equal(importedProfile.Id, overview.AppRules[0].ProfileId);
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "apptunnel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteConfigFile(string root, string fileName)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllText(
            path,
            $$"""
            [Interface]
            PrivateKey = {{CreateKey(1)}}
            Address = 10.50.0.2/32
            DNS = 1.1.1.1

            [Peer]
            PublicKey = {{CreateKey(2)}}
            PresharedKey = {{CreateKey(3)}}
            AllowedIPs = 0.0.0.0/0
            Endpoint = demo.example.com:51820
            PersistentKeepalive = 25
            """,
            new UTF8Encoding(false));

        return path;
    }

    private static string WritePlaceholderExecutable(string root, string fileName)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllBytes(path, [0x4D, 0x5A]);
        return path;
    }

    private static string CreateKey(byte seed) =>
        Convert.ToBase64String(Enumerable.Repeat(seed, 32).Select(value => (byte)value).ToArray());

    private sealed class FakeProtector : IDpapiProtector
    {
        public byte[] Protect(byte[] clearText) => clearText.ToArray();

        public byte[] Unprotect(byte[] cipherText) => cipherText.ToArray();
    }

    private sealed class FakeLogBundleExporter : ILogBundleExporter
    {
        public Task<ExportedLogBundle> ExportAsync(string? destinationDirectory, CancellationToken cancellationToken) =>
            Task.FromResult(new ExportedLogBundle(
                Path.Combine(destinationDirectory ?? Path.GetTempPath(), "bundle.zip"),
                DateTimeOffset.UtcNow,
                IncludedFileCount: 0));
    }

    private sealed class FakeWfpBackendControl : IWfpBackendControl
    {
        private readonly Dictionary<Guid, WfpRuleDiagnostic> _rules = [];
        private bool _filtersEnabled;
        private bool _installed;

        public Task<WfpOperationResult> InstallAsync(CancellationToken cancellationToken)
        {
            _installed = true;
            return Task.FromResult(CreateResult(true, "install", "installed"));
        }

        public Task<WfpOperationResult> UninstallAsync(CancellationToken cancellationToken)
        {
            _installed = false;
            _filtersEnabled = false;
            _rules.Clear();
            return Task.FromResult(CreateResult(true, "uninstall", "uninstalled"));
        }

        public Task<WfpOperationResult> SetFiltersEnabledAsync(bool isEnabled, CancellationToken cancellationToken)
        {
            _filtersEnabled = isEnabled;
            return Task.FromResult(CreateResult(true, "set-filters", isEnabled ? "enabled" : "disabled"));
        }

        public Task<WfpOperationResult> SetTunnelStateAsync(bool isConnected, CancellationToken cancellationToken) =>
            Task.FromResult(CreateResult(true, "set-tunnel-state", isConnected ? "connected" : "disconnected"));

        public Task<WfpOperationResult> AddAppRuleAsync(WfpAppRuleRegistration request, CancellationToken cancellationToken)
        {
            _rules[request.RuleId] = new WfpRuleDiagnostic(
                request.RuleId,
                request.AppKind,
                request.DisplayName,
                request.ExecutablePath ?? request.PackageFamilyName ?? string.Empty,
                request.ProfileId.ToString("D"),
                request.KillAppTrafficOnTunnelDrop,
                request.IncludeChildProcesses,
                DateTimeOffset.UtcNow);
            return Task.FromResult(CreateResult(true, "add-rule", "added"));
        }

        public Task<WfpOperationResult> RemoveAppRuleAsync(Guid ruleId, CancellationToken cancellationToken)
        {
            _rules.Remove(ruleId);
            return Task.FromResult(CreateResult(true, "remove-rule", "removed"));
        }

        public Task<WfpBackendDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CreateDiagnostics());

        private WfpOperationResult CreateResult(bool succeeded, string operation, string message) =>
            new(succeeded, operation, message, CreateDiagnostics(), DateTimeOffset.UtcNow);

        private WfpBackendDiagnostics CreateDiagnostics() =>
            new(
                _installed ? WfpBackendInstallState.Installed : WfpBackendInstallState.NotInstalled,
                DriverServiceInstalled: _installed,
                BridgeReachable: true,
                FiltersEnabled: _filtersEnabled,
                DriverServiceName: "AppTunnelWfp",
                DriverDisplayName: "App Tunnel WFP Driver",
                DriverBinaryPath: "driver.sys",
                BridgeBinaryPath: "bridge.exe",
                RegisteredRuleCount: _rules.Count,
                ActiveFlowCount: 0,
                RegisteredRules: _rules.Values.ToArray(),
                ActiveFlows: [],
                Messages: [],
                UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    private sealed class StubOpenVpnBackend : IOpenVpnBackend
    {
        public string BackendName => "OpenVPN stub";

        public BackendReadiness Readiness => BackendReadiness.Mvp;

        public bool IsMock => true;

        public Task<OpenVpnBackendResult> ConnectAsync(OpenVpnServiceContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new OpenVpnBackendResult(
                TunnelConnectionState.Connected,
                "OpenVPN stub connected.",
                ErrorMessage: null,
                UpdatedAtUtc: DateTimeOffset.UtcNow));

        public Task<OpenVpnBackendResult> DisconnectAsync(OpenVpnServiceContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new OpenVpnBackendResult(
                TunnelConnectionState.Disconnected,
                "OpenVPN stub disconnected.",
                ErrorMessage: null,
                UpdatedAtUtc: DateTimeOffset.UtcNow));

        public Task<OpenVpnBackendResult> GetStatusAsync(OpenVpnServiceContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new OpenVpnBackendResult(
                TunnelConnectionState.Disconnected,
                "OpenVPN stub idle.",
                ErrorMessage: null,
                UpdatedAtUtc: DateTimeOffset.UtcNow));
    }
}

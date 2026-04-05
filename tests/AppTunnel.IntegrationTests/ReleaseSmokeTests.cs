using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Deployment;
using AppTunnel.Router.Wfp;

namespace AppTunnel.IntegrationTests;

public sealed class ReleaseSmokeTests
{
    [Fact]
    public void InstallerPackagingIncludesServiceInstallAndCleanupArtifacts()
    {
        var repoRoot = ResolveRepositoryRoot();
        var productWxs = Path.Combine(repoRoot, "packaging", "AppTunnel.Installer", "Product.wxs");
        var content = File.ReadAllText(productWxs);

        Assert.Contains("ServiceInstall", content, StringComparison.Ordinal);
        Assert.Contains("ServiceControl", content, StringComparison.Ordinal);
        Assert.Contains("Cleanup-AppTunnel.ps1", content, StringComparison.Ordinal);
    }

    [Fact]
    public void PortablePackagingPublishesSingleFileLaunchers()
    {
        var repoRoot = ResolveRepositoryRoot();
        var buildScript = Path.Combine(repoRoot, "build", "Build-Release.ps1");
        var content = File.ReadAllText(buildScript);

        Assert.Contains("AppTunnel.PortableLauncher", content, StringComparison.Ordinal);
        Assert.Contains("AppTunnel.PortableCleanup", content, StringComparison.Ordinal);
        Assert.Contains("-SingleFile", content, StringComparison.Ordinal);
        Assert.Contains("Copy-PublishTree", content, StringComparison.Ordinal);
        Assert.Contains("serviceAlwaysOverwrite", content, StringComparison.Ordinal);
    }

    [Fact]
    public void PortableFirstRunCreatesExpectedLayoutAndCleanupRemovesWfpState()
    {
        var root = CreateTempDirectory();
        var runtimeRouterDirectory = Path.Combine(root, "runtime", "router");
        var stateFilePath = Path.Combine(runtimeRouterDirectory, "AppTunnel.WfpState.json");

        PortableLayout.EnsureLayout(root);
        Directory.CreateDirectory(runtimeRouterDirectory);
        File.WriteAllText(stateFilePath, "{}");

        var plan = PortableDeploymentPlanFactory.Create(root);
        WfpDeploymentCleanup.UninstallIfPresent(root);

        Assert.True(Directory.Exists(plan.RuntimeDirectory));
        Assert.True(Directory.Exists(plan.DataDirectory));
        Assert.True(Directory.Exists(plan.LogsDirectory));
        Assert.Contains("--root", plan.ServiceOptions.Arguments, StringComparison.Ordinal);
        Assert.False(File.Exists(stateFilePath));
    }

    [Fact]
    public async Task SelectedAppOverVpnEnablesWfpFilters()
    {
        var control = new FakeWfpBackendControl();
        var backend = new WfpRouterBackend(control);

        var result = await backend.ApplyRoutingPlanAsync(
            CreateRoutingPlan(
                connected: true,
                appRules:
                [
                    CreateRule(killOnDrop: false),
                ]),
            CancellationToken.None);

        Assert.Equal(RoutingBackendKind.Wfp, result.ActiveBackend);
        Assert.Contains("enforcing", result.State, StringComparison.OrdinalIgnoreCase);
        Assert.True(control.FiltersEnabled);
        Assert.Single(result.Diagnostics.SelectedProcesses);
    }

    [Fact]
    public async Task UnselectedAppOffVpnLeavesWfpIdle()
    {
        var control = new FakeWfpBackendControl();
        var backend = new WfpRouterBackend(control);

        var result = await backend.ApplyRoutingPlanAsync(
            CreateRoutingPlan(connected: false, appRules: []),
            CancellationToken.None);

        Assert.Contains("idle", result.State, StringComparison.OrdinalIgnoreCase);
        Assert.False(control.FiltersEnabled);
        Assert.Empty(result.Diagnostics.SelectedProcesses);
    }

    [Fact]
    public async Task TunnelDropLeakPreventionIsSurfacedWhenKillOnDropRuleHasNoTunnel()
    {
        var control = new FakeWfpBackendControl();
        var backend = new WfpRouterBackend(control);

        var result = await backend.ApplyRoutingPlanAsync(
            CreateRoutingPlan(
                connected: false,
                appRules:
                [
                    CreateRule(killOnDrop: true),
                ]),
            CancellationToken.None);

        Assert.Contains(
            result.Diagnostics.ErrorStates,
            error => error.Contains("Leak prevention remains armed", StringComparison.OrdinalIgnoreCase));
    }

    private static RoutingPlan CreateRoutingPlan(bool connected, IReadOnlyList<AppRule> appRules)
    {
        var profile = new TunnelProfile(
            Guid.Parse("A7D14B91-D71E-4E49-9F00-A9BBDF65F7B2"),
            "Demo VPN",
            TunnelKind.WireGuard,
            @"C:\vpn\demo.conf",
            secretReferenceId: null,
            isEnabled: true,
            importedAtUtc: DateTimeOffset.UtcNow,
            wireGuardProfile: new WireGuardProfileDetails(
                "Demo",
                ["10.50.0.2/32"],
                ["1.1.1.1"],
                ListenPort: null,
                Mtu: null,
                Peers: []));

        var status = new TunnelStatusSnapshot(
            profile.Id,
            connected ? TunnelConnectionState.Connected : TunnelConnectionState.Disconnected,
            connected ? "Connected" : "Disconnected",
            ErrorMessage: null,
            BackendName: "WireGuard",
            IsMock: true,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        return new RoutingPlan(
            DateTimeOffset.UtcNow,
            RoutingBackendKind.Wfp,
            [profile],
            [status],
            appRules);
    }

    private static AppRule CreateRule(bool killOnDrop) =>
        new(
            Guid.Parse("36F85B76-E6D0-47F0-9884-07C57A4A2D30"),
            AppKind.Win32Exe,
            "Browser",
            @"C:\Program Files\Browser\browser.exe",
            packageFamilyName: null,
            packageIdentity: null,
            Guid.Parse("A7D14B91-D71E-4E49-9F00-A9BBDF65F7B2"),
            isEnabled: true,
            launchOnConnect: false,
            killAppTrafficOnTunnelDrop: killOnDrop,
            includeChildProcesses: true,
            updatedAtUtc: DateTimeOffset.UtcNow);

    private static string ResolveRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "AppTunnel.sln")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException("Repository root could not be resolved.");
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "apptunnel-release-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeWfpBackendControl : IWfpBackendControl
    {
        private readonly Dictionary<Guid, WfpRuleDiagnostic> _rules = [];

        public bool FiltersEnabled { get; private set; }

        public Task<WfpOperationResult> InstallAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CreateResult(true, "install", "installed"));

        public Task<WfpOperationResult> UninstallAsync(CancellationToken cancellationToken)
        {
            _rules.Clear();
            FiltersEnabled = false;
            return Task.FromResult(CreateResult(true, "uninstall", "uninstalled"));
        }

        public Task<WfpOperationResult> SetFiltersEnabledAsync(bool isEnabled, CancellationToken cancellationToken)
        {
            FiltersEnabled = isEnabled;
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
                request.ExecutablePath ?? string.Empty,
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
                WfpBackendInstallState.Installed,
                DriverServiceInstalled: true,
                BridgeReachable: true,
                FiltersEnabled,
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
}

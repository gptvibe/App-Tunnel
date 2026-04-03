using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;
using AppTunnel.Core.Security;
using AppTunnel.Core.Services;
using AppTunnel.Service.Runtime;
using AppTunnel.Vpn.OpenVpn;

namespace AppTunnel.IntegrationTests;

public sealed class OpenVpnSupportTests
{
    [Fact]
    public async Task ParserReadsReferencedMaterialAndCredentials()
    {
        var directory = CreateTempDirectory();
        var configPath = Path.Combine(directory, "sample.ovpn");
        await File.WriteAllTextAsync(Path.Combine(directory, "ca.crt"), "ca-material", new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(directory, "client.crt"), "client-cert", new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(directory, "client.key"), "client-key", new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(directory, "auth.txt"), "demo-user\nsecret-pass\n", new UTF8Encoding(false));
        await File.WriteAllTextAsync(
            configPath,
            """
            client
            dev tun
            proto udp
            remote vpn.example.com 1194
            ca ca.crt
            cert client.crt
            key client.key
            auth-user-pass auth.txt
            """,
            new UTF8Encoding(false));

        var parser = new OpenVpnConfigParser();
        var parsed = await parser.ParseAsync(configPath, requestedDisplayName: null, importOptions: null, CancellationToken.None);

        Assert.Equal("sample", parsed.DisplayName);
        Assert.Equal("tun", parsed.ProfileDetails.Device);
        Assert.Equal("udp", parsed.ProfileDetails.Protocol);
        Assert.Contains("vpn.example.com:1194", parsed.ProfileDetails.RemoteEndpoints);
        Assert.True(parsed.ProfileDetails.RequiresUsernamePassword);
        Assert.True(parsed.ProfileDetails.HasStoredCredentials);
        Assert.Equal(3, parsed.ProfileDetails.ExternalMaterialCount);
        Assert.Contains("auth-user-pass auth-user-pass.txt", parsed.SecretMaterial.NormalizedConfig, StringComparison.Ordinal);
        Assert.Equal(3, parsed.SecretMaterial.MaterialFiles.Count);
    }

    [Fact]
    public async Task ImportPersistsOpenVpnCredentialsWithoutLeakingThemIntoConfiguration()
    {
        var root = CreateTempDirectory();
        var paths = new AppTunnelPaths(root);
        var configurationStore = new JsonAppTunnelConfigurationStore(paths);
        var structuredLogService = new StructuredLogService(paths, "tests");
        var secretStore = new DpapiSecretStore(paths, new FakeProtector());
        var configPath = Path.Combine(root, "profile.ovpn");

        await File.WriteAllTextAsync(
            configPath,
            """
            client
            dev tun
            proto udp
            remote vpn.example.com 1194
            auth-user-pass
            <ca>
            ca-inline
            </ca>
            """,
            new UTF8Encoding(false));

        var engine = new OpenVpnTunnelEngine(
            secretStore,
            structuredLogService,
            paths,
            new OpenVpnConfigParser(),
            new StubOpenVpnBackend());
        var importedProfile = await engine.ImportProfileAsync(
            new ProfileImportRequest(
                "OpenVPN demo",
                configPath,
                new OpenVpnImportOptions("demo-user", "secret-pass")),
            CancellationToken.None);

        await configurationStore.SaveAsync(
            new AppTunnelConfiguration(
                [importedProfile],
                [],
                new AppTunnelSettings(
                    AppTunnelPipeNames.Control,
                    RoutingBackendKind.DryRun,
                    root,
                    RefreshIntervalSeconds: 5,
                    StartMinimizedToTray: false)),
            CancellationToken.None);

        var configurationJson = await File.ReadAllTextAsync(paths.ConfigurationFilePath, CancellationToken.None);
        var storedSecretJson = await secretStore.ReadAsync(importedProfile.SecretReferenceId!, CancellationToken.None);

        Assert.NotNull(importedProfile.OpenVpnProfile);
        Assert.True(importedProfile.OpenVpnProfile!.RequiresUsernamePassword);
        Assert.DoesNotContain("secret-pass", configurationJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ca-inline", configurationJson, StringComparison.Ordinal);
        Assert.NotNull(storedSecretJson);
        Assert.Contains("demo-user", storedSecretJson!, StringComparison.Ordinal);
        Assert.Contains("secret-pass", storedSecretJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManagedBackendTracksProcessLifecycle()
    {
        var root = CreateTempDirectory();
        var paths = new AppTunnelPaths(root);
        var structuredLogService = new StructuredLogService(paths, "tests");
        var fakeExePath = Path.Combine(root, "openvpn.exe");
        await File.WriteAllBytesAsync(fakeExePath, [0x4D, 0x5A], CancellationToken.None);

        var processFactory = new ScriptedProcessFactory([
            new ScriptedProcessScenario(
                [
                    new OpenVpnProcessOutputLine("stdout", "NOTE: starting connection"),
                    new OpenVpnProcessOutputLine("stdout", "Initialization Sequence Completed"),
                ],
                0)
        ]);
        var backend = new ManagedProcessOpenVpnBackend(
            structuredLogService,
            new OpenVpnBackendOptions(fakeExePath, ConnectTimeoutSeconds: 5),
            processFactory);
        var context = new OpenVpnServiceContext(
            Guid.NewGuid(),
            "Managed profile",
            Path.Combine(root, "runtime"),
            Path.Combine(root, "runtime", "profile.ovpn"));

        var connected = await backend.ConnectAsync(context, CancellationToken.None);
        var status = await backend.GetStatusAsync(context, CancellationToken.None);
        var disconnected = await backend.DisconnectAsync(context, CancellationToken.None);

        Assert.Equal(TunnelConnectionState.Connected, connected.State);
        Assert.Equal(TunnelConnectionState.Connected, status.State);
        Assert.Equal(TunnelConnectionState.Disconnected, disconnected.State);
        Assert.True(processFactory.LastProcess!.KillCalled);
    }

    [Fact]
    public async Task ParserRejectsMissingCredentialsWithReadableError()
    {
        var directory = CreateTempDirectory();
        var configPath = Path.Combine(directory, "missing-auth.ovpn");
        await File.WriteAllTextAsync(
            configPath,
            """
            client
            dev tun
            remote vpn.example.com 1194
            auth-user-pass
            """,
            new UTF8Encoding(false));

        var parser = new OpenVpnConfigParser();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            parser.ParseAsync(configPath, requestedDisplayName: null, importOptions: null, CancellationToken.None));

        Assert.Contains("requires a username and password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "apptunnel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeProtector : IDpapiProtector
    {
        public byte[] Protect(byte[] clearText) => clearText.ToArray();

        public byte[] Unprotect(byte[] cipherText) => cipherText.ToArray();
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

    private sealed record ScriptedProcessScenario(
        IReadOnlyList<OpenVpnProcessOutputLine> Output,
        int ExitCode);

    private sealed class ScriptedProcessFactory(IReadOnlyList<ScriptedProcessScenario> scenarios) : IOpenVpnProcessFactory
    {
        private int _index;

        public ScriptedProcess? LastProcess { get; private set; }

        public IOpenVpnProcess Start(
            OpenVpnProcessStartInfo startInfo,
            Action<OpenVpnProcessOutputLine> onOutput,
            Action<int> onExited)
        {
            var scenario = scenarios[_index++];
            LastProcess = new ScriptedProcess(scenario, onOutput, onExited);
            LastProcess.Start();
            return LastProcess;
        }
    }

    private sealed class ScriptedProcess(
        ScriptedProcessScenario scenario,
        Action<OpenVpnProcessOutputLine> onOutput,
        Action<int> onExited) : IOpenVpnProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _hasExited;

        public int Id => 4321;

        public bool HasExited => _hasExited;

        public int? ExitCode { get; private set; }

        public bool KillCalled { get; private set; }

        public void Start()
        {
            _ = Task.Run(async () =>
            {
                foreach (var line in scenario.Output)
                {
                    onOutput(line);
                    await Task.Delay(10);
                }
            });
        }

        public void Kill(bool entireProcessTree)
        {
            KillCalled = true;
            _hasExited = true;
            ExitCode = scenario.ExitCode;
            onExited(scenario.ExitCode);
            _exit.TrySetResult();
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _exit.Task.WaitAsync(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

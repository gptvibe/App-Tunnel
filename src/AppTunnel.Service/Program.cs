using AppTunnel.Core.Contracts;
using AppTunnel.Core.Security;
using AppTunnel.Core.Services;
using AppTunnel.Router.WinDivert;
using AppTunnel.Service;
using AppTunnel.Service.Runtime;
using AppTunnel.Vpn.OpenVpn;
using AppTunnel.Vpn.WireGuard;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "App Tunnel Service";
});

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "O";
});

var configuredRootDirectory = builder.Configuration["AppTunnel:RootDirectory"];
var appTunnelPaths = new AppTunnelPaths(
    string.IsNullOrWhiteSpace(configuredRootDirectory)
        ? AppTunnelPaths.GetDefaultRootDirectory()
        : configuredRootDirectory);

builder.Services.AddSingleton(appTunnelPaths);
builder.Services.AddSingleton<IAppTunnelConfigurationStore, JsonAppTunnelConfigurationStore>();
builder.Services.AddSingleton<IStructuredLogService>(serviceProvider =>
    new StructuredLogService(serviceProvider.GetRequiredService<AppTunnelPaths>(), "service"));
builder.Services.AddSingleton<IApplicationDiscoveryService, WindowsApplicationDiscoveryService>();
builder.Services.AddSingleton<IDpapiProtector, DpapiProtectedData>();
builder.Services.AddSingleton<ISecretStore, DpapiSecretStore>();
builder.Services.AddSingleton<ILogBundleExporter, LogBundleExporter>();
var wireGuardBackendMode = Enum.TryParse<WireGuardBackendMode>(
    builder.Configuration["AppTunnel:WireGuard:Mode"],
    ignoreCase: true,
    out var configuredWireGuardBackendMode)
    ? configuredWireGuardBackendMode
    : WireGuardBackendMode.Auto;
var wireGuardBackendOptions = new WireGuardBackendOptions(
    wireGuardBackendMode,
    builder.Configuration["AppTunnel:WireGuard:WireGuardExePath"]);

builder.Services.AddSingleton(wireGuardBackendOptions);
builder.Services.AddSingleton<WireGuardConfigParser>();
builder.Services.AddSingleton<IWireGuardBackend>(_ => WireGuardBackendFactory.Create(wireGuardBackendOptions));
builder.Services.AddSingleton<ITunnelEngine, WireGuardTunnelEngine>();
builder.Services.AddSingleton<ITunnelEngine, OpenVpnTunnelEngine>();
builder.Services.AddSingleton<IRouterBackend, WinDivertRouterBackend>();
builder.Services.AddSingleton<ServiceTunnelManager>();
builder.Services.AddSingleton<DryRunRouterManager>();
builder.Services.AddSingleton<AppTunnelRuntime>();
builder.Services.AddSingleton<IAppTunnelControlService>(serviceProvider =>
    serviceProvider.GetRequiredService<AppTunnelRuntime>());
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<AppTunnelRuntime>());
builder.Services.AddHostedService<NamedPipeControlServer>();

var host = builder.Build();
await host.RunAsync();

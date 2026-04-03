using AppTunnel.Core.Contracts;
using AppTunnel.Core.Security;
using AppTunnel.Core.Services;
using AppTunnel.Router.WinDivert;
using AppTunnel.Service;
using AppTunnel.Service.Runtime;
using AppTunnel.Vpn.OpenVpn;
using AppTunnel.Vpn.WireGuard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
var openVpnBackendOptions = new OpenVpnBackendOptions(
    builder.Configuration["AppTunnel:OpenVpn:OpenVpnExePath"],
    int.TryParse(
        builder.Configuration["AppTunnel:OpenVpn:ConnectTimeoutSeconds"],
        out var configuredOpenVpnTimeoutSeconds)
        ? configuredOpenVpnTimeoutSeconds
        : 20);

builder.Services.AddSingleton(wireGuardBackendOptions);
builder.Services.AddSingleton(openVpnBackendOptions);
builder.Services.AddSingleton<WireGuardConfigParser>();
builder.Services.AddSingleton<OpenVpnConfigParser>();
builder.Services.AddSingleton<IWireGuardBackend>(_ => WireGuardBackendFactory.Create(wireGuardBackendOptions));
builder.Services.AddSingleton<IOpenVpnProcessFactory, SystemOpenVpnProcessFactory>();
builder.Services.AddSingleton<IOpenVpnBackend, ManagedProcessOpenVpnBackend>();
builder.Services.AddSingleton<ITunnelEngine, WireGuardTunnelEngine>();
builder.Services.AddSingleton<ITunnelEngine, OpenVpnTunnelEngine>();
builder.Services.AddSingleton<IRouterBackend, WinDivertRouterBackend>();
builder.Services.AddSingleton<ServiceTunnelManager>();
builder.Services.AddSingleton<RouterManager>();
builder.Services.AddSingleton<AppTunnelRuntime>();
builder.Services.AddSingleton<IAppTunnelControlService>(serviceProvider =>
    serviceProvider.GetRequiredService<AppTunnelRuntime>());
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<AppTunnelRuntime>());
builder.Services.AddHostedService<NamedPipeControlServer>();

var host = builder.Build();
await host.RunAsync();

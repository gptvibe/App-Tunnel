using AppTunnel.Core.Contracts;
using AppTunnel.Core.Services;
using AppTunnel.Router.WinDivert;
using AppTunnel.Service;
using AppTunnel.Service.Security;
using AppTunnel.Vpn.OpenVpn;
using AppTunnel.Vpn.WireGuard;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
	options.ServiceName = "App Tunnel Service";
});

builder.Logging.AddJsonConsole(options =>
{
	options.IncludeScopes = true;
	options.TimestampFormat = "O";
});

builder.Services.AddSingleton<ITunnelEngine, WireGuardTunnelEngine>();
builder.Services.AddSingleton<ITunnelEngine, OpenVpnTunnelEngine>();
builder.Services.AddSingleton<IRouterBackend, WinDivertRouterBackend>();
builder.Services.AddSingleton<ISecretStore, DpapiSecretStore>();
builder.Services.AddSingleton<IAppTunnelControlService, InMemoryAppTunnelControlService>();
builder.Services.AddHostedService<NamedPipeControlServer>();

var host = builder.Build();
await host.RunAsync();

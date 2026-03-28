namespace AppTunnel.Core.Ipc;

public enum AppTunnelControlCommand
{
    Ping,
    GetOverview,
}

public sealed record AppTunnelControlRequest(AppTunnelControlCommand Command)
{
    public static AppTunnelControlRequest CreatePing() => new(AppTunnelControlCommand.Ping);

    public static AppTunnelControlRequest CreateGetOverview() => new(AppTunnelControlCommand.GetOverview);
}
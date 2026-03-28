using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Ipc;

public sealed record AppTunnelControlResponse(
    bool IsSuccess,
    string Message,
    PingReply? Ping,
    ServiceOverview? Overview,
    string? ErrorCode)
{
    public static AppTunnelControlResponse FromPing(PingReply pingReply) =>
        new(true, "Pong", pingReply, null, null);

    public static AppTunnelControlResponse FromOverview(ServiceOverview overview) =>
        new(true, "Overview ready", null, overview, null);

    public static AppTunnelControlResponse Failed(string errorCode, string message) =>
        new(false, message, null, null, errorCode);
}
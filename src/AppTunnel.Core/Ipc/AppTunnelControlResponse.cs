using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Ipc;

public sealed record AppTunnelControlResponse(
    bool IsSuccess,
    string Message,
    PingReply? Ping,
    ServiceOverview? Overview,
    ExportedLogBundle? LogBundle,
    string? ErrorCode)
{
    public static AppTunnelControlResponse FromPing(PingReply pingReply) =>
        new(true, "Pong", pingReply, null, null, null);

    public static AppTunnelControlResponse FromOverview(ServiceOverview overview) =>
        new(true, "Overview ready", null, overview, null, null);

    public static AppTunnelControlResponse FromLogBundle(ExportedLogBundle bundle) =>
        new(true, "Log bundle ready", null, null, bundle, null);

    public static AppTunnelControlResponse Failed(string errorCode, string message) =>
        new(false, message, null, null, null, errorCode);
}

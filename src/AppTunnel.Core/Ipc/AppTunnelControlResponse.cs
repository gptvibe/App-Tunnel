using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Ipc;

public sealed record AppTunnelControlResponse(
    bool IsSuccess,
    string Message,
    PingReply? Ping,
    ServiceOverview? Overview,
    AppTunnelSettings? Settings,
    ExportedLogBundle? LogBundle,
    TunnelProfile? Profile,
    TunnelStatusSnapshot? TunnelStatus,
    AppRule? AppRule,
    string? ErrorCode)
{
    public static AppTunnelControlResponse FromPing(PingReply pingReply) =>
        new(true, "Pong", pingReply, null, null, null, null, null, null, null);

    public static AppTunnelControlResponse FromOverview(ServiceOverview overview) =>
        new(true, "Overview ready", null, overview, null, null, null, null, null, null);

    public static AppTunnelControlResponse FromSettings(AppTunnelSettings settings, string message) =>
        new(true, message, null, null, settings, null, null, null, null, null);

    public static AppTunnelControlResponse FromLogBundle(ExportedLogBundle bundle) =>
        new(true, "Log bundle ready", null, null, null, bundle, null, null, null, null);

    public static AppTunnelControlResponse FromProfile(TunnelProfile profile, string message) =>
        new(true, message, null, null, null, null, profile, null, null, null);

    public static AppTunnelControlResponse FromTunnelStatus(TunnelStatusSnapshot tunnelStatus, string message) =>
        new(true, message, null, null, null, null, null, tunnelStatus, null, null);

    public static AppTunnelControlResponse FromAppRule(AppRule appRule, string message) =>
        new(true, message, null, null, null, null, null, null, appRule, null);

    public static AppTunnelControlResponse Failed(string errorCode, string message) =>
        new(false, message, null, null, null, null, null, null, null, errorCode);
}

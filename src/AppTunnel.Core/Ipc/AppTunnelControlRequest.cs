namespace AppTunnel.Core.Ipc;

public enum AppTunnelControlCommand
{
    Ping,
    GetOverview,
    ExportLogBundle,
}

public sealed record AppTunnelControlRequest(
    AppTunnelControlCommand Command,
    string? DestinationDirectory)
{
    public static AppTunnelControlRequest CreatePing() =>
        new(AppTunnelControlCommand.Ping, null);

    public static AppTunnelControlRequest CreateGetOverview() =>
        new(AppTunnelControlCommand.GetOverview, null);

    public static AppTunnelControlRequest CreateExportLogBundle(string? destinationDirectory = null) =>
        new(AppTunnelControlCommand.ExportLogBundle, destinationDirectory);
}

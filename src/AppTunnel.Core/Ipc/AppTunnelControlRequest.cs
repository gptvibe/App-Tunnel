using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Ipc;

public enum AppTunnelControlCommand
{
    Ping,
    GetOverview,
    ImportProfile,
    AddAppRule,
    UpdateAppRule,
    ConnectProfile,
    DisconnectProfile,
    ExportLogBundle,
}

public sealed record AppTunnelControlRequest(
    AppTunnelControlCommand Command,
    string? DestinationDirectory,
    Guid? ProfileId,
    string? DisplayName,
    string? SourcePath,
    AppRuleCreateRequest? AppRuleCreateRequest,
    AppRuleUpdateRequest? AppRuleUpdateRequest)
{
    public static AppTunnelControlRequest CreatePing() =>
        new(AppTunnelControlCommand.Ping, null, null, null, null, null, null);

    public static AppTunnelControlRequest CreateGetOverview() =>
        new(AppTunnelControlCommand.GetOverview, null, null, null, null, null, null);

    public static AppTunnelControlRequest CreateImportProfile(string sourcePath, string? displayName = null) =>
        new(AppTunnelControlCommand.ImportProfile, null, null, displayName, sourcePath, null, null);

    public static AppTunnelControlRequest CreateAddAppRule(AppRuleCreateRequest request) =>
        new(AppTunnelControlCommand.AddAppRule, null, null, null, null, request, null);

    public static AppTunnelControlRequest CreateUpdateAppRule(AppRuleUpdateRequest request) =>
        new(AppTunnelControlCommand.UpdateAppRule, null, null, null, null, null, request);

    public static AppTunnelControlRequest CreateConnectProfile(Guid profileId) =>
        new(AppTunnelControlCommand.ConnectProfile, null, profileId, null, null, null, null);

    public static AppTunnelControlRequest CreateDisconnectProfile(Guid profileId) =>
        new(AppTunnelControlCommand.DisconnectProfile, null, profileId, null, null, null, null);

    public static AppTunnelControlRequest CreateExportLogBundle(string? destinationDirectory = null) =>
        new(AppTunnelControlCommand.ExportLogBundle, destinationDirectory, null, null, null, null, null);
}

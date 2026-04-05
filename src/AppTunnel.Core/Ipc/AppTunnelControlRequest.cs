using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Ipc;

public enum AppTunnelControlCommand
{
    Ping,
    GetOverview,
    UpdateSettings,
    ImportProfile,
    AddAppRule,
    UpdateAppRule,
    ConnectProfile,
    DisconnectProfile,
    ExportLogBundle,
    InstallWfpBackend,
    UninstallWfpBackend,
    EnableWfpFilters,
    DisableWfpFilters,
    AddWfpAppRule,
    RemoveWfpAppRule,
    GetWfpDiagnostics,
}

public sealed record AppTunnelControlRequest(
    AppTunnelControlCommand Command,
    string? DestinationDirectory,
    Guid? ProfileId,
    Guid? RuleId,
    bool? IsEnabled,
    string? DisplayName,
    string? SourcePath,
    OpenVpnImportOptions? OpenVpnImportOptions,
    AppTunnelSettingsUpdateRequest? SettingsUpdateRequest,
    AppRuleCreateRequest? AppRuleCreateRequest,
    AppRuleUpdateRequest? AppRuleUpdateRequest,
    WfpAppRuleRegistration? WfpAppRuleRegistration)
{
    public static AppTunnelControlRequest CreatePing() =>
        new(AppTunnelControlCommand.Ping, null, null, null, null, null, null, null, null, null, null, null);

    public static AppTunnelControlRequest CreateGetOverview() =>
        new(AppTunnelControlCommand.GetOverview, null, null, null, null, null, null, null, null, null, null, null);

    public static AppTunnelControlRequest CreateUpdateSettings(AppTunnelSettingsUpdateRequest request) =>
        new(AppTunnelControlCommand.UpdateSettings, null, null, null, null, null, null, null, request, null, null, null);

    public static AppTunnelControlRequest CreateImportProfile(
        string sourcePath,
        string? displayName = null,
        OpenVpnImportOptions? openVpnImportOptions = null) =>
        new(AppTunnelControlCommand.ImportProfile, null, null, null, null, displayName, sourcePath, openVpnImportOptions, null, null, null, null);

    public static AppTunnelControlRequest CreateAddAppRule(AppRuleCreateRequest request) =>
        new(AppTunnelControlCommand.AddAppRule, null, null, null, null, null, null, null, null, request, null, null);

    public static AppTunnelControlRequest CreateUpdateAppRule(AppRuleUpdateRequest request) =>
        new(AppTunnelControlCommand.UpdateAppRule, null, null, null, null, null, null, null, null, null, request, null);

    public static AppTunnelControlRequest CreateConnectProfile(Guid profileId) =>
        new(AppTunnelControlCommand.ConnectProfile, null, profileId, null, null, null, null, null, null, null, null, null);

    public static AppTunnelControlRequest CreateDisconnectProfile(Guid profileId) =>
        new(AppTunnelControlCommand.DisconnectProfile, null, profileId, null, null, null, null, null, null, null, null, null);

    public static AppTunnelControlRequest CreateExportLogBundle(string? destinationDirectory = null) =>
        new(AppTunnelControlCommand.ExportLogBundle, destinationDirectory, null, null, null, null, null, null, null, null, null, null);

    public static AppTunnelControlRequest CreateInstallWfpBackend() =>
        new(AppTunnelControlCommand.InstallWfpBackend, null, null, null, null, null, null, null, null, null, null, null);

    public static AppTunnelControlRequest CreateUninstallWfpBackend() =>
        new(AppTunnelControlCommand.UninstallWfpBackend, null, null, null, null, null, null, null, null, null, null, null);

    public static AppTunnelControlRequest CreateSetWfpFiltersEnabled(bool isEnabled) =>
        new(
            isEnabled
                ? AppTunnelControlCommand.EnableWfpFilters
                : AppTunnelControlCommand.DisableWfpFilters,
            null,
            null,
            null,
            isEnabled,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    public static AppTunnelControlRequest CreateAddWfpAppRule(WfpAppRuleRegistration request) =>
        new(AppTunnelControlCommand.AddWfpAppRule, null, null, request.RuleId, null, null, null, null, null, null, null, request);

    public static AppTunnelControlRequest CreateRemoveWfpAppRule(Guid ruleId) =>
        new(AppTunnelControlCommand.RemoveWfpAppRule, null, null, ruleId, null, null, null, null, null, null, null, null);

    public static AppTunnelControlRequest CreateGetWfpDiagnostics() =>
        new(AppTunnelControlCommand.GetWfpDiagnostics, null, null, null, null, null, null, null, null, null, null, null);
}

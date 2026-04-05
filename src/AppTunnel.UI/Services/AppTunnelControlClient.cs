using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;

namespace AppTunnel.UI.Services;

public sealed class AppTunnelControlClient
{
    public async Task<PingReply> PingAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(AppTunnelControlRequest.CreatePing(), cancellationToken);
        return response.Ping ?? throw new InvalidOperationException("The service did not return a ping payload.");
    }

    public async Task<ServiceOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(AppTunnelControlRequest.CreateGetOverview(), cancellationToken);
        return response.Overview ?? throw new InvalidOperationException("The service did not return an overview payload.");
    }

    public async Task<AppTunnelSettings> UpdateSettingsAsync(
        AppTunnelSettingsUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            AppTunnelControlRequest.CreateUpdateSettings(request),
            cancellationToken);

        return response.Settings ?? throw new InvalidOperationException("The service did not return a settings payload.");
    }

    public async Task<TunnelProfile> ImportProfileAsync(
        string sourcePath,
        string? displayName = null,
        OpenVpnImportOptions? openVpnImportOptions = null,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            AppTunnelControlRequest.CreateImportProfile(sourcePath, displayName, openVpnImportOptions),
            cancellationToken);

        return response.Profile ?? throw new InvalidOperationException("The service did not return an imported profile payload.");
    }

    public async Task<AppRule> AddAppRuleAsync(
        AppRuleCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            AppTunnelControlRequest.CreateAddAppRule(request),
            cancellationToken);

        return response.AppRule ?? throw new InvalidOperationException("The service did not return an app-rule payload.");
    }

    public async Task<AppRule> UpdateAppRuleAsync(
        AppRuleUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            AppTunnelControlRequest.CreateUpdateAppRule(request),
            cancellationToken);

        return response.AppRule ?? throw new InvalidOperationException("The service did not return an updated app-rule payload.");
    }

    public async Task<TunnelStatusSnapshot> ConnectProfileAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            AppTunnelControlRequest.CreateConnectProfile(profileId),
            cancellationToken);

        return response.TunnelStatus ?? throw new InvalidOperationException("The service did not return a tunnel status payload.");
    }

    public async Task<TunnelStatusSnapshot> DisconnectProfileAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            AppTunnelControlRequest.CreateDisconnectProfile(profileId),
            cancellationToken);

        return response.TunnelStatus ?? throw new InvalidOperationException("The service did not return a tunnel status payload.");
    }

    public async Task<ExportedLogBundle> ExportLogBundleAsync(
        string? destinationDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            AppTunnelControlRequest.CreateExportLogBundle(destinationDirectory),
            cancellationToken);

        return response.LogBundle ?? throw new InvalidOperationException("The service did not return a log bundle payload.");
    }

    public async Task<WfpOperationResult> InstallWfpBackendAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(AppTunnelControlRequest.CreateInstallWfpBackend(), cancellationToken);
        return response.WfpOperation ?? throw new InvalidOperationException("The service did not return a WFP install payload.");
    }

    public async Task<WfpOperationResult> UninstallWfpBackendAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(AppTunnelControlRequest.CreateUninstallWfpBackend(), cancellationToken);
        return response.WfpOperation ?? throw new InvalidOperationException("The service did not return a WFP uninstall payload.");
    }

    public async Task<WfpOperationResult> SetWfpFiltersEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            AppTunnelControlRequest.CreateSetWfpFiltersEnabled(isEnabled),
            cancellationToken);

        return response.WfpOperation ?? throw new InvalidOperationException("The service did not return a WFP filter payload.");
    }

    public async Task<WfpOperationResult> AddWfpAppRuleAsync(
        WfpAppRuleRegistration request,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            AppTunnelControlRequest.CreateAddWfpAppRule(request),
            cancellationToken);

        return response.WfpOperation ?? throw new InvalidOperationException("The service did not return a WFP app-rule payload.");
    }

    public async Task<WfpOperationResult> RemoveWfpAppRuleAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            AppTunnelControlRequest.CreateRemoveWfpAppRule(ruleId),
            cancellationToken);

        return response.WfpOperation ?? throw new InvalidOperationException("The service did not return a WFP app-rule removal payload.");
    }

    public async Task<WfpBackendDiagnostics> GetWfpDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(AppTunnelControlRequest.CreateGetWfpDiagnostics(), cancellationToken);
        return response.WfpDiagnostics ?? throw new InvalidOperationException("The service did not return WFP diagnostics.");
    }

    private static async Task<AppTunnelControlResponse> SendAsync(
        AppTunnelControlRequest request,
        CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName: AppTunnelPipeNames.Control,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);

        await pipe.ConnectAsync(1500, cancellationToken);

        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(request, AppTunnelJson.Default));

        var responseLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            throw new InvalidOperationException("The service returned an empty response.");
        }

        var response = JsonSerializer.Deserialize<AppTunnelControlResponse>(responseLine, AppTunnelJson.Default)
            ?? throw new InvalidOperationException("The service response could not be parsed.");

        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(response.Message);
        }

        return response;
    }
}

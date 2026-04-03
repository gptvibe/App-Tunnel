using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AppTunnel.Service;

public sealed class NamedPipeControlServer(
    ILogger<NamedPipeControlServer> logger,
    IAppTunnelControlService controlService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Named pipe control server listening on {PipeName}", AppTunnelPipeNames.Control);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    AppTunnelPipeNames.Control,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(stoppingToken);
                await HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Named pipe accept loop failed.");

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        var requestLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                AppTunnelControlResponse.Failed("bad_request", "The request payload was empty."),
                AppTunnelJson.Default));
            return;
        }

        AppTunnelControlResponse response;

        try
        {
            var request = JsonSerializer.Deserialize<AppTunnelControlRequest>(requestLine, AppTunnelJson.Default);
            if (request is null)
            {
                response = AppTunnelControlResponse.Failed("bad_request", "The request payload could not be parsed.");
            }
            else
            {
                response = await DispatchAsync(request, cancellationToken);
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid named-pipe control request received.");
            response = AppTunnelControlResponse.Failed("bad_json", "The request JSON was invalid.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Control request handling failed.");
            response = AppTunnelControlResponse.Failed("service_error", ex.Message);
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, AppTunnelJson.Default));
    }

    private async Task<AppTunnelControlResponse> DispatchAsync(
        AppTunnelControlRequest request,
        CancellationToken cancellationToken)
    {
        return request.Command switch
        {
            AppTunnelControlCommand.Ping => AppTunnelControlResponse.FromPing(
                await controlService.PingAsync(cancellationToken)),
            AppTunnelControlCommand.GetOverview => AppTunnelControlResponse.FromOverview(
                await controlService.GetOverviewAsync(cancellationToken)),
            AppTunnelControlCommand.UpdateSettings => AppTunnelControlResponse.FromSettings(
                await controlService.UpdateSettingsAsync(
                    request.SettingsUpdateRequest ?? throw new InvalidOperationException("UpdateSettings requires a settings payload."),
                    cancellationToken),
                "Settings updated"),
            AppTunnelControlCommand.ImportProfile => AppTunnelControlResponse.FromProfile(
                await controlService.ImportProfileAsync(
                    new ProfileImportRequest(
                        request.DisplayName ?? string.Empty,
                        request.SourcePath ?? throw new InvalidOperationException("ImportProfile requires a source path."),
                        request.OpenVpnImportOptions),
                    cancellationToken),
                "Profile imported"),
            AppTunnelControlCommand.AddAppRule => AppTunnelControlResponse.FromAppRule(
                await controlService.AddAppRuleAsync(
                    request.AppRuleCreateRequest ?? throw new InvalidOperationException("AddAppRule requires an app-rule create payload."),
                    cancellationToken),
                "App rule added"),
            AppTunnelControlCommand.UpdateAppRule => AppTunnelControlResponse.FromAppRule(
                await controlService.UpdateAppRuleAsync(
                    request.AppRuleUpdateRequest ?? throw new InvalidOperationException("UpdateAppRule requires an app-rule update payload."),
                    cancellationToken),
                "App rule updated"),
            AppTunnelControlCommand.ConnectProfile => AppTunnelControlResponse.FromTunnelStatus(
                await controlService.ConnectProfileAsync(
                    request.ProfileId ?? throw new InvalidOperationException("ConnectProfile requires a profile ID."),
                    cancellationToken),
                "Profile connected"),
            AppTunnelControlCommand.DisconnectProfile => AppTunnelControlResponse.FromTunnelStatus(
                await controlService.DisconnectProfileAsync(
                    request.ProfileId ?? throw new InvalidOperationException("DisconnectProfile requires a profile ID."),
                    cancellationToken),
                "Profile disconnected"),
            AppTunnelControlCommand.ExportLogBundle => AppTunnelControlResponse.FromLogBundle(
                await controlService.ExportLogBundleAsync(request.DestinationDirectory, cancellationToken)),
            _ => AppTunnelControlResponse.Failed("unknown_command", $"Unsupported command '{request.Command}'."),
        };
    }
}

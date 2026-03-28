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
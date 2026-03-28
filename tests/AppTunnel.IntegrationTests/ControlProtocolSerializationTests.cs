using System.Text.Json;
using AppTunnel.Core.Ipc;

namespace AppTunnel.IntegrationTests;

public sealed class ControlProtocolSerializationTests
{
    [Fact]
    public void RequestSerializesCommandAsString()
    {
        var json = JsonSerializer.Serialize(AppTunnelControlRequest.CreateGetOverview(), AppTunnelJson.Default);

        Assert.Contains("GetOverview", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseRoundTripsSuccessEnvelope()
    {
        var json = """
            {
              "isSuccess": true,
              "message": "Pong",
              "ping": {
                "serviceName": "App Tunnel Service",
                "timestampUtc": "2026-03-27T00:00:00+00:00",
                "protocolVersion": "scaffold-v1"
              },
              "overview": null,
              "errorCode": null
            }
            """;

        var response = JsonSerializer.Deserialize<AppTunnelControlResponse>(json, AppTunnelJson.Default);

        Assert.NotNull(response);
        Assert.True(response!.IsSuccess);
        Assert.Equal("scaffold-v1", response.Ping!.ProtocolVersion);
    }
}
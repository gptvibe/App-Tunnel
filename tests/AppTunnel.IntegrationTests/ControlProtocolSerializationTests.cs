using System.Text.Json;
using AppTunnel.Core.Domain;
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
    public void ImportRequestRoundTripsSourcePath()
    {
      var json = JsonSerializer.Serialize(
        AppTunnelControlRequest.CreateImportProfile(@"C:\vpn\demo.conf", "Demo"),
        AppTunnelJson.Default);

      Assert.Contains("ImportProfile", json, StringComparison.Ordinal);
      Assert.Contains(@"C:\\vpn\\demo.conf", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAppRuleRequestSerializesNestedPayload()
    {
      var json = JsonSerializer.Serialize(
        AppTunnelControlRequest.CreateAddAppRule(new AppRuleCreateRequest(
          AppKind.Win32Exe,
          "Browser",
          @"C:\Program Files\Browser\browser.exe",
          PackageFamilyName: null,
          PackageIdentity: null)),
        AppTunnelJson.Default);

      Assert.Contains("AddAppRule", json, StringComparison.Ordinal);
      Assert.Contains("Browser", json, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateSettingsRequestSerializesPreferredBackend()
    {
      var json = JsonSerializer.Serialize(
        AppTunnelControlRequest.CreateUpdateSettings(
          new AppTunnelSettingsUpdateRequest(RoutingBackendKind.WinDivert)),
        AppTunnelJson.Default);

      Assert.Contains("UpdateSettings", json, StringComparison.Ordinal);
      Assert.Contains("WinDivert", json, StringComparison.Ordinal);
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
                "protocolVersion": "scaffold-v1",
                "runState": "Running"
              },
              "overview": null,
              "settings": null,
              "logBundle": null,
              "profile": null,
              "tunnelStatus": null,
              "appRule": null,
              "errorCode": null
            }
            """;

        var response = JsonSerializer.Deserialize<AppTunnelControlResponse>(json, AppTunnelJson.Default);

        Assert.NotNull(response);
        Assert.True(response!.IsSuccess);
        Assert.Equal("scaffold-v1", response.Ping!.ProtocolVersion);
    }
}

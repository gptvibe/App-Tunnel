using AppTunnel.Core.Domain;
using AppTunnel.Vpn.OpenVpn;

namespace AppTunnel.IntegrationTests;

public sealed class OpenVpnImportTests
{
    [Fact]
    public async Task ParseAsync_RequiresCredentials_WhenAuthUserPassHasNoInlineFile()
    {
        var parser = new OpenVpnConfigParser();
        var sourcePath = CreateTempConfig(
            """
            client
            dev tun
            proto tcp
            remote us-sea.prod.surfshark.com 1443
            auth-user-pass
            <ca>
            -----BEGIN CERTIFICATE-----
            demo
            -----END CERTIFICATE-----
            </ca>
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            parser.ParseAsync(sourcePath, "Surfshark SEA", importOptions: null, CancellationToken.None));

        Assert.Contains("requires a username and password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ParseAsync_AcceptsProvidedCredentials_ForAuthUserPassProfiles()
    {
        var parser = new OpenVpnConfigParser();
        var sourcePath = CreateTempConfig(
            """
            client
            dev tun
            proto tcp
            remote us-sea.prod.surfshark.com 1443
            auth-user-pass
            <ca>
            -----BEGIN CERTIFICATE-----
            demo
            -----END CERTIFICATE-----
            </ca>
            """);

        var parsed = await parser.ParseAsync(
            sourcePath,
            "Surfshark SEA",
            new OpenVpnImportOptions("demo-user", "demo-pass"),
            CancellationToken.None);

        Assert.Equal("Surfshark SEA", parsed.DisplayName);
        Assert.True(parsed.ProfileDetails.RequiresUsernamePassword);
        Assert.True(parsed.ProfileDetails.HasStoredCredentials);
        Assert.Contains("us-sea.prod.surfshark.com:1443", parsed.ProfileDetails.RemoteEndpoints);
        Assert.Contains("auth-user-pass auth-user-pass.txt", parsed.SecretMaterial.NormalizedConfig, StringComparison.Ordinal);
    }

    private static string CreateTempConfig(string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), "apptunnel-openvpn-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "profile.ovpn");
        File.WriteAllText(sourcePath, contents.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
        return sourcePath;
    }
}

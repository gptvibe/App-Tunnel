using System.Text;
using AppTunnel.Vpn.WireGuard;

namespace AppTunnel.IntegrationTests;

public sealed class WireGuardConfigParserTests
{
    [Fact]
    public async Task ParseAsyncReadsMetadataWithoutSecrets()
    {
        var parser = new WireGuardConfigParser();
        var configPath = WriteConfigFile(
            "valid.conf",
            $$"""
            [Interface]
            PrivateKey = {{CreateKey(1)}}
            Address = 10.10.0.2/32, fd00::2/128
            DNS = 1.1.1.1, vpn.internal
            ListenPort = 51820
            MTU = 1380

            [Peer]
            PublicKey = {{CreateKey(2)}}
            PresharedKey = {{CreateKey(3)}}
            AllowedIPs = 0.0.0.0/0, ::/0
            Endpoint = demo.example.com:51820
            PersistentKeepalive = 25
            """);

        var parsed = await parser.ParseAsync(configPath, requestedDisplayName: null, CancellationToken.None);

        Assert.Equal("valid", parsed.DisplayName);
        Assert.Equal(2, parsed.Interface.Addresses.Count);
        Assert.Equal(2, parsed.Interface.DnsServers.Count);
        Assert.Single(parsed.Peers);
        Assert.Equal("demo.example.com:51820", parsed.Peers[0].Endpoint);
        Assert.Equal(25, parsed.Peers[0].PersistentKeepalive);
    }

    [Fact]
    public async Task ParseAsyncRejectsUnsupportedDangerousDirectives()
    {
        var parser = new WireGuardConfigParser();
        var configPath = WriteConfigFile(
            "invalid.conf",
            $$"""
            [Interface]
            PrivateKey = {{CreateKey(1)}}
            Address = 10.10.0.2/32
            PostUp = calc.exe

            [Peer]
            PublicKey = {{CreateKey(2)}}
            AllowedIPs = 0.0.0.0/0
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            parser.ParseAsync(configPath, requestedDisplayName: null, CancellationToken.None));

        Assert.Contains("not supported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string WriteConfigFile(string fileName, string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), "apptunnel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        return path;
    }

    private static string CreateKey(byte seed) =>
        Convert.ToBase64String(Enumerable.Repeat(seed, 32).Select(value => (byte)value).ToArray());
}
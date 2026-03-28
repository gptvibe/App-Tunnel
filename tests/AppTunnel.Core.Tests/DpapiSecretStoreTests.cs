using System.Text;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Security;
using AppTunnel.Core.Services;

namespace AppTunnel.Core.Tests;

public sealed class DpapiSecretStoreTests
{
    [Fact]
    public void DpapiProtectedDataRoundTripsPayload()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new DpapiProtectedData();
        var clearText = "super-secret-value";

        var cipherText = protector.Protect(Encoding.UTF8.GetBytes(clearText));
        var roundTrip = Encoding.UTF8.GetString(protector.Unprotect(cipherText));

        Assert.Equal(clearText, roundTrip);
    }

    [Fact]
    public async Task SecretStorePersistsAndReadsProtectedPayload()
    {
        var root = CreateTempDirectory();
        var paths = new AppTunnelPaths(root);
        var protector = new FakeProtector();
        var store = new DpapiSecretStore(paths, protector);

        var reference = await store.StoreAsync(
            "WireGuard token",
            SecretPurpose.ProfileBlob,
            "alpha-bravo",
            CancellationToken.None);

        var secretPath = Path.Combine(paths.SecretsDirectory, $"{reference.SecretId}.secret.json");

        Assert.True(File.Exists(secretPath));
        Assert.Equal("alpha-bravo", await store.ReadAsync(reference.SecretId, CancellationToken.None));

        await store.DeleteAsync(reference.SecretId, CancellationToken.None);

        Assert.False(File.Exists(secretPath));
        Assert.Null(await store.ReadAsync(reference.SecretId, CancellationToken.None));
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "apptunnel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeProtector : IDpapiProtector
    {
        public byte[] Protect(byte[] clearText) => clearText.ToArray();

        public byte[] Unprotect(byte[] cipherText) => cipherText.ToArray();
    }
}

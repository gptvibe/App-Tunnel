using System.Text;
using System.Text.Json;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;
using AppTunnel.Core.Services;

namespace AppTunnel.Core.Security;

public sealed class DpapiSecretStore(
    AppTunnelPaths paths,
    IDpapiProtector protector) : ISecretStore
{
    public async Task<StoredSecretReference> StoreAsync(
        string displayName,
        SecretPurpose purpose,
        string secretValue,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(secretValue))
        {
            throw new ArgumentException("Secret value is required.", nameof(secretValue));
        }

        cancellationToken.ThrowIfCancellationRequested();
        paths.EnsureDirectories();

        var secretId = Guid.NewGuid().ToString("N");
        var updatedAtUtc = DateTimeOffset.UtcNow;
        var payload = Encoding.UTF8.GetBytes(secretValue);
        var cipherText = protector.Protect(payload);

        var secretRecord = new SecretRecord(
            displayName,
            purpose,
            updatedAtUtc,
            Convert.ToBase64String(cipherText));

        await File.WriteAllTextAsync(
            GetSecretPath(secretId),
            JsonSerializer.Serialize(
                secretRecord,
                new JsonSerializerOptions(AppTunnelJson.Default)
                {
                    WriteIndented = true,
                }),
            cancellationToken);

        return new StoredSecretReference(secretId, displayName, purpose, updatedAtUtc);
    }

    public async Task<string?> ReadAsync(string secretId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        paths.EnsureDirectories();

        var secretPath = GetSecretPath(secretId);
        if (!File.Exists(secretPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(secretPath, cancellationToken);
        var record = JsonSerializer.Deserialize<SecretRecord>(json, AppTunnelJson.Default);
        if (record is null)
        {
            return null;
        }

        var clearText = protector.Unprotect(Convert.FromBase64String(record.CipherText));
        return Encoding.UTF8.GetString(clearText);
    }

    public Task DeleteAsync(string secretId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        paths.EnsureDirectories();

        var secretPath = GetSecretPath(secretId);
        if (File.Exists(secretPath))
        {
            File.Delete(secretPath);
        }

        return Task.CompletedTask;
    }

    private string GetSecretPath(string secretId) =>
        Path.Combine(paths.SecretsDirectory, $"{secretId}.secret.json");

    private sealed record SecretRecord(
        string DisplayName,
        SecretPurpose Purpose,
        DateTimeOffset UpdatedAtUtc,
        string CipherText);
}

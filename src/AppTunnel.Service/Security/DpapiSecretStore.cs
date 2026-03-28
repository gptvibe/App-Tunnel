using System.Security.Cryptography;
using System.Text;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Service.Security;

public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _rootDirectory;

    public DpapiSecretStore()
    {
        _rootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AppTunnel",
            "Secrets");

        Directory.CreateDirectory(_rootDirectory);
    }

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

        var secretId = Guid.NewGuid().ToString("N");
        var clearTextBytes = Encoding.UTF8.GetBytes(secretValue);
        var protectedBytes = ProtectedData.Protect(clearTextBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);

        await File.WriteAllBytesAsync(GetSecretPath(secretId), protectedBytes, cancellationToken);

        return new StoredSecretReference(secretId, displayName, purpose, DateTimeOffset.UtcNow);
    }

    public async Task<string?> ReadAsync(string secretId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var secretPath = GetSecretPath(secretId);
        if (!File.Exists(secretPath))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(secretPath, cancellationToken);
        var clearTextBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(clearTextBytes);
    }

    public Task DeleteAsync(string secretId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var secretPath = GetSecretPath(secretId);
        if (File.Exists(secretPath))
        {
            File.Delete(secretPath);
        }

        return Task.CompletedTask;
    }

    private string GetSecretPath(string secretId) => Path.Combine(_rootDirectory, $"{secretId}.bin");
}
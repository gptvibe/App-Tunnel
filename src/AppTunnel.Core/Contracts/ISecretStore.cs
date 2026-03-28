using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Contracts;

public interface ISecretStore
{
    Task<StoredSecretReference> StoreAsync(
        string displayName,
        SecretPurpose purpose,
        string secretValue,
        CancellationToken cancellationToken);

    Task<string?> ReadAsync(string secretId, CancellationToken cancellationToken);

    Task DeleteAsync(string secretId, CancellationToken cancellationToken);
}
namespace AppTunnel.Core.Domain;

public sealed record OpenVpnProfileDetails(
    string Device,
    string? Protocol,
    IReadOnlyList<string> RemoteEndpoints,
    bool RequiresUsernamePassword,
    bool HasStoredCredentials,
    int InlineMaterialCount,
    int ExternalMaterialCount,
    OpenVpnValidationResult Validation);

public sealed record OpenVpnValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

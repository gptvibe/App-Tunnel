using System.Text;
using System.Text.Json;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;
using AppTunnel.Core.Services;

namespace AppTunnel.Vpn.OpenVpn;

public sealed class OpenVpnTunnelEngine(
    ISecretStore secretStore,
    IStructuredLogService structuredLogService,
    AppTunnelPaths paths,
    OpenVpnConfigParser configParser,
    IOpenVpnBackend backend) : ITunnelEngine
{
    public TunnelKind TunnelKind => TunnelKind.OpenVpn;

    public string DisplayName => "OpenVPN";

    public BackendReadiness Readiness => backend.Readiness;

    public async Task<TunnelProfile> ImportProfileAsync(ProfileImportRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parsedConfig = await configParser.ParseAsync(
            request.SourcePath,
            request.DisplayName,
            request.OpenVpnOptions,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var secretReference = await secretStore.StoreAsync(
            parsedConfig.DisplayName,
            SecretPurpose.ProfileBlob,
            JsonSerializer.Serialize(parsedConfig.SecretMaterial, AppTunnelJson.Default),
            cancellationToken);

        var profile = new TunnelProfile(
            Guid.NewGuid(),
            parsedConfig.DisplayName,
            TunnelKind,
            Path.GetFullPath(request.SourcePath),
            secretReference.SecretId,
            isEnabled: true,
            importedAtUtc: now,
            wireGuardProfile: null,
            openVpnProfile: parsedConfig.ProfileDetails);

        await structuredLogService.WriteAsync(
            "Information",
            nameof(OpenVpnTunnelEngine),
            "Imported OpenVPN profile.",
            new Dictionary<string, string>
            {
                ["profileId"] = profile.Id.ToString("D"),
                ["displayName"] = profile.DisplayName,
                ["sourcePath"] = profile.ImportedConfigPath,
                ["remoteCount"] = profile.OpenVpnProfile?.RemoteEndpoints.Count.ToString() ?? "0",
                ["requiresCredentials"] = profile.OpenVpnProfile?.RequiresUsernamePassword.ToString() ?? "False",
                ["backend"] = backend.BackendName,
            },
            cancellationToken);

        return profile;
    }

    public async Task<TunnelStatusSnapshot> ConnectAsync(TunnelProfile profile, CancellationToken cancellationToken)
    {
        var context = BuildContext(profile);

        await structuredLogService.WriteAsync(
            "Information",
            nameof(OpenVpnTunnelEngine),
            "Connecting OpenVPN profile.",
            BuildProperties(profile),
            cancellationToken);

        try
        {
            await StageRuntimeFilesAsync(profile, context, cancellationToken);
            var status = ToStatus(profile.Id, await backend.ConnectAsync(context, cancellationToken));

            await structuredLogService.WriteAsync(
                status.State == TunnelConnectionState.Faulted ? "Error" : "Information",
                nameof(OpenVpnTunnelEngine),
                "OpenVPN connect request completed.",
                BuildProperties(profile, status),
                cancellationToken);

            if (status.State is TunnelConnectionState.Disconnected or TunnelConnectionState.Faulted)
            {
                TryDeleteRuntimeDirectory(context.RuntimeDirectory);
            }

            return status;
        }
        catch (Exception ex)
        {
            TryDeleteRuntimeDirectory(context.RuntimeDirectory);

            await structuredLogService.WriteAsync(
                "Error",
                nameof(OpenVpnTunnelEngine),
                "OpenVPN connect request failed.",
                BuildProperties(profile, errorMessage: ex.Message),
                cancellationToken);

            return CreateFaultedStatus(profile.Id, ex.Message);
        }
    }

    public async Task<TunnelStatusSnapshot> DisconnectAsync(TunnelProfile profile, CancellationToken cancellationToken)
    {
        var context = BuildContext(profile);

        await structuredLogService.WriteAsync(
            "Information",
            nameof(OpenVpnTunnelEngine),
            "Disconnecting OpenVPN profile.",
            BuildProperties(profile),
            cancellationToken);

        try
        {
            var status = ToStatus(profile.Id, await backend.DisconnectAsync(context, cancellationToken));
            if (status.State is TunnelConnectionState.Disconnected or TunnelConnectionState.Faulted)
            {
                TryDeleteRuntimeDirectory(context.RuntimeDirectory);
            }

            await structuredLogService.WriteAsync(
                status.State == TunnelConnectionState.Faulted ? "Warning" : "Information",
                nameof(OpenVpnTunnelEngine),
                "OpenVPN disconnect request completed.",
                BuildProperties(profile, status),
                cancellationToken);

            return status;
        }
        catch (Exception ex)
        {
            await structuredLogService.WriteAsync(
                "Error",
                nameof(OpenVpnTunnelEngine),
                "OpenVPN disconnect request failed.",
                BuildProperties(profile, errorMessage: ex.Message),
                cancellationToken);

            return CreateFaultedStatus(profile.Id, ex.Message);
        }
    }

    public async Task<TunnelStatusSnapshot> GetStatusAsync(TunnelProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            return ToStatus(profile.Id, await backend.GetStatusAsync(BuildContext(profile), cancellationToken));
        }
        catch (Exception ex)
        {
            return CreateFaultedStatus(profile.Id, ex.Message);
        }
    }

    private async Task StageRuntimeFilesAsync(
        TunnelProfile profile,
        OpenVpnServiceContext context,
        CancellationToken cancellationToken)
    {
        var secretMaterial = await ReadSecretMaterialAsync(profile, cancellationToken);
        Directory.CreateDirectory(context.RuntimeDirectory);

        await File.WriteAllTextAsync(
            context.ConfigPath,
            secretMaterial.NormalizedConfig,
            new UTF8Encoding(false),
            cancellationToken);

        foreach (var materialFile in secretMaterial.MaterialFiles)
        {
            var filePath = Path.Combine(
                context.RuntimeDirectory,
                materialFile.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllBytesAsync(
                filePath,
                Convert.FromBase64String(materialFile.Base64Contents),
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(secretMaterial.Username)
            || !string.IsNullOrWhiteSpace(secretMaterial.Password))
        {
            await File.WriteAllTextAsync(
                Path.Combine(context.RuntimeDirectory, "auth-user-pass.txt"),
                $"{secretMaterial.Username}{Environment.NewLine}{secretMaterial.Password}{Environment.NewLine}",
                new UTF8Encoding(false),
                cancellationToken);
        }
    }

    private async Task<OpenVpnSecretMaterial> ReadSecretMaterialAsync(
        TunnelProfile profile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.SecretReferenceId))
        {
            throw new InvalidOperationException("The imported OpenVPN profile is missing its DPAPI secret reference.");
        }

        var json = await secretStore.ReadAsync(profile.SecretReferenceId, cancellationToken)
            ?? throw new InvalidOperationException("The imported OpenVPN secret material could not be loaded.");

        return JsonSerializer.Deserialize<OpenVpnSecretMaterial>(json, AppTunnelJson.Default)
            ?? throw new InvalidOperationException("The stored OpenVPN secret material is invalid.");
    }

    private OpenVpnServiceContext BuildContext(TunnelProfile profile)
    {
        if (profile.TunnelKind != TunnelKind.OpenVpn)
        {
            throw new InvalidOperationException($"Profile '{profile.DisplayName}' is not an OpenVPN profile.");
        }

        if (profile.OpenVpnProfile is null)
        {
            throw new InvalidOperationException($"Profile '{profile.DisplayName}' is missing OpenVPN metadata.");
        }

        var runtimeDirectory = Path.Combine(paths.RootDirectory, "runtime", "openvpn", profile.Id.ToString("N"));
        var configPath = Path.Combine(runtimeDirectory, "profile.ovpn");

        return new OpenVpnServiceContext(
            profile.Id,
            profile.DisplayName,
            runtimeDirectory,
            configPath);
    }

    private TunnelStatusSnapshot ToStatus(Guid profileId, OpenVpnBackendResult backendResult) =>
        new(
            profileId,
            backendResult.State,
            backendResult.Summary,
            backendResult.ErrorMessage,
            backend.BackendName,
            backend.IsMock,
            backendResult.UpdatedAtUtc);

    private TunnelStatusSnapshot CreateFaultedStatus(Guid profileId, string errorMessage) =>
        new(
            profileId,
            TunnelConnectionState.Faulted,
            "OpenVPN backend reported an error.",
            errorMessage,
            backend.BackendName,
            backend.IsMock,
            DateTimeOffset.UtcNow);

    private static void TryDeleteRuntimeDirectory(string runtimeDirectory)
    {
        if (Directory.Exists(runtimeDirectory))
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    private Dictionary<string, string> BuildProperties(
        TunnelProfile profile,
        TunnelStatusSnapshot? status = null,
        string? errorMessage = null)
    {
        var properties = new Dictionary<string, string>
        {
            ["profileId"] = profile.Id.ToString("D"),
            ["displayName"] = profile.DisplayName,
            ["backend"] = backend.BackendName,
            ["requiresCredentials"] = profile.OpenVpnProfile?.RequiresUsernamePassword.ToString() ?? "False",
        };

        if (status is not null)
        {
            properties["state"] = status.State.ToString();
            properties["summary"] = status.Summary;
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            properties["error"] = errorMessage;
        }

        return properties;
    }
}

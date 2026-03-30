using System.Text;
using System.Text.Json;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;
using AppTunnel.Core.Services;

namespace AppTunnel.Vpn.WireGuard;

public sealed class WireGuardTunnelEngine(
    ISecretStore secretStore,
    IStructuredLogService structuredLogService,
    AppTunnelPaths paths,
    WireGuardConfigParser configParser,
    IWireGuardBackend backend) : ITunnelEngine
{
    public TunnelKind TunnelKind => TunnelKind.WireGuard;

    public string DisplayName => "WireGuard";

    public BackendReadiness Readiness => backend.Readiness;

    public async Task<TunnelProfile> ImportProfileAsync(ProfileImportRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parsedConfig = await configParser.ParseAsync(request.SourcePath, request.DisplayName, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var profile = new TunnelProfile(
            Guid.NewGuid(),
            parsedConfig.DisplayName,
            TunnelKind,
            Path.GetFullPath(request.SourcePath),
            secretReferenceId: (await secretStore.StoreAsync(
                parsedConfig.DisplayName,
                SecretPurpose.ProfileBlob,
                JsonSerializer.Serialize(BuildSecretMaterial(parsedConfig), AppTunnelJson.Default),
                cancellationToken)).SecretId,
            isEnabled: true,
            importedAtUtc: now,
            wireGuardProfile: new WireGuardProfileDetails(
                parsedConfig.DisplayName,
                parsedConfig.Interface.Addresses,
                parsedConfig.Interface.DnsServers,
                parsedConfig.Interface.ListenPort,
                parsedConfig.Interface.Mtu,
                parsedConfig.Peers
                    .Select(peer => new WireGuardPeerDetails(
                        peer.PublicKey,
                        peer.Endpoint,
                        peer.AllowedIps,
                        !string.IsNullOrWhiteSpace(peer.PresharedKey),
                        peer.PersistentKeepalive))
                    .ToArray()));

        await structuredLogService.WriteAsync(
            "Information",
            nameof(WireGuardTunnelEngine),
            "Imported WireGuard profile.",
            new Dictionary<string, string>
            {
                ["profileId"] = profile.Id.ToString("D"),
                ["displayName"] = profile.DisplayName,
                ["sourcePath"] = profile.ImportedConfigPath,
                ["peerCount"] = profile.WireGuardProfile?.Peers.Count.ToString() ?? "0",
                ["backend"] = backend.BackendName,
                ["isMockBackend"] = backend.IsMock.ToString(),
            },
            cancellationToken);

        return profile;
    }

    public async Task<TunnelStatusSnapshot> ConnectAsync(TunnelProfile profile, CancellationToken cancellationToken)
    {
        var context = BuildContext(profile);

        await structuredLogService.WriteAsync(
            "Information",
            nameof(WireGuardTunnelEngine),
            "Connecting WireGuard profile.",
            BuildProperties(profile),
            cancellationToken);

        try
        {
            if (!backend.IsMock)
            {
                await StageRuntimeConfigAsync(profile, context.ConfigPath, cancellationToken);
            }

            var status = ToStatus(profile.Id, await backend.ConnectAsync(context, cancellationToken));

            await structuredLogService.WriteAsync(
                status.State == TunnelConnectionState.Connected ? "Information" : "Warning",
                nameof(WireGuardTunnelEngine),
                "WireGuard profile connect completed.",
                BuildProperties(profile, status),
                cancellationToken);

            return status;
        }
        catch (Exception ex)
        {
            TryDeleteRuntimeConfig(context.ConfigPath);

            await structuredLogService.WriteAsync(
                "Error",
                nameof(WireGuardTunnelEngine),
                "WireGuard profile connect failed.",
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
            nameof(WireGuardTunnelEngine),
            "Disconnecting WireGuard profile.",
            BuildProperties(profile),
            cancellationToken);

        try
        {
            var status = ToStatus(profile.Id, await backend.DisconnectAsync(context, cancellationToken));
            if (status.State is TunnelConnectionState.Disconnected or TunnelConnectionState.Faulted)
            {
                TryDeleteRuntimeConfig(context.ConfigPath);
            }

            await structuredLogService.WriteAsync(
                status.State == TunnelConnectionState.Disconnected ? "Information" : "Warning",
                nameof(WireGuardTunnelEngine),
                "WireGuard profile disconnect completed.",
                BuildProperties(profile, status),
                cancellationToken);

            return status;
        }
        catch (Exception ex)
        {
            await structuredLogService.WriteAsync(
                "Error",
                nameof(WireGuardTunnelEngine),
                "WireGuard profile disconnect failed.",
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

    private async Task StageRuntimeConfigAsync(
        TunnelProfile profile,
        string configPath,
        CancellationToken cancellationToken)
    {
        var secretMaterial = await ReadSecretMaterialAsync(profile, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        await File.WriteAllTextAsync(
            configPath,
            RenderConfiguration(profile, secretMaterial),
            new UTF8Encoding(false),
            cancellationToken);
    }

    private async Task<WireGuardSecretMaterial> ReadSecretMaterialAsync(
        TunnelProfile profile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.SecretReferenceId))
        {
            throw new InvalidOperationException("The imported WireGuard profile is missing its DPAPI secret reference.");
        }

        var json = await secretStore.ReadAsync(profile.SecretReferenceId, cancellationToken)
            ?? throw new InvalidOperationException("The imported WireGuard secret material could not be loaded.");

        return JsonSerializer.Deserialize<WireGuardSecretMaterial>(json, AppTunnelJson.Default)
            ?? throw new InvalidOperationException("The stored WireGuard secret material is invalid.");
    }

    private WireGuardServiceContext BuildContext(TunnelProfile profile)
    {
        if (profile.TunnelKind != TunnelKind.WireGuard)
        {
            throw new InvalidOperationException($"Profile '{profile.DisplayName}' is not a WireGuard profile.");
        }

        if (profile.WireGuardProfile is null)
        {
            throw new InvalidOperationException($"Profile '{profile.DisplayName}' is missing WireGuard metadata.");
        }

        var tunnelName = BuildTunnelName(profile);
        var configPath = Path.Combine(paths.RootDirectory, "runtime", "wireguard", $"{tunnelName}.conf");

        return new WireGuardServiceContext(
            profile.Id,
            profile.DisplayName,
            tunnelName,
            $"WireGuardTunnel${tunnelName}",
            configPath);
    }

    private static string BuildTunnelName(TunnelProfile profile)
    {
        var baseName = profile.WireGuardProfile?.InterfaceName ?? profile.DisplayName;
        var safeCharacters = baseName
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        var sanitized = new string(safeCharacters).Trim('-');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "profile";
        }

        var prefix = sanitized.Length > 24 ? sanitized[..24] : sanitized;
        return $"apptunnel-{prefix}-{profile.Id:N}";
    }

    private static string RenderConfiguration(TunnelProfile profile, WireGuardSecretMaterial secretMaterial)
    {
        var details = profile.WireGuardProfile
            ?? throw new InvalidOperationException("WireGuard profile metadata is required.");

        var builder = new StringBuilder();
        builder.AppendLine("[Interface]");
        builder.AppendLine($"PrivateKey = {secretMaterial.PrivateKey}");
        builder.AppendLine($"Address = {string.Join(", ", details.Addresses)}");

        if (details.DnsServers.Count > 0)
        {
            builder.AppendLine($"DNS = {string.Join(", ", details.DnsServers)}");
        }

        if (details.ListenPort.HasValue)
        {
            builder.AppendLine($"ListenPort = {details.ListenPort.Value}");
        }

        if (details.Mtu.HasValue)
        {
            builder.AppendLine($"MTU = {details.Mtu.Value}");
        }

        foreach (var peer in details.Peers)
        {
            builder.AppendLine();
            builder.AppendLine("[Peer]");
            builder.AppendLine($"PublicKey = {peer.PublicKey}");

            var peerSecret = secretMaterial.Peers.SingleOrDefault(candidate =>
                string.Equals(candidate.PublicKey, peer.PublicKey, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(peerSecret?.PresharedKey))
            {
                builder.AppendLine($"PresharedKey = {peerSecret.PresharedKey}");
            }

            builder.AppendLine($"AllowedIPs = {string.Join(", ", peer.AllowedIps)}");

            if (!string.IsNullOrWhiteSpace(peer.Endpoint))
            {
                builder.AppendLine($"Endpoint = {peer.Endpoint}");
            }

            if (peer.PersistentKeepaliveSeconds.HasValue)
            {
                builder.AppendLine($"PersistentKeepalive = {peer.PersistentKeepaliveSeconds.Value}");
            }
        }

        return builder.ToString();
    }

    private static WireGuardSecretMaterial BuildSecretMaterial(WireGuardParsedConfig parsedConfig) =>
        new(
            parsedConfig.Interface.PrivateKey,
            parsedConfig.Peers
                .Where(peer => !string.IsNullOrWhiteSpace(peer.PresharedKey))
                .Select(peer => new WireGuardPeerSecretMaterial(peer.PublicKey, peer.PresharedKey))
                .ToArray());

    private TunnelStatusSnapshot ToStatus(Guid profileId, WireGuardBackendResult backendResult) =>
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
            "WireGuard backend reported an error.",
            errorMessage,
            backend.BackendName,
            backend.IsMock,
            DateTimeOffset.UtcNow);

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
            ["isMockBackend"] = backend.IsMock.ToString(),
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

    private static void TryDeleteRuntimeConfig(string configPath)
    {
        if (File.Exists(configPath))
        {
            File.Delete(configPath);
        }
    }

    private sealed record WireGuardSecretMaterial(
        string PrivateKey,
        IReadOnlyList<WireGuardPeerSecretMaterial> Peers);

    private sealed record WireGuardPeerSecretMaterial(
        string PublicKey,
        string? PresharedKey);
}

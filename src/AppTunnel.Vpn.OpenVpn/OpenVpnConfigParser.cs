using System.Text;
using AppTunnel.Core.Domain;

namespace AppTunnel.Vpn.OpenVpn;

public sealed class OpenVpnConfigParser
{
    private static readonly HashSet<string> DangerousDirectives =
    [
        "auth-user-pass-verify",
        "client-connect",
        "client-crresponse",
        "client-disconnect",
        "down",
        "ipchange",
        "learn-address",
        "management",
        "management-client-auth",
        "plugin",
        "route-up",
        "script-security",
        "tls-verify",
        "up",
    ];

    private static readonly HashSet<string> InlineMaterialTags =
    [
        "ca",
        "cert",
        "extra-certs",
        "key",
        "pkcs12",
        "secret",
        "tls-auth",
        "tls-crypt",
        "tls-crypt-v2",
    ];

    private static readonly HashSet<string> ExternalMaterialDirectives =
    [
        "ca",
        "cert",
        "crl-verify",
        "extra-certs",
        "key",
        "pkcs12",
        "secret",
        "tls-auth",
        "tls-crypt",
        "tls-crypt-v2",
    ];

    public async Task<OpenVpnParsedConfig> ParseAsync(
        string sourcePath,
        string? requestedDisplayName,
        OpenVpnImportOptions? importOptions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source path is required.", nameof(sourcePath));
        }

        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The OpenVPN configuration file could not be found.", fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".ovpn", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OpenVPN imports require a .ovpn profile.");
        }

        var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
        var sourceDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The OpenVPN profile directory could not be resolved.");
        var displayName = !string.IsNullOrWhiteSpace(requestedDisplayName)
            ? requestedDisplayName.Trim()
            : Path.GetFileNameWithoutExtension(fullPath);

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("The OpenVPN profile name could not be derived from the file name.");
        }

        var normalizedConfig = new StringBuilder();
        var warnings = new List<string>();
        var storedFiles = new List<OpenVpnStoredMaterialFile>();
        var remotes = new List<string>();
        string? device = null;
        string? protocol = null;
        string? pendingInlineTag = null;
        var inlineMaterialCount = 0;
        var externalMaterialCount = 0;
        var requiresUsernamePassword = false;
        var username = NormalizeOptional(importOptions?.Username);
        var password = NormalizeOptional(importOptions?.Password);

        for (var index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lineNumber = index + 1;
            var rawLine = lines[index];
            var trimmedLine = rawLine.Trim();

            if (pendingInlineTag is not null)
            {
                normalizedConfig.AppendLine(rawLine);

                if (string.Equals(trimmedLine, $"</{pendingInlineTag}>", StringComparison.OrdinalIgnoreCase))
                {
                    pendingInlineTag = null;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                normalizedConfig.AppendLine(rawLine);
                continue;
            }

            if (trimmedLine.StartsWith('#') || trimmedLine.StartsWith(';'))
            {
                normalizedConfig.AppendLine(rawLine);
                continue;
            }

            if (TryGetInlineBlockTag(trimmedLine, out var inlineTag))
            {
                if (!InlineMaterialTags.Contains(inlineTag))
                {
                    throw CreateError(lineNumber, $"Inline block '<{inlineTag}>' is not supported by the MVP import path.");
                }

                inlineMaterialCount++;
                pendingInlineTag = inlineTag;
                normalizedConfig.AppendLine(rawLine);
                continue;
            }

            var content = StripComments(rawLine).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                normalizedConfig.AppendLine();
                continue;
            }

            var tokens = Tokenize(content);
            if (tokens.Count == 0)
            {
                normalizedConfig.AppendLine(rawLine);
                continue;
            }

            var directive = tokens[0].ToLowerInvariant();
            var arguments = tokens.Skip(1).ToArray();

            if (DangerousDirectives.Contains(directive))
            {
                throw CreateError(lineNumber, $"Directive '{directive}' is not supported because the service blocks script and management hooks.");
            }

            switch (directive)
            {
                case "remote":
                    if (arguments.Length == 0)
                    {
                        throw CreateError(lineNumber, "Directive 'remote' requires a hostname or IP address.");
                    }

                    remotes.Add(arguments.Length > 1 ? $"{arguments[0]}:{arguments[1]}" : arguments[0]);
                    normalizedConfig.AppendLine(content);
                    break;

                case "dev":
                    if (arguments.Length == 0)
                    {
                        throw CreateError(lineNumber, "Directive 'dev' requires a device name.");
                    }

                    device ??= arguments[0];
                    normalizedConfig.AppendLine(content);
                    break;

                case "proto":
                    if (arguments.Length == 0)
                    {
                        throw CreateError(lineNumber, "Directive 'proto' requires a protocol value.");
                    }

                    protocol ??= arguments[0];
                    normalizedConfig.AppendLine(content);
                    break;

                case "auth-user-pass":
                    requiresUsernamePassword = true;

                    if (arguments.Length > 1)
                    {
                        throw CreateError(lineNumber, "Directive 'auth-user-pass' accepts at most one optional credentials path.");
                    }

                    if (arguments.Length == 1)
                    {
                        var credentialPath = ResolveReferencedPath(sourceDirectory, arguments[0]);
                        if (!File.Exists(credentialPath))
                        {
                            throw CreateError(lineNumber, $"Credential file '{arguments[0]}' could not be found.");
                        }

                        var credentialLines = await File.ReadAllLinesAsync(credentialPath, cancellationToken);
                        if (credentialLines.Length < 2)
                        {
                            throw CreateError(lineNumber, $"Credential file '{arguments[0]}' must contain a username on the first line and a password on the second line.");
                        }

                        username ??= NormalizeOptional(credentialLines[0]);
                        password ??= NormalizeOptional(credentialLines[1]);
                    }

                    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    {
                        throw CreateError(lineNumber, "This OpenVPN profile requires a username and password. Provide them during import or include a readable auth-user-pass file.");
                    }

                    normalizedConfig.AppendLine("auth-user-pass auth-user-pass.txt");
                    break;

                default:
                    if (ExternalMaterialDirectives.Contains(directive))
                    {
                        if (arguments.Length == 0)
                        {
                            throw CreateError(lineNumber, $"Directive '{directive}' requires a file path.");
                        }

                        var referencedPath = ResolveReferencedPath(sourceDirectory, arguments[0]);
                        if (!File.Exists(referencedPath))
                        {
                            throw CreateError(lineNumber, $"Referenced file '{arguments[0]}' for directive '{directive}' could not be found.");
                        }

                        var relativePath = BuildMaterialRelativePath(directive, externalMaterialCount + 1, referencedPath);
                        storedFiles.Add(new OpenVpnStoredMaterialFile(
                            relativePath,
                            Convert.ToBase64String(await File.ReadAllBytesAsync(referencedPath, cancellationToken))));
                        externalMaterialCount++;

                        var remainingArguments = arguments.Skip(1).ToArray();
                        normalizedConfig.AppendLine(RenderDirective(directive, relativePath, remainingArguments));
                    }
                    else
                    {
                        normalizedConfig.AppendLine(content);
                    }

                    break;
            }
        }

        if (pendingInlineTag is not null)
        {
            throw new InvalidOperationException($"OpenVPN config ended before closing '</{pendingInlineTag}>'.");
        }

        if (remotes.Count == 0)
        {
            warnings.Add("No 'remote' directive was discovered during import. Profiles that rely on advanced connection blocks may still work, but endpoint diagnostics will be incomplete.");
        }

        if (string.IsNullOrWhiteSpace(device))
        {
            warnings.Add("No explicit 'dev' directive was discovered. The backend will rely on the OpenVPN runtime defaults.");
        }

        var profileDetails = new OpenVpnProfileDetails(
            Device: string.IsNullOrWhiteSpace(device) ? "default" : device,
            Protocol: protocol,
            RemoteEndpoints: remotes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            RequiresUsernamePassword: requiresUsernamePassword,
            HasStoredCredentials: !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password),
            InlineMaterialCount: inlineMaterialCount,
            ExternalMaterialCount: externalMaterialCount,
            Validation: new OpenVpnValidationResult(
                IsValid: true,
                Errors: [],
                Warnings: warnings));

        return new OpenVpnParsedConfig(
            displayName,
            new OpenVpnSecretMaterial(
                normalizedConfig.ToString(),
                username,
                password,
                storedFiles),
            profileDetails);
    }

    private static bool TryGetInlineBlockTag(string trimmedLine, out string tag)
    {
        tag = string.Empty;

        if (!trimmedLine.StartsWith('<') || !trimmedLine.EndsWith('>') || trimmedLine.StartsWith("</", StringComparison.Ordinal))
        {
            return false;
        }

        tag = trimmedLine[1..^1].Trim().ToLowerInvariant();
        return tag.Length > 0;
    }

    private static string StripComments(string line)
    {
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && (current == '#' || current == ';'))
            {
                return line[..index];
            }
        }

        return line;
    }

    private static IReadOnlyList<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in input)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (inQuotes)
        {
            throw new InvalidOperationException("OpenVPN config contains an unterminated quoted value.");
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string ResolveReferencedPath(string sourceDirectory, string reference)
    {
        var normalizedReference = reference.Trim().Trim('"');
        return Path.GetFullPath(Path.IsPathRooted(normalizedReference)
            ? normalizedReference
            : Path.Combine(sourceDirectory, normalizedReference));
    }

    private static string BuildMaterialRelativePath(string directive, int index, string referencedPath)
    {
        var extension = Path.GetExtension(referencedPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = directive switch
            {
                "pkcs12" => ".p12",
                "key" => ".key",
                _ => ".pem",
            };
        }

        return $"materials/{directive}-{index}{extension}".Replace('\\', '/');
    }

    private static string RenderDirective(string directive, string path, IReadOnlyList<string> remainingArguments)
    {
        var builder = new StringBuilder();
        builder.Append(directive);
        builder.Append(' ');
        builder.Append('"');
        builder.Append(path);
        builder.Append('"');

        foreach (var argument in remainingArguments)
        {
            builder.Append(' ');
            builder.Append(argument);
        }

        return builder.ToString();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InvalidOperationException CreateError(int lineNumber, string message) =>
        new($"OpenVPN config error at line {lineNumber}: {message}");
}

public sealed record OpenVpnParsedConfig(
    string DisplayName,
    OpenVpnSecretMaterial SecretMaterial,
    OpenVpnProfileDetails ProfileDetails);

public sealed record OpenVpnSecretMaterial(
    string NormalizedConfig,
    string? Username,
    string? Password,
    IReadOnlyList<OpenVpnStoredMaterialFile> MaterialFiles);

public sealed record OpenVpnStoredMaterialFile(
    string RelativePath,
    string Base64Contents);

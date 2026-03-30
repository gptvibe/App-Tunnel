using System.Net;

namespace AppTunnel.Vpn.WireGuard;

public sealed class WireGuardConfigParser
{
    private static readonly HashSet<string> SupportedInterfaceKeys =
    [
        "privatekey",
        "address",
        "dns",
        "listenport",
        "mtu",
    ];

    private static readonly HashSet<string> SupportedPeerKeys =
    [
        "publickey",
        "presharedkey",
        "allowedips",
        "endpoint",
        "persistentkeepalive",
    ];

    private static readonly HashSet<string> UnsupportedDangerousKeys =
    [
        "preup",
        "postup",
        "predown",
        "postdown",
        "saveconfig",
        "table",
    ];

    public async Task<WireGuardParsedConfig> ParseAsync(
        string sourcePath,
        string? requestedDisplayName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source path is required.", nameof(sourcePath));
        }

        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The WireGuard configuration file could not be found.", fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".conf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("WireGuard imports require a .conf profile.");
        }

        var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
        var interfaceSection = new MutableInterfaceSection();
        var peerSections = new List<MutablePeerSection>();
        MutablePeerSection? currentPeer = null;
        ConfigSection currentSection = ConfigSection.None;

        for (var index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lineNumber = index + 1;
            var line = StripComments(lines[index]).Trim();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var sectionName = line[1..^1].Trim();
                currentSection = sectionName.ToLowerInvariant() switch
                {
                    "interface" => ConfigSection.Interface,
                    "peer" => ConfigSection.Peer,
                    _ => throw CreateError(lineNumber, $"Unsupported section '[{sectionName}]'."),
                };

                if (currentSection == ConfigSection.Peer)
                {
                    currentPeer = new MutablePeerSection();
                    peerSections.Add(currentPeer);
                }

                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
            {
                throw CreateError(lineNumber, "Expected 'key = value' syntax.");
            }

            var key = line[..separatorIndex].Trim().ToLowerInvariant();
            var value = line[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw CreateError(lineNumber, $"Setting '{key}' cannot be empty.");
            }

            if (UnsupportedDangerousKeys.Contains(key))
            {
                throw CreateError(lineNumber, $"Setting '{key}' is not supported in App Tunnel imports.");
            }

            switch (currentSection)
            {
                case ConfigSection.Interface:
                    ParseInterfaceKey(interfaceSection, key, value, lineNumber);
                    break;
                case ConfigSection.Peer when currentPeer is not null:
                    ParsePeerKey(currentPeer, key, value, lineNumber);
                    break;
                default:
                    throw CreateError(lineNumber, "A setting must appear under [Interface] or [Peer].");
            }
        }

        var displayName = !string.IsNullOrWhiteSpace(requestedDisplayName)
            ? requestedDisplayName.Trim()
            : Path.GetFileNameWithoutExtension(fullPath);

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("The WireGuard profile name could not be derived from the file name.");
        }

        if (peerSections.Count == 0)
        {
            throw new InvalidOperationException("WireGuard config requires at least one [Peer] section.");
        }

        return new WireGuardParsedConfig(
            displayName,
            interfaceSection.Build(),
            peerSections.Select(static peer => peer.Build()).ToArray());
    }

    private static void ParseInterfaceKey(MutableInterfaceSection section, string key, string value, int lineNumber)
    {
        if (!SupportedInterfaceKeys.Contains(key))
        {
            throw CreateError(lineNumber, $"Unsupported [Interface] setting '{key}'.");
        }

        switch (key)
        {
            case "privatekey":
                EnsureWireGuardKey(value, key, lineNumber);
                AssignSingle(ref section.PrivateKey, value, key, lineNumber);
                break;
            case "address":
                section.Addresses.AddRange(ParseCsv(value, token => ValidateIpOrCidr(token, key, lineNumber), key, lineNumber));
                break;
            case "dns":
                section.DnsServers.AddRange(ParseCsv(value, token => ValidateDnsToken(token, lineNumber), key, lineNumber));
                break;
            case "listenport":
                AssignSingle(ref section.ListenPort, ParsePort(value, allowZero: false, key, lineNumber), key, lineNumber);
                break;
            case "mtu":
                AssignSingle(ref section.Mtu, ParsePositiveInt(value, key, lineNumber), key, lineNumber);
                break;
        }
    }

    private static void ParsePeerKey(MutablePeerSection section, string key, string value, int lineNumber)
    {
        if (!SupportedPeerKeys.Contains(key))
        {
            throw CreateError(lineNumber, $"Unsupported [Peer] setting '{key}'.");
        }

        switch (key)
        {
            case "publickey":
                EnsureWireGuardKey(value, key, lineNumber);
                AssignSingle(ref section.PublicKey, value, key, lineNumber);
                break;
            case "presharedkey":
                EnsureWireGuardKey(value, key, lineNumber);
                AssignSingle(ref section.PresharedKey, value, key, lineNumber);
                break;
            case "allowedips":
                section.AllowedIps.AddRange(ParseCsv(value, token => ValidateIpOrCidr(token, key, lineNumber), key, lineNumber));
                break;
            case "endpoint":
                AssignSingle(ref section.Endpoint, ValidateEndpoint(value, lineNumber), key, lineNumber);
                break;
            case "persistentkeepalive":
                AssignSingle(ref section.PersistentKeepalive, ParsePort(value, allowZero: true, key, lineNumber), key, lineNumber);
                break;
        }
    }

    private static string StripComments(string line)
    {
        var commentIndex = line.IndexOf('#');
        if (commentIndex >= 0)
        {
            line = line[..commentIndex];
        }

        commentIndex = line.IndexOf(';');
        return commentIndex >= 0 ? line[..commentIndex] : line;
    }

    private static IReadOnlyList<string> ParseCsv(
        string value,
        Func<string, string> validator,
        string key,
        int lineNumber)
    {
        var values = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(validator)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0
            ? throw CreateError(lineNumber, $"Setting '{key}' requires at least one value.")
            : values;
    }

    private static void EnsureWireGuardKey(string value, string key, int lineNumber)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length != 32)
            {
                throw CreateError(lineNumber, $"Setting '{key}' must decode to 32 bytes.");
            }
        }
        catch (FormatException)
        {
            throw CreateError(lineNumber, $"Setting '{key}' is not a valid base64 WireGuard key.");
        }
    }

    private static string ValidateIpOrCidr(string value, string key, int lineNumber)
    {
        if (IPAddress.TryParse(value, out _))
        {
            return value;
        }

        var slashIndex = value.IndexOf('/');
        if (slashIndex <= 0 || slashIndex == value.Length - 1)
        {
            throw CreateError(lineNumber, $"Setting '{key}' contains invalid address '{value}'.");
        }

        var addressPart = value[..slashIndex];
        var prefixPart = value[(slashIndex + 1)..];
        if (!IPAddress.TryParse(addressPart, out var address))
        {
            throw CreateError(lineNumber, $"Setting '{key}' contains invalid address '{value}'.");
        }

        if (!int.TryParse(prefixPart, out var prefixLength))
        {
            throw CreateError(lineNumber, $"Setting '{key}' contains invalid CIDR prefix '{value}'.");
        }

        var maxPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > maxPrefix)
        {
            throw CreateError(lineNumber, $"Setting '{key}' contains out-of-range CIDR prefix '{value}'.");
        }

        return value;
    }

    private static string ValidateDnsToken(string value, int lineNumber)
    {
        if (IPAddress.TryParse(value, out _))
        {
            return value;
        }

        if (Uri.CheckHostName(value) != UriHostNameType.Unknown)
        {
            return value;
        }

        throw CreateError(lineNumber, $"DNS value '{value}' is invalid.");
    }

    private static string ValidateEndpoint(string value, int lineNumber)
    {
        string host;
        string port;

        if (value.StartsWith('['))
        {
            var closingIndex = value.IndexOf(']');
            if (closingIndex <= 1 || closingIndex == value.Length - 1 || value[closingIndex + 1] != ':')
            {
                throw CreateError(lineNumber, $"Endpoint '{value}' is invalid.");
            }

            host = value[1..closingIndex];
            port = value[(closingIndex + 2)..];
        }
        else
        {
            var lastColon = value.LastIndexOf(':');
            if (lastColon <= 0 || lastColon == value.Length - 1)
            {
                throw CreateError(lineNumber, $"Endpoint '{value}' must include host and port.");
            }

            host = value[..lastColon];
            port = value[(lastColon + 1)..];
        }

        if (Uri.CheckHostName(host) == UriHostNameType.Unknown && !IPAddress.TryParse(host, out _))
        {
            throw CreateError(lineNumber, $"Endpoint host '{host}' is invalid.");
        }

        _ = ParsePort(port, allowZero: false, "endpoint", lineNumber);
        return value;
    }

    private static int ParsePort(string value, bool allowZero, string key, int lineNumber)
    {
        if (!int.TryParse(value, out var port))
        {
            throw CreateError(lineNumber, $"Setting '{key}' must be numeric.");
        }

        var minimum = allowZero ? 0 : 1;
        if (port < minimum || port > 65535)
        {
            throw CreateError(lineNumber, $"Setting '{key}' must be between {minimum} and 65535.");
        }

        return port;
    }

    private static int ParsePositiveInt(string value, string key, int lineNumber)
    {
        if (!int.TryParse(value, out var parsedValue) || parsedValue <= 0)
        {
            throw CreateError(lineNumber, $"Setting '{key}' must be a positive integer.");
        }

        return parsedValue;
    }

    private static void AssignSingle<T>(ref T? slot, T value, string key, int lineNumber)
    {
        if (slot is not null)
        {
            throw CreateError(lineNumber, $"Setting '{key}' may only appear once per section.");
        }

        slot = value;
    }

    private static InvalidOperationException CreateError(int lineNumber, string message) =>
        new($"WireGuard config error at line {lineNumber}: {message}");

    private enum ConfigSection
    {
        None,
        Interface,
        Peer,
    }

    private sealed class MutableInterfaceSection
    {
        public string? PrivateKey;

        public List<string> Addresses { get; } = [];

        public List<string> DnsServers { get; } = [];

        public int? ListenPort;

        public int? Mtu;

        public WireGuardParsedInterface Build()
        {
            if (string.IsNullOrWhiteSpace(PrivateKey))
            {
                throw new InvalidOperationException("WireGuard config requires [Interface] PrivateKey.");
            }

            if (Addresses.Count == 0)
            {
                throw new InvalidOperationException("WireGuard config requires at least one [Interface] Address.");
            }

            return new WireGuardParsedInterface(
                PrivateKey,
                Addresses.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                DnsServers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                ListenPort,
                Mtu);
        }
    }

    private sealed class MutablePeerSection
    {
        public string? PublicKey;

        public string? PresharedKey;

        public List<string> AllowedIps { get; } = [];

        public string? Endpoint;

        public int? PersistentKeepalive;

        public WireGuardParsedPeer Build()
        {
            if (string.IsNullOrWhiteSpace(PublicKey))
            {
                throw new InvalidOperationException("Each [Peer] section requires PublicKey.");
            }

            if (AllowedIps.Count == 0)
            {
                throw new InvalidOperationException("Each [Peer] section requires AllowedIPs.");
            }

            return new WireGuardParsedPeer(
                PublicKey,
                PresharedKey,
                Endpoint,
                AllowedIps.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                PersistentKeepalive);
        }
    }
}

public sealed record WireGuardParsedConfig(
    string DisplayName,
    WireGuardParsedInterface Interface,
    IReadOnlyList<WireGuardParsedPeer> Peers);

public sealed record WireGuardParsedInterface(
    string PrivateKey,
    IReadOnlyList<string> Addresses,
    IReadOnlyList<string> DnsServers,
    int? ListenPort,
    int? Mtu);

public sealed record WireGuardParsedPeer(
    string PublicKey,
    string? PresharedKey,
    string? Endpoint,
    IReadOnlyList<string> AllowedIps,
    int? PersistentKeepalive);
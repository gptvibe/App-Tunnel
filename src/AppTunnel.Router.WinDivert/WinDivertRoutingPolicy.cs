using System.Net;
using System.Net.NetworkInformation;
using AppTunnel.Core.Domain;

namespace AppTunnel.Router.WinDivert;

public sealed record WinDivertResolvedPlan(
    IReadOnlyDictionary<Guid, WinDivertRuleBinding> RuleBindings,
    IReadOnlyDictionary<Guid, WinDivertTunnelBinding> TunnelBindings,
    IReadOnlyList<string> Errors,
    string ActiveTunnel);

public sealed record WinDivertRuleBinding(
    Guid RuleId,
    string DisplayName,
    string ExecutablePath,
    Guid ProfileId,
    bool IncludeChildProcesses);

public sealed record WinDivertTunnelBinding(
    Guid ProfileId,
    string DisplayName,
    TunnelConnectionState State,
    IPAddress? LocalAddress,
    uint InterfaceIndex,
    uint SubInterfaceIndex,
    IReadOnlyList<IPAddress> DnsServers,
    string Summary);

public static class WinDivertRoutingPolicy
{
    public static WinDivertResolvedPlan Resolve(RoutingPlan routingPlan)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);

        var ruleBindings = routingPlan.AppRules
            .Where(rule =>
                rule.IsEnabled
                && rule.AppKind == AppKind.Win32Exe
                && rule.ProfileId.HasValue
                && !string.IsNullOrWhiteSpace(rule.ExecutablePath))
            .Select(rule => new WinDivertRuleBinding(
                rule.Id,
                rule.DisplayName,
                rule.ExecutablePath!,
                rule.ProfileId!.Value,
                rule.IncludeChildProcesses))
            .ToDictionary(rule => rule.RuleId);

        var errors = new List<string>();
        var tunnelBindings = new Dictionary<Guid, WinDivertTunnelBinding>();

        foreach (var profile in routingPlan.Profiles)
        {
            if (!ruleBindings.Values.Any(rule => rule.ProfileId == profile.Id))
            {
                continue;
            }

            var status = routingPlan.TunnelStatuses.FirstOrDefault(candidate => candidate.ProfileId == profile.Id)
                ?? new TunnelStatusSnapshot(
                    profile.Id,
                    TunnelConnectionState.Unknown,
                    "No tunnel status reported.",
                    null,
                    "Unavailable",
                    IsMock: false,
                    DateTimeOffset.UtcNow);

            tunnelBindings[profile.Id] = ResolveTunnelBinding(profile, status, errors);
        }

        var activeTunnel = tunnelBindings.Values
            .Where(binding => binding.State == TunnelConnectionState.Connected)
            .Select(binding => binding.Summary)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WinDivertResolvedPlan(
            ruleBindings,
            tunnelBindings,
            errors,
            activeTunnel.Length switch
            {
                0 => "None",
                1 => activeTunnel[0],
                _ => string.Join(", ", activeTunnel),
            });
    }

    private static WinDivertTunnelBinding ResolveTunnelBinding(
        TunnelProfile profile,
        TunnelStatusSnapshot status,
        ICollection<string> errors)
    {
        var ipv4Address = profile.WireGuardProfile?.Addresses
            .Select(ParseConfiguredAddress)
            .FirstOrDefault(address => address?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

        var dnsServers = profile.WireGuardProfile?.DnsServers
            .Select(ParseDnsServer)
            .Where(address => address is not null)
            .Cast<IPAddress>()
            .ToArray() ?? [];

        if (status.State != TunnelConnectionState.Connected)
        {
            return new WinDivertTunnelBinding(
                profile.Id,
                profile.DisplayName,
                status.State,
                ipv4Address,
                InterfaceIndex: 0,
                SubInterfaceIndex: 0,
                dnsServers,
                $"{profile.DisplayName} ({status.State})");
        }

        if (ipv4Address is null)
        {
            errors.Add($"Profile '{profile.DisplayName}' does not expose an IPv4 tunnel address.");
            return new WinDivertTunnelBinding(
                profile.Id,
                profile.DisplayName,
                status.State,
                LocalAddress: null,
                InterfaceIndex: 0,
                SubInterfaceIndex: 0,
                dnsServers,
                $"{profile.DisplayName} (missing IPv4 address)");
        }

        var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(candidate => candidate.GetIPProperties().UnicastAddresses
                .Any(unicast => unicast.Address.Equals(ipv4Address)));

        if (networkInterface is null)
        {
            errors.Add($"Profile '{profile.DisplayName}' is connected, but its interface address '{ipv4Address}' was not found.");
            return new WinDivertTunnelBinding(
                profile.Id,
                profile.DisplayName,
                status.State,
                ipv4Address,
                InterfaceIndex: 0,
                SubInterfaceIndex: 0,
                dnsServers,
                $"{profile.DisplayName} (interface not found)");
        }

        var ipv4Properties = networkInterface.GetIPProperties().GetIPv4Properties();
        return new WinDivertTunnelBinding(
            profile.Id,
            profile.DisplayName,
            status.State,
            ipv4Address,
            (uint?)ipv4Properties?.Index ?? 0,
            SubInterfaceIndex: 0,
            dnsServers,
            $"{profile.DisplayName} ({networkInterface.Name})");
    }

    private static IPAddress? ParseConfiguredAddress(string configuredAddress)
    {
        if (string.IsNullOrWhiteSpace(configuredAddress))
        {
            return null;
        }

        var slashIndex = configuredAddress.IndexOf('/');
        var token = slashIndex >= 0
            ? configuredAddress[..slashIndex]
            : configuredAddress;

        return IPAddress.TryParse(token.Trim(), out var address)
            ? address
            : null;
    }

    private static IPAddress? ParseDnsServer(string value) =>
        IPAddress.TryParse(value, out var address)
            ? address
            : null;
}

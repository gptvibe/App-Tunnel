# WinDivert MVP Backend

## Architecture

`AppTunnel.Router.WinDivert` is the first functional routing backend for App Tunnel.

- The service persists a preferred routing backend in `AppTunnelSettings.PreferredRoutingBackend`.
- `RouterManager` builds a `RoutingPlan` from:
  - imported tunnel profiles
  - current tunnel status snapshots
  - enabled app rules that are assigned to a profile
- `WinDivertRoutingPolicy` resolves:
  - which Win32 executables are selected
  - which connected tunnel interface each selected rule should use
  - which tunnel DNS servers should be enforced for selected DNS traffic
- `WinDivertRouterBackend` opens three WinDivert layers:
  - `SOCKET` for process/socket metadata
  - `FLOW` for endpoint lifecycle metadata
  - `NETWORK` for IPv4 packet interception and reinjection
- Selected flows are tracked as 5-tuples plus process metadata.
- For matched IPv4 TCP/UDP packets, the backend:
  - rewrites the effective local address to the tunnel IPv4 address
  - redirects DNS port 53 traffic to the tunnel DNS server when one is configured
  - reinjects selected outbound traffic toward the resolved tunnel interface
  - rewrites inbound packets back to the original local/remote tuple before reinjection
- If the assigned tunnel is not connected, selected packets are dropped instead of allowed onto the normal network.

## Admin Requirements

WinDivert capture and packet reinjection require administrator privileges.

- The service must run elevated for `WinDivert` routing mode.
- If the service is not elevated, the backend reports an error state and does not start live capture loops.
- The UI surfaces this as `Elevation required` in routing diagnostics.

## Known PID And Flow Limitations

- The MVP matches selected processes by exact Win32 executable path.
- Child-process inclusion is best-effort only; the `IncludeChildProcesses` rule flag is not a full parent/child ancestry tracker yet.
- Flow tracking is IPv4 TCP/UDP-first.
- Raw sockets, ICMP, and other non-TCP/UDP traffic are not fully attributed to a selected process in this MVP.
- PID attribution depends on WinDivert `SOCKET`/`FLOW` metadata arriving before packet handling for a new flow.
- Packaged-app identities are still unsupported by live routing enforcement.

## Performance Caveats

- The backend currently inspects all IPv4 packets at the WinDivert network layer and decides whether to pass, rewrite, or drop them.
- Packet parsing, checksum recomputation, and user-mode reinjection add overhead compared with a kernel-mode WFP implementation.
- Diagnostics keep recent selected processes and mapped flows in memory for UI visibility.
- High connection churn or very high packet rates may increase CPU usage noticeably.

## Unsupported Edge Cases

- IPv6 packet routing is not implemented yet, though the shared routing model is shaped to allow it later.
- DNS-over-HTTPS and DNS-over-TLS are not blocked or redirected by the MVP. Only classic port `53` DNS traffic is handled.
- Multiple simultaneously connected tunnels are visible, but the MVP routing behavior is still tuned for one selected tunnel per flow.
- Tunnel interfaces that do not expose a routable IPv4 address cannot be used by this backend.
- If a tunnel disconnects outside the App Tunnel service, enforcement updates on the next status refresh cycle rather than through a dedicated kernel callback.

## Diagnostics UI

The desktop UI exposes:

- selected process activity
- mapped flows
- dropped packet counters
- active tunnel summary
- elevation and other error states

This data comes from `ServiceOverview.RouterDiagnostics`.

## Validation Steps

Run these on a real elevated Windows machine with WinDivert available and a working WireGuard tunnel.

### 1. Selected app uses VPN IP

1. Start the App Tunnel service elevated.
2. Import a WireGuard profile whose peer provides internet egress.
3. Set routing backend to `WinDivert`.
4. Assign a Win32 browser or `curl.exe` rule to the connected profile.
5. Launch the selected app and visit an IP-echo endpoint such as `https://ifconfig.me` or `https://api.ipify.org`.
6. Confirm the reported public IP matches the VPN egress IP.
7. In the diagnostics UI, confirm:
   - the process appears under selected processes
   - mapped flows increase
   - active tunnel matches the assigned profile

### 2. Unselected app stays on normal IP

1. Leave a second browser or `curl.exe` instance unassigned.
2. Query the same IP-echo endpoint from the unselected app.
3. Confirm the reported public IP matches the normal WAN IP instead of the VPN IP.
4. Confirm the unselected app does not appear under selected-process diagnostics.

### 3. Tunnel down blocks selected traffic

1. Keep the selected app running.
2. Disconnect the assigned tunnel from App Tunnel.
3. Retry the IP-echo request from the selected app.
4. Confirm the request fails instead of succeeding over the normal interface.
5. Confirm diagnostics show:
   - dropped packet counters increasing
   - the active tunnel no longer connected
   - an error or blocked state for mapped flows

## Suggested Scripted Checks

These commands are useful during manual validation:

```powershell
# Selected app
curl.exe https://api.ipify.org

# Normal network baseline
curl.exe --interface <normal-local-ip> https://api.ipify.org

# Tunnel adapter snapshot
Get-NetIPAddress -AddressFamily IPv4 | Sort-Object InterfaceAlias

# DNS resolution while selected app runs
Resolve-DnsName example.com
```

Use the App Tunnel diagnostics panel as the source of truth for whether App Tunnel classified a process as selected, mapped a flow, or dropped traffic because the tunnel was unavailable.

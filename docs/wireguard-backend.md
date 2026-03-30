# WireGuard Backend

## Scope

This milestone implements the first real tunnel backend in App Tunnel and keeps it explicitly separate from routing logic.

- Tunnel import, validation, secret handling, connection lifecycle, and live status sit behind `ITunnelEngine`.
- App-scoped routing remains behind `IRouterBackend` and is still dry-run only.
- No per-app routing state is pushed into the WireGuard backend yet.

## Supported input

Only WireGuard `.conf` files are supported.

The importer parses and validates:

- exactly one `[Interface]` section
- one or more `[Peer]` sections
- base64 WireGuard keys that decode to 32 bytes
- interface addresses and peer `AllowedIPs`
- `Endpoint`, `ListenPort`, `MTU`, and `PersistentKeepalive`

The importer rejects unsupported or dangerous directives such as `PreUp`, `PostUp`, `PreDown`, `PostDown`, `SaveConfig`, and `Table`. App Tunnel intentionally does not execute profile-provided scripts.

## Persistence model

App Tunnel splits persisted WireGuard data into two buckets:

- App config JSON stores non-sensitive profile metadata.
  - display name
  - imported source path
  - interface name
  - addresses
  - DNS entries
  - listen port and MTU
  - peer public keys, endpoints, allowed IPs, and keepalive settings
- DPAPI secret storage stores sensitive material.
  - interface private key
  - peer preshared keys

The profile entry in `config.json` keeps only a DPAPI secret reference ID. That keeps private key material out of the main application catalog while preserving enough metadata for the UI and service overview.

## Runtime flow

### Import

1. The UI opens a file picker limited to `.conf` files.
2. The UI sends `ImportProfile` over the named pipe.
3. `ServiceTunnelManager` routes the request to the WireGuard `ITunnelEngine`.
4. `WireGuardTunnelEngine` parses the file, validates it, stores secret material through DPAPI, and returns a `TunnelProfile` containing only metadata plus the secret reference.
5. `AppTunnelRuntime` persists the new profile to `config.json` and logs the import.

### Connect

1. The UI sends `ConnectProfile`.
2. The service looks up the persisted profile and routes the call through `ServiceTunnelManager` to `ITunnelEngine.ConnectAsync`.
3. The WireGuard engine reads protected material from DPAPI.
4. In official-runtime mode, the engine renders a runtime `.conf` file under `AppTunnel\\runtime\\wireguard` and hands it to the backend.
5. The backend starts or installs the official WireGuard tunnel service.
6. The resulting tunnel state is returned as `TunnelStatusSnapshot` and exposed in `GetOverview`.

### Disconnect

1. The UI sends `DisconnectProfile`.
2. The WireGuard backend stops and uninstalls the managed tunnel service.
3. The rendered runtime `.conf` file is deleted.
4. The service logs the lifecycle transition and updates live status.

### Status

`GetOverview` refreshes live tunnel status through `ServiceTunnelManager`, which queries each registered `ITunnelEngine`. The WPF UI uses that snapshot to render connectability, disconnectability, backend mode, last update time, and error messages.

## Backend abstraction

The backend seam under `src/AppTunnel.Vpn.WireGuard` is intentionally service-managed and ready for the official Windows runtime model.

- `IWireGuardBackend` abstracts the low-level connect, disconnect, and status operations.
- `OfficialWireGuardServiceBackend` manages the official WireGuard tunnel service on Windows.
- `MockWireGuardBackend` keeps the end-to-end control path testable when the real dependency is unavailable.
- `WireGuardBackendFactory` selects the concrete backend from service configuration.

This keeps the higher-level tunnel engine stable even if the official runtime integration changes later.

## External runtime requirements

For live tunnels, App Tunnel expects the official WireGuard for Windows distribution and its `wireguard.exe` helper.

- Default mode: `Auto`
- Explicit real-runtime mode: `OfficialService`
- Explicit test/development mode: `Mock`

Configuration lives under:

```json
{
  "AppTunnel": {
    "WireGuard": {
      "Mode": "Auto",
      "WireGuardExePath": null
    }
  }
}
```

Resolution behavior:

- If `WireGuardExePath` is set, App Tunnel uses that exact executable path.
- Otherwise App Tunnel probes the standard `Program Files\\WireGuard\\wireguard.exe` locations.
- In `Auto` mode, missing runtime dependencies fall back to `Mock`.
- In `OfficialService` mode, missing `wireguard.exe` is treated as configuration error.

The current official backend uses the service-oriented `wireguard.exe` command surface to install and uninstall a managed tunnel service from a generated runtime config.

## Security notes

- Private key and preshared key material are stored through DPAPI and never written into `config.json`.
- In official-runtime mode, decrypted config is rendered to a service-owned runtime file only for the lifetime of the managed tunnel session.
- Those runtime files contain sensitive material while active and must remain accessible only to the service account and local administrators.
- Profile-provided script hooks are deliberately rejected.

## Logging

Structured lifecycle logs are written for:

- import
- connect start and completion
- disconnect start and completion
- connect and disconnect failures

These entries are surfaced in the UI and included in exported log bundles.

## Testing strategy

Integration tests use `MockWireGuardBackend` so they do not require `wireguard.exe`, a real tunnel driver, or administrator-managed service state.

Covered scenarios:

- config parsing and validation
- control-protocol serialization
- runtime import, connect, status refresh, disconnect, and persistence behavior in mock mode

## Remaining limitations

- OpenVPN is still a placeholder.
- The official backend currently targets the helper executable and service model, not a lower-level embedded API surface.
- Named-pipe ACL hardening is still pending.
- Routing remains separate and dry-run only; no per-app packet steering happens during WireGuard connect or disconnect.
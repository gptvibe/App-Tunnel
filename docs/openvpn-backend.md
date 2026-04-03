# OpenVPN Backend

## Current implementation

The MVP OpenVPN path lives behind the existing `ITunnelEngine` contract and is implemented by `AppTunnel.Vpn.OpenVpn`.

The service import flow now accepts `.ovpn` files and routes them to `OpenVpnTunnelEngine`, which:

- validates the profile with `OpenVpnConfigParser`
- blocks script and management directives that would let a profile execute arbitrary hooks inside the service
- resolves external cert and key references during import
- accepts username and password at import time when `auth-user-pass` is used
- stores the normalized runtime config, credentials, and referenced material in DPAPI-protected secret storage
- persists only non-secret profile metadata in `config.json`

At connect time, the service stages a runtime directory under `AppTunnel/runtime/openvpn/<profile-id>` and writes:

- a normalized `profile.ovpn`
- extracted referenced material files under `materials/`
- `auth-user-pass.txt` when credentials are required

The first working backend is `ManagedProcessOpenVpnBackend`, which starts `openvpn.exe` from the Windows Service and monitors stdout/stderr for connection state:

- `Initialization Sequence Completed` => connected
- `AUTH_FAILED` and startup errors => faulted
- unexpected process exit => faulted
- service disconnect => the process tree is killed and the tunnel is treated as disconnected

Recent backend lines are also copied into the structured service log so the UI can show per-profile OpenVPN logs without a separate log transport.

## Why this was chosen

Wrapping `openvpn.exe` from the service gives the highest `.ovpn` compatibility for the first functional pass because it reuses the established OpenVPN parser and transport stack instead of reimplementing that behavior in App Tunnel.

That tradeoff is intentional for the MVP:

- it gets `.ovpn` import, validation, credentials, and lifecycle working quickly
- it keeps the routing abstraction unchanged because the rest of the app still talks to `ITunnelEngine`
- it gives us a clean seam for later replacement because the UI and service runtime do not know whether OpenVPN is process-backed or embedded

## Limitations

- `openvpn.exe` must be installed or `AppTunnel:OpenVpn:OpenVpnExePath` must point to it.
- The backend is service-owned, but not embedded. Process startup and teardown are slower than a tighter native integration.
- Disconnect currently force-stops the managed process tree instead of performing a softer management-channel shutdown.
- Import rejects script and management directives such as `up`, `down`, `route-up`, `plugin`, `management`, `tls-verify`, and related hooks.
- The parser supports the common cert/key material directives and inline blocks used in client profiles, but it is still an MVP validator rather than a full OpenVPN grammar implementation.
- Profiles that rely on advanced connection blocks or uncommon directives may still run through `openvpn.exe`, but metadata and diagnostics may be incomplete.
- Runtime material is staged on disk while a session is active. Secrets are DPAPI-protected at rest, but the live OpenVPN process still consumes plaintext runtime files while connected.

## Migration plan

The OpenVPN code is split so a future OpenVPN 3 Core path can replace the managed process backend without changing the UI, routing, or app-rule abstractions.

Planned migration shape:

1. Keep `OpenVpnTunnelEngine` as the engine-level entry point.
2. Replace or supplement `IOpenVpnBackend` with an OpenVPN 3 Core implementation.
3. Reuse the same imported profile metadata and DPAPI secret package shape where possible.
4. Keep `TunnelProfile`, `TunnelStatusSnapshot`, UI profile views, and routing plans unchanged.
5. Move backend-specific diagnostics from stdout parsing to richer embedded session/state callbacks once OpenVPN 3 Core is in place.

That means the desktop app and service orchestration should not need a redesign later; only the OpenVPN backend registration and implementation should change.

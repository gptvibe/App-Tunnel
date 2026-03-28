# App Tunnel Roadmap

## Milestone 0: Foundation scaffold

- Create the managed solution and repo structure
- Define core contracts and domain models
- Add a minimal WPF shell and Windows service handshake
- Document architecture, risks, licensing, and distribution modes

## Milestone 1: WireGuard MVP with WinDivert

- Import `.conf` WireGuard profiles
- Persist secrets via DPAPI
- Launch and supervise WireGuard tunnel sessions
- Build the WinDivert-based selective-routing MVP
- Add app assignment workflows for Win32 `.exe` applications
- Produce first structured log bundle format

## Milestone 2: Installer and portable hardening

- Install service and required drivers from an installer edition
- Add portable first-run elevation and cleanup utility
- Harden named-pipe ACLs across service and interactive user sessions
- Add diagnostics collection, bundle export, and support packaging

## Milestone 3: OpenVPN backend

- Import `.ovpn` profiles
- Support auth variants that require secret material
- Normalize lifecycle and health reporting behind `ITunnelEngine`

## Milestone 4: Production routing path

- Implement the WFP callout driver
- Implement the user-mode WFP bridge
- Replace or complement the WinDivert MVP path with production-grade policy enforcement
- Validate leak resistance, DNS behavior, reconnect semantics, and upgrade paths

## Milestone 5: Packaged Windows apps

- Add packaged app identity discovery
- Support assignment by package family name or app user model ID
- Validate process-to-package attribution in the production router path
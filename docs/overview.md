# App Tunnel Overview

App Tunnel is a Windows-only desktop product for app-scoped VPN routing. The user imports one or more VPN profiles, assigns selected applications to a profile, and the platform routes only those application flows through the tunnel while leaving all other traffic on the standard network path.

## Scope

- UI: C# .NET 8 WPF with MVVM
- Service: Windows Service hosting privileged orchestration
- IPC: Named pipes between UI and service
- Secret storage: DPAPI via the service boundary
- Routing backends:
  - MVP: WinDivert-based selective routing
  - Production: WFP callout driver plus companion bridge service
- VPN engines:
  - First: WireGuard
  - Second: OpenVPN

## Current scaffold contents

- Core domain models for applications, VPN profiles, assignments, distribution modes, router backends, and service overview state
- Stubbed `ITunnelEngine`, `IRouterBackend`, `ISecretStore`, and log bundle abstractions
- Minimal named-pipe handshake implemented between the WPF UI and the service
- WireGuard, OpenVPN, and WinDivert backend placeholders with explicit `TODO` implementation boundaries
- Native placeholders for the future WFP driver and user-mode bridge

## Repository layout

- `src/AppTunnel.UI`: WPF shell, MVVM view model, named-pipe client
- `src/AppTunnel.Service`: Windows service host, named-pipe server, DPAPI secret store
- `src/AppTunnel.Core`: shared models, contracts, and IPC message types
- `src/AppTunnel.Vpn.WireGuard`: WireGuard engine scaffold
- `src/AppTunnel.Vpn.OpenVpn`: OpenVPN engine scaffold
- `src/AppTunnel.Router.WinDivert`: WinDivert routing backend scaffold
- `native/AppTunnel.Router.WfpDriver`: planned WFP callout driver
- `native/AppTunnel.Router.WfpBridge`: planned user-mode WFP bridge service
- `tests/AppTunnel.Core.Tests`: core model and control-surface tests
- `tests/AppTunnel.IntegrationTests`: protocol and integration-oriented tests

## Non-goals of this milestone

- No real packet steering or traffic isolation yet
- No driver installation workflow yet
- No production-grade service ACL model for cross-session named-pipe access yet
- No persistent settings or profile import UX yet
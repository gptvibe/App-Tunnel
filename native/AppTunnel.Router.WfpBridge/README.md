# AppTunnel.Router.WfpBridge

Native user-mode bridge/control utility for the WFP backend.

## Files

- `AppTunnel.Router.WfpBridge.vcxproj`: Win32 console project
- `src/main.cpp`: bridge CLI for install, uninstall, filter enable/disable, and diagnostics
- `../Common/AppTunnelWfpShared.h`: shared driver protocol and WFP GUIDs

## Responsibilities

- Install or remove the kernel driver service
- Add or remove the App Tunnel WFP provider, sublayer, callouts, and filters
- Forward rule sync, tunnel-state, and diagnostics requests to the driver device
- Keep the Windows service decoupled from SCM and raw WFP object plumbing

## Commands

- `install <driver-sys-path>`
- `uninstall`
- `enable-filters`
- `disable-filters`
- `set-tunnel-state <connected|disconnected>`
- `add-rule <rule-id> <profile-id> <win32|packaged> <flags> <display-name> <exe-path|-> <package-family|-> <package-identity|->`
- `remove-rule <rule-id>`
- `diagnostics`

# Installer Edition

## Responsibilities

- Install the WPF UI
- Register and configure the Windows service
- Install required routing drivers or user-mode helpers
- Set service recovery options, upgrade behavior, and uninstall behavior
- Register shell integration such as Start Menu entries and optional file associations

## Packaging

- WiX installer project: `packaging/AppTunnel.Installer/`
- Release build script: `build/Build-Release.ps1`
- Cleanup helper: `build/Cleanup-AppTunnel.ps1`
- Runtime layout:
  - `runtime\AppTunnel.UI.exe`
  - `runtime\AppTunnel.Service.exe`
  - additional published dependencies
  - `runtime\router\` when native bridge/driver assets are staged

## Behavior

- Install:
  - copies the self-contained runtime
  - registers `AppTunnelService`
  - starts the service on install
  - stages the WFP bridge, driver placeholder/signing notes, and cleanup script with the runtime
- Upgrade:
  - uses WiX major-upgrade handling for `0.0.1`
  - preserves external data stored under ProgramData
- Uninstall:
  - stops and removes the Windows service
  - removes installed files
- Cleanup:
  - `tools\Cleanup-AppTunnel.ps1` removes the Windows service, attempts bridge-driven WFP uninstall, removes the driver service if present, and optionally clears ProgramData state

## Signing Note

The installer can package the managed runtime and the native backend staging area, but a production installer must only include release-signed WFP driver binaries. Unsigned or test-signed drivers are for local validation only.

# Portable ZIP Edition

## Definition

Portable means the app can live in a self-contained folder and does not require a traditional installer package for day-to-day execution. It does not mean no elevation, no service, or no driver.

## Expected behavior

- User extracts a ZIP to a chosen folder
- First run elevates to install or register the Windows service and any required routing components
- App stores its portable metadata relative to the portable root where practical
- Secrets remain in DPAPI-protected storage owned by the service
- Cleanup utility removes the service, drivers, and temporary state when the user wants to uninstall

## Implemented layout

- `AppTunnelPortable.exe`
- `AppTunnelPortableCleanup.exe`
- `runtime\`
- `data\`
- `logs\`

The two root utilities are published as self-contained single-file executables so the portable root stays runnable after a simple ZIP extract.

## Behavior

- `AppTunnelPortable.exe`
  - detects the portable root from its own folder
  - creates `runtime\`, `data\`, and `logs\` if needed
  - registers and starts `AppTunnelPortableService` on first run
  - passes `--root "<portable>" --portable` to the service so config and secrets live under `data\` while logs stay under `logs\`
- `AppTunnelPortableCleanup.exe`
  - requires admin rights
  - unregisters the portable service
  - removes WFP state and log files

## Admin Requirement

Portable mode still requires administrator rights for networking components. The launcher may elevate on first run, and cleanup must elevate before unregistering the service or driver.

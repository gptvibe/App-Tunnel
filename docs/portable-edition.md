# Portable ZIP Edition

## Definition

Portable means the app can live in a self-contained folder and does not require a traditional installer package for day-to-day execution. It does not mean no elevation, no service, or no driver.

## Expected behavior

- User extracts a ZIP to a chosen folder
- First run elevates to install or register the Windows service and any required routing components
- App stores its portable metadata relative to the portable root where practical
- Secrets remain in DPAPI-protected storage owned by the service
- Cleanup utility removes the service, drivers, and temporary state when the user wants to uninstall

## Design implications

- Portable root detection must be explicit
- The UI must surface elevation requirements and cleanup status clearly
- Upgrades need an in-place folder replacement workflow that preserves portable metadata where safe
- Logs and diagnostics should be exportable without depending on installer-owned locations

## Current gap

The scaffold documents the portable mode but does not implement portable bootstrap, elevation workflow, or cleanup tooling yet.
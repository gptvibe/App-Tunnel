# Installer Edition

## Responsibilities

- Install the WPF UI
- Register and configure the Windows service
- Install required routing drivers or user-mode helpers
- Set service recovery options, upgrade behavior, and uninstall behavior
- Register shell integration such as Start Menu entries and optional file associations

## Expected packaging work

- Bundle the managed binaries
- Stage any third-party VPN or routing binaries
- Register native components for the future WFP path
- Provide upgrade-safe migrations for settings and secrets

## Current gap

The scaffold does not include WiX, MSIX, MSI, or any other installer project yet. Packaging technology selection remains open.
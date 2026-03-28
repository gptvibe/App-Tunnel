# Licensing Notes

This repository scaffold does not ship third-party binaries yet. Before the first distributable build, the team must complete a formal licensing review for every redistributed component.

## Items requiring explicit review

- WinDivert binaries, headers, and redistribution terms
- WireGuard user-mode tooling or drivers used by the WireGuard backend
- OpenVPN binaries and any dependent TAP or Wintun components
- Driver signing certificates for the future WFP callout driver
- Installer technology, bootstrapper prerequisites, and bundled redistributables

## Expected policy

- Keep App Tunnel source licensing separate from third-party binary licenses
- Track the exact source URL, version, checksum, and license text for each bundled dependency
- Store third-party notices in a dedicated notices file before release packaging begins
- Do not assume the portable ZIP edition has different redistribution obligations from the installer edition

## Current gap

Licensing is documented only at the planning level in this scaffold. No compliance manifest, notice bundle, or release-review checklist exists yet.
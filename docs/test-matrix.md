# Test Matrix

## Smoke Matrix

| Scenario | Coverage | Expected Result |
| --- | --- | --- |
| Installer install/uninstall | `tests/AppTunnel.IntegrationTests/ReleaseSmokeTests.cs` plus VM validation | MSI includes service install/control metadata and cleanup script; install starts service; uninstall removes service |
| Portable first run / cleanup | `tests/AppTunnel.IntegrationTests/ReleaseSmokeTests.cs` plus launcher/cleanup VM validation | Launcher creates `runtime/`, `data/`, `logs/`; first run can elevate to register service; cleanup unregisters service and removes WFP state |
| Selected app over VPN | `tests/AppTunnel.IntegrationTests/ReleaseSmokeTests.cs` | WFP backend enables filters and registers selected rule when a tunnel is connected |
| Unselected app off VPN | `tests/AppTunnel.IntegrationTests/ReleaseSmokeTests.cs` | WFP backend stays idle with no selected rules and does not enable filters |
| Tunnel drop leak prevention | `tests/AppTunnel.IntegrationTests/ReleaseSmokeTests.cs` | Kill-on-drop rules surface leak-prevention state until a tunnel reconnects |

## Manual Validation Matrix

| Area | Environment | Notes |
| --- | --- | --- |
| Installer upgrade | Windows 10/11 VM, admin | Install `0.0.1`, reinstall over same build, confirm service survives upgrade and data root remains in ProgramData |
| Portable in-place upgrade | Windows 10/11 VM, admin | Replace `runtime/` while preserving `data/`, rerun launcher, confirm service bin path still points at current folder |
| WFP native backend | Test-signed lab machine only | Build native bridge/driver with WDK, enable test signing, confirm bridge install/enable/diagnostics commands |
| Release-signed driver package | Signing pipeline | Verify EV signing plus Microsoft attestation or HLK before any public release |
| Tunnel reconnect leak prevention | Disposable VPN testbed | Disconnect tunnel with kill-on-drop rules active and confirm selected apps do not leak traffic |

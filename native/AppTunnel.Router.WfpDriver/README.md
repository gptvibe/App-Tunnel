# AppTunnel.Router.WfpDriver

Kernel-mode WFP callout driver project.

## Files

- `AppTunnel.Router.WfpDriver.vcxproj`: WDK project file
- `AppTunnel.Router.WfpDriver.inf`: driver package manifest
- `src/Driver.c`: callout registration, IOCTL handling, and baseline classify logic
- `src/Driver.h`: driver globals and callback declarations
- `../Common/AppTunnelWfpShared.h`: shared GUIDs, IOCTLs, and rule/diagnostic structs

## Notes

- Win32 executable matching is implemented in the design now.
- Packaged-app matching fields are already present in the shared rule contract for a future revision.
- Runtime state is supplied from user mode so the driver can keep kill-on-drop policy armed even when the tunnel is down.
- Release distribution still requires a release-signed driver package. Test-signed binaries are for lab validation only.

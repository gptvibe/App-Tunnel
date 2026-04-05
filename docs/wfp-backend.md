# WFP Backend

## Scope

`AppTunnel.Router.Wfp` is the production-oriented routing path for App Tunnel. It consists of:

- `native/AppTunnel.Router.WfpDriver/`: kernel-mode WFP callout driver project (WDK)
- `native/AppTunnel.Router.WfpBridge/`: user-mode bridge/control executable
- `src/AppTunnel.Router.Wfp/`: managed service-side controller and router backend

The managed service owns policy and release orchestration. The native bridge owns SCM/WFP object changes. The driver owns flow classification and leak blocking.

## Layers and Callouts

The driver and bridge are structured around these layers:

- `FWPM_LAYER_ALE_AUTH_CONNECT_V4/V6`
  Used for outbound app matching and block-on-drop enforcement before connect authorization completes.
- `FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4/V6`
  Used for inbound authorization so selected apps do not receive traffic outside the intended tunnel path.
- `FWPM_LAYER_ALE_FLOW_ESTABLISHED_V4/V6`
  Used to track per-flow state after authorization and expose diagnostics back to the service.
- `FWPM_LAYER_ALE_CONNECT_REDIRECT_V4/V6`
  Reserved for redirect/steering metadata once the active tunnel interface and provider context are bound.

Callouts are registered in kernel mode and filters are added from the bridge into the App Tunnel provider/sublayer. Filters are broad by layer; app matching happens in the callout using Win32 path matching today and packaged-app identifiers in the rule structure for a future revision.

## Flow Tracking

Flow tracking is split in two stages:

- Authorization stage:
  Match app identity, decide whether the flow is selected, and block immediately if the rule requires kill-on-drop and no healthy tunnel is present.
- Established-flow stage:
  Associate the flow with the rule/profile pair and emit diagnostics counters for later support bundles.

The shared native structs intentionally carry both Win32 and packaged-app identity fields:

- Win32 today: `ExecutablePath`
- Future packaged-app matching: `PackageFamilyName` and `PackageIdentity`

That keeps the control protocol stable when packaged-app enforcement is implemented.

## Redirect and Block Strategy

The redirect/block model is:

1. Match selected traffic at ALE authorization layers.
2. If the rule is marked kill-on-drop and the tunnel is unavailable, block at authorization time.
3. If the tunnel is healthy, keep the flow in the callout path and apply redirect metadata at `ALE_CONNECT_REDIRECT_*`.
4. Maintain separate inbound authorization coverage so replies for selected apps are not accepted on the wrong path.

The current scaffold keeps the redirect contract stable and records diagnostics, but production steering still depends on a properly built and signed native driver package being staged with the bridge.

## Leak Prevention Strategy

Leak prevention is layered:

- Block-on-drop flag per app rule
- Inbound and outbound ALE authorization coverage
- Flow tracking to avoid stale state across tunnel changes
- Service-side disable path that removes filters when the backend is intentionally stopped
- Packaging rules that keep portable/admin cleanup explicit so driver state is not orphaned

Operationally:

- Installer edition should remove the service, filters, and driver package on uninstall/cleanup.
- Portable edition must call the cleanup utility to unregister the service and driver before deleting files.

## Service Control APIs

The service-side control surface added in `IAppTunnelControlService` exposes:

- `InstallWfpBackendAsync`
- `UninstallWfpBackendAsync`
- `SetWfpFiltersEnabledAsync`
- internal router-to-backend tunnel-state synchronization via `IWfpBackendControl.SetTunnelStateAsync`
- `AddWfpAppRuleAsync`
- `RemoveWfpAppRuleAsync`
- `GetWfpDiagnosticsAsync`

Those methods are mirrored on the named-pipe IPC contract so the UI or future admin tooling can drive diagnostics and recovery without talking directly to the bridge or driver.

The native bridge command surface is:

- `install <driver-sys-path>`
- `uninstall`
- `enable-filters`
- `disable-filters`
- `set-tunnel-state <connected|disconnected>`
- `add-rule <rule-id> <profile-id> <win32|packaged> <flags> <display-name> <exe-path|-> <package-family|-> <package-identity|->`
- `remove-rule <rule-id>`
- `diagnostics`

## Dev Signing vs Release Signing

Unsigned drivers are not shippable.

Use these paths deliberately:

- Dev/test signing:
  Build the WDK driver locally, sign it with a local test certificate, enable Windows test-signing on the test machine, and install only on isolated validation hosts.
- Release signing:
  Use an EV code-signing certificate for the submission path, produce a production catalog, and complete Microsoft attestation signing or the full HLK workflow required for the target distribution channel.

Release packaging must stage only the release-signed driver package. Test-signed or unsigned binaries can be useful for local bring-up but must never be represented as production-ready deliverables.

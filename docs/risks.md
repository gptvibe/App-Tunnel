# Key Risks

## Network isolation risk

Per-app VPN routing on Windows is the hardest part of the product. Process attribution, socket ownership changes, DNS routing, reconnect behavior, and leak prevention all require careful validation.

## Driver complexity risk

The production path depends on a WFP callout driver and a signed deployment story. This is materially harder than the WinDivert MVP and must be treated as a separate engineering track.

## Service/UI boundary risk

Running the control plane as a Windows service means the IPC boundary must handle session isolation, ACLs, versioning, and service restarts. The current scaffold proves only the contract shape, not the hardened deployment model.

## VPN backend risk

WireGuard and OpenVPN differ in profile semantics, adapter behavior, credential handling, and health reporting. The `ITunnelEngine` abstraction reduces UI coupling but does not remove backend-specific edge cases.

## Portable edition risk

Portable mode still needs elevation, service installation, possible driver installation, and reliable cleanup. It is operationally different from a classic portable app and must be communicated clearly to users.

## Logging and supportability risk

Supportability depends on consistent structured logs, environment snapshots, and safe redaction. The scaffold defines the shape, but export bundles and redaction policy are not implemented.
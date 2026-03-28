# AppTunnel.Router.WfpBridge

Planned user-mode bridge for the production WFP routing path.

## Intent

- Translate service policy into driver-consumable updates
- Manage rule lifecycle, health reporting, and diagnostics
- Decouple the Windows service from direct driver protocol details

## TODO

- Define the bridge-to-driver protocol
- Decide whether the bridge runs inside the main service or as a companion process
- Implement lifecycle management, backoff, and version negotiation
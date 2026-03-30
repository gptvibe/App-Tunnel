# Rule Engine

## Scope

The rule engine owns application identity, tunnel assignment, and future launch and enforcement intent. It does not own tunnel lifecycle and it does not currently implement packet steering. Tunnel connectivity remains under `ITunnelEngine`, while selective routing remains under `IRouterBackend`.

In the current milestone the rule engine provides:

- persisted app rules
- Win32 executable discovery and normalization
- future-ready packaged-app identity fields
- rule settings for launch and drop behavior
- UI flows for add and assignment

## Win32 path-based rules

Win32 rules are keyed by a normalized absolute `.exe` path.

Current behavior:

1. The UI opens an add-app dialog limited to `.exe` files.
2. The service validates the file extension and normalizes the path with `Path.GetFullPath`.
3. The rule is stored as `AppKind.Win32Exe` with:
   - `DisplayName`
   - normalized `ExecutablePath`
   - optional assigned tunnel profile
   - rule flags

The rule engine rejects duplicate Win32 rules that point at the same normalized executable path.

This path-based approach is the correct first step for classic desktop apps because:

- it is stable enough for user-selected Win32 executables
- it is easy to inspect and explain in the UI
- it does not require package identity or manifest parsing

## Packaged-app identity-based rules

Packaged-app support is modelled now but not yet surfaced in the UI for creation.

Each rule already reserves these fields:

- `PackageFamilyName`
- `PackageIdentity`
- `DisplayName`

These fields are intended for later enumeration of Store, MSIX, and other packaged apps where an executable path alone is either unstable or insufficient.

Planned direction:

- `PackageFamilyName` identifies the package family
- `PackageIdentity` stores the later-selected app container or package application identity
- `DisplayName` remains the user-facing name shown in the UI

The service abstraction for this future work is `IApplicationDiscoveryService`, which already exposes:

- inspection of Win32 apps
- packaged-app enumeration for a later implementation

## Rule settings

Each rule stores these settings:

- `IsEnabled`
- `ProfileId`
- `LaunchOnConnect`
- `KillAppTrafficOnTunnelDrop`
- `IncludeChildProcesses`

Current meaning:

- `IsEnabled`: the rule is configured but inactive when false
- `ProfileId`: the assigned tunnel profile, or null when unassigned
- `LaunchOnConnect`: persist user intent to launch the app once the tunnel is connected
- `KillAppTrafficOnTunnelDrop`: persist user intent to stop or block selected app traffic when the tunnel drops
- `IncludeChildProcesses`: persist intent to expand the rule to child processes when that is safe and detectable

The current milestone persists and displays these settings but does not yet execute launch or drop-enforcement actions.

## Status model

The current UI derives per-app status badges from the stored rule plus live tunnel state:

- `Disabled`: the rule exists but is disabled
- `Unassigned`: no tunnel profile is assigned
- `Missing Profile`: the assigned profile no longer exists
- `Waiting`: the assigned tunnel is not connected
- `Transitioning`: the assigned tunnel is connecting or disconnecting
- `Protected`: the assigned tunnel is connected and the rule is ready for future routing enforcement
- `Attention`: the assigned tunnel is faulted

These badges are intentionally conservative. They represent rule readiness and tunnel health, not proof of active per-app packet steering.

## Child-process caveats

`IncludeChildProcesses` is explicitly best effort.

Reasons:

- many apps launch helpers outside the original executable directory
- some helpers are shared by multiple products or browsers
- process trees can break across brokered launches, updaters, COM activation, scheduled tasks, or service-hosted helpers
- Windows packaged apps and Win32 helpers can mix identities in ways that are not visible from a single parent executable path

Because of this, child-process inclusion should be treated as an operator preference, not a guarantee.

The desired future behavior is:

- include direct children when there is strong evidence they belong to the selected app session
- avoid broad path-based expansion that would capture unrelated processes
- surface ambiguity in logs and diagnostics rather than silently over-matching

## Helper processes outside the original exe path

The desired policy for helpers discovered outside the original executable path is deliberately cautious.

Default design intent:

1. The original selected executable remains the root identity for the rule.
2. Helpers outside that path should not be auto-included solely because they were launched by the root process once.
3. If later discovery identifies a stable helper relationship, the engine should prefer an explicit additional rule or a verified helper mapping over silent expansion.
4. Any future auto-expansion should be auditable through structured logs.

This avoids a common failure mode where an app launches shared browsers, crash handlers, update agents, or broker processes that should not inherit the same routing policy.

## Persistence

Rules are stored in `config.json` alongside tunnel profiles.

Win32 rules store normalized executable paths. Packaged-app rules will eventually store identity fields instead of depending on file-system paths.

This keeps application targeting separate from tunnel secrets and separate from later routing implementation details.

## Near-term gaps

- packaged-app enumeration is not implemented yet
- launch-on-connect is stored but not executed yet
- tunnel-drop enforcement is stored but not executed yet
- child-process expansion is not implemented yet
- routing backends still do not enforce app-scoped traffic behavior
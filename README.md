# App Tunnel

Windows-only app-scoped VPN routing scaffold targeting C# .NET 8 WPF plus a Windows service control plane.

## Included in this scaffold

- Architecture and distribution docs in `docs/`
- Managed solution and project structure in `src/` and `tests/`
- Shared core domain models and backend abstractions
- Named-pipe handshake between the WPF UI and Windows service
- Native placeholders for the future WFP production router

## Build requirements

- Windows 10 or Windows 11
- .NET SDK with .NET 8 targeting support
- Visual Studio 2022 or later for WPF and service debugging
- Administrator rights later for service and driver installation scenarios

## Build

```powershell
dotnet restore .\AppTunnel.sln
dotnet build .\AppTunnel.sln
```

Run the unit tests after the build:

```powershell
dotnet test .\AppTunnel.sln --no-build
```

## Run Locally

Run the Windows Service host first in a terminal:

```powershell
dotnet run --project .\src\AppTunnel.Service\AppTunnel.Service.csproj
```

Then start the WPF UI in a second terminal:

```powershell
dotnet run --project .\src\AppTunnel.UI\AppTunnel.UI.csproj
```

The UI should show the service connection state in the dashboard shell. The service should stay running with its named-pipe IPC host active. If the service is not running yet, the UI will show a disconnected state until the pipe becomes available.

For the current scaffold, the service uses dry-run tunnel and router managers, DPAPI-backed secret storage, configuration persistence, and structured log export wiring without touching live VPN or routing state.

## Documentation

- `docs/overview.md`
- `docs/architecture.md`
- `docs/roadmap.md`
- `docs/licensing.md`
- `docs/risks.md`
- `docs/portable-edition.md`
- `docs/installer-edition.md`

## Known gaps

- No real WireGuard or OpenVPN session management yet
- No WinDivert interception or WFP driver implementation yet
- No hardened named-pipe ACL model for service-to-user communication yet
- No installer or portable cleanup utility yet

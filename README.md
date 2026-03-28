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

If the machine has .NET 8 targeting packs but only newer shared runtimes installed, test execution can use runtime roll-forward:

```powershell
$env:DOTNET_ROLL_FORWARD='LatestMajor'
$env:DOTNET_ROLL_FORWARD_TO_PRERELEASE='1'
dotnet test .\AppTunnel.sln --no-build
```

## Run the scaffold in development

Run the service first in a terminal:

```powershell
dotnet run --project .\src\AppTunnel.Service\AppTunnel.Service.csproj
```

Run the UI second:

```powershell
dotnet run --project .\src\AppTunnel.UI\AppTunnel.UI.csproj
```

The current UI performs a named-pipe handshake and renders service capability data. It does not yet import profiles, launch VPN engines, or apply packet-routing policy.

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
- No persistent storage for apps, profiles, or assignments yet
- No hardened named-pipe ACL model for service-to-user communication yet
- No installer or portable cleanup utility yet
- No structured log export bundle implementation yet


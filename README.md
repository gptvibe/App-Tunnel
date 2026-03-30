# App Tunnel

Windows-only app-scoped VPN routing scaffold targeting C# .NET 8 WPF plus a Windows service control plane.

## Included in this scaffold

- Architecture and distribution docs in `docs/`
- Managed solution and project structure in `src/` and `tests/`
- Shared core domain models and backend abstractions
- Named-pipe handshake between the WPF UI and Windows service
- WireGuard .conf import, validation, DPAPI-backed secret storage, and service-managed tunnel lifecycle
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

By default, the service runs the WireGuard backend in `Auto` mode. If the official WireGuard for Windows runtime is installed, App Tunnel will stage a service-owned runtime config and manage the official tunnel service for connect and disconnect operations. If `wireguard.exe` is not available, App Tunnel falls back to a mock backend so the UI and integration tests still exercise the full control path.

To force mock mode for local development or tests, set the following in `src/AppTunnel.Service/appsettings.json` or `appsettings.Development.json`:

```json
{
	"AppTunnel": {
		"WireGuard": {
			"Mode": "Mock"
		}
	}
}
```

## Documentation

- `docs/overview.md`
- `docs/architecture.md`
- `docs/wireguard-backend.md`
- `docs/rule-engine.md`
- `docs/roadmap.md`
- `docs/licensing.md`
- `docs/risks.md`
- `docs/portable-edition.md`
- `docs/installer-edition.md`

## Known gaps

- OpenVPN remains unimplemented
- Live WireGuard sessions require the official WireGuard for Windows runtime; otherwise the mock backend is used
- No WinDivert interception or WFP driver implementation yet
- No hardened named-pipe ACL model for service-to-user communication yet
- No installer or portable cleanup utility yet

# Release Checklist

## Version 0.0.1

- Confirm `Directory.Build.props` version fields are `0.0.1`
- Build the managed solution: `dotnet build .\AppTunnel.sln`
- Run tests: `dotnet test .\AppTunnel.sln`
- Build release artifacts: `powershell -ExecutionPolicy Bypass -File .\build\Build-Release.ps1`
- Verify portable ZIP contains:
  - `AppTunnelPortable.exe`
  - `AppTunnelPortableCleanup.exe`
  - `runtime\`
  - `data\`
  - `logs\`
- Verify installer output contains:
  - MSI package
  - `runtime\` staged binaries
  - `tools\Cleanup-AppTunnel.ps1`
- Verify `docs\wfp-backend.md`, `docs\installer-edition.md`, and `docs\portable-edition.md` are staged with the release notes
- Confirm whether native WFP bridge/driver binaries were actually produced
- If native driver binaries are present:
  - Verify they are test-signed only for lab validation, or
  - Verify the production release package uses release-signed binaries only
- Do not ship unsigned or test-signed driver binaries as production deliverables
- Validate uninstall and portable cleanup on a disposable Windows VM with admin rights
- Export logs and diagnostics after at least one end-to-end smoke pass

## Sign-Off Gates

- Packaging complete
- Smoke tests pass
- Docs updated
- Signing requirements explicitly recorded
- Artifact paths captured for handoff

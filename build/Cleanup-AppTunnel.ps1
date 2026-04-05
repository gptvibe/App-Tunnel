param(
    [switch]$RemoveData,
    [string]$InstallRoot = "$env:ProgramFiles\App Tunnel"
)

$serviceName = "AppTunnelService"
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
}

$driverServiceName = "AppTunnelWfp"
if (Get-Service -Name $driverServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $driverServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $driverServiceName | Out-Null
}

$bridgePath = Join-Path $InstallRoot "runtime\AppTunnel.Router.WfpBridge.exe"
if (Test-Path $bridgePath) {
    & $bridgePath uninstall | Out-Null
}

$programDataRoot = Join-Path $env:ProgramData "AppTunnel"
if ($RemoveData -and (Test-Path $programDataRoot)) {
    Remove-Item -LiteralPath $programDataRoot -Recurse -Force
}

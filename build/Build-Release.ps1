param(
    [string]$Version = "0.0.1",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repoRoot "artifacts"
$publishRoot = Join-Path $artifactsRoot "publish"
$stageRoot = Join-Path $artifactsRoot "stage"
$releaseRoot = Join-Path $artifactsRoot "release\$Version"
$installerStage = Join-Path $stageRoot "installer"
$portableStage = Join-Path $stageRoot "portable"
$installerOutput = Join-Path $releaseRoot "installer"
$portableOutput = Join-Path $releaseRoot "portable"
$wixProject = Join-Path $repoRoot "packaging\AppTunnel.Installer\AppTunnel.Installer.wixproj"

foreach ($path in @($publishRoot, $stageRoot, $releaseRoot)) {
    if (Test-Path $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

function Publish-App {
    param(
        [string]$ProjectPath,
        [string]$OutputPath,
        [switch]$SingleFile
    )

    dotnet publish $ProjectPath `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        -p:PublishSingleFile=$($SingleFile.IsPresent.ToString().ToLowerInvariant()) `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $OutputPath

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ProjectPath"
    }
}

function Copy-PublishTree {
    param(
        [string]$SourceRoot,
        [string]$DestinationRoot,
        [string[]]$AlwaysOverwriteNames = @()
    )

    Get-ChildItem -LiteralPath $SourceRoot -Recurse -File | ForEach-Object {
        $relativePath = $_.FullName.Substring($SourceRoot.Length).TrimStart('\')
        $destinationPath = Join-Path $DestinationRoot $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath

        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null

        if ((Test-Path $destinationPath) -and ($AlwaysOverwriteNames -notcontains $_.Name)) {
            return
        }

        Copy-Item -LiteralPath $_.FullName -Destination $destinationPath -Force
    }
}

function Try-BuildNativeProject {
    param(
        [string]$ProjectPath
    )

    $msbuild = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if (-not $msbuild) {
        Write-Warning "MSBuild.exe was not available. Skipping native build for $ProjectPath."
        return
    }

    try {
        & $msbuild.Source $ProjectPath /m /p:Configuration=Release /p:Platform=x64 | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "MSBuild returned exit code $LASTEXITCODE."
        }
    }
    catch {
        Write-Warning "Native build failed for $ProjectPath. Packaging will continue with source scaffolding only."
    }
}

function Copy-RouterAssets {
    param(
        [string]$DestinationRoot,
        [switch]$FlattenIntoRoot
    )

    $routerDestination = if ($FlattenIntoRoot) {
        $DestinationRoot
    }
    else {
        Join-Path $DestinationRoot "router"
    }

    New-Item -ItemType Directory -Force -Path $routerDestination | Out-Null

    $bridgeBinary = Join-Path $repoRoot "native\bin\AppTunnel.Router.WfpBridge\x64\Release\AppTunnel.Router.WfpBridge.exe"
    $driverBinary = Join-Path $repoRoot "native\bin\AppTunnel.Router.WfpDriver\x64\Release\AppTunnel.Router.WfpDriver.sys"
    $wfpDoc = Join-Path $repoRoot "docs\wfp-backend.md"

    if (Test-Path $bridgeBinary) {
        Copy-Item $bridgeBinary (Join-Path $routerDestination "AppTunnel.Router.WfpBridge.exe")
    }

    if (Test-Path $driverBinary) {
        Copy-Item $driverBinary (Join-Path $routerDestination "AppTunnel.Router.WfpDriver.sys")
    }

    Copy-Item $wfpDoc (Join-Path $routerDestination "wfp-backend.md")

    if (-not (Test-Path (Join-Path $routerDestination "AppTunnel.Router.WfpDriver.sys"))) {
        Set-Content -LiteralPath (Join-Path $routerDestination "NATIVE-ASSETS-REQUIRED.txt") -Value @(
            "No signed WFP driver binary was available during packaging.",
            "Portable and installer artifacts include the managed backend, packaging, and native source scaffolding only.",
            "Stage a test-signed binary for lab validation and a release-signed package for production shipping."
        )
    }
}

function New-WixFragment {
    param(
        [string]$StageRuntimeRoot,
        [string]$OutputPath
    )

    $excluded = @(
        "AppTunnel.UI.exe",
        "AppTunnel.Service.exe"
    )

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$builder.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    [void]$builder.AppendLine('  <Fragment>')
    [void]$builder.AppendLine('    <DirectoryRef Id="RuntimeFolder">')
    $componentRefs = New-Object System.Collections.Generic.List[string]
    $componentIndex = [ref]0

    function Get-DirectoryId {
        param([string]$RelativePath)

        if ([string]::IsNullOrWhiteSpace($RelativePath)) {
            return "RuntimeFolder"
        }

        $sanitized = ($RelativePath -replace '[^A-Za-z0-9_]', '_')
        return "RuntimeFolder_$sanitized"
    }

    function Add-DirectoryContents {
        param(
            [string]$CurrentPath,
            [string]$RelativePath,
            [int]$IndentLevel
        )

        $indent = ' ' * $IndentLevel

        Get-ChildItem -LiteralPath $CurrentPath -File | ForEach-Object {
            if ($excluded -contains $_.Name) {
                return
            }

            $fileId = "GeneratedFile$($componentIndex.Value)"
            $componentId = "GeneratedComponent$($componentIndex.Value)"
            [void]$builder.AppendLine("$indent<Component Id=""$componentId"" Guid=""*"" Bitness=""always64"">")
            [void]$builder.AppendLine("$indent  <File Id=""$fileId"" Source=""$($_.FullName)"" KeyPath=""yes"" />")
            [void]$builder.AppendLine("$indent</Component>")
            $componentRefs.Add($componentId)
            $componentIndex.Value++
        }

        Get-ChildItem -LiteralPath $CurrentPath -Directory | Sort-Object Name | ForEach-Object {
            $childRelativePath = if ([string]::IsNullOrWhiteSpace($RelativePath)) {
                $_.Name
            }
            else {
                Join-Path $RelativePath $_.Name
            }

            $directoryId = Get-DirectoryId $childRelativePath
            [void]$builder.AppendLine("$indent<Directory Id=""$directoryId"" Name=""$($_.Name)"">")
            Add-DirectoryContents -CurrentPath $_.FullName -RelativePath $childRelativePath -IndentLevel ($IndentLevel + 2)
            [void]$builder.AppendLine("$indent</Directory>")
        }
    }

    Add-DirectoryContents -CurrentPath $StageRuntimeRoot -RelativePath '' -IndentLevel 6

    [void]$builder.AppendLine('    </DirectoryRef>')
    [void]$builder.AppendLine('  </Fragment>')
    [void]$builder.AppendLine('  <Fragment>')
    [void]$builder.AppendLine('    <ComponentGroup Id="GeneratedRuntimeFiles">')
    foreach ($componentId in $componentRefs) {
        [void]$builder.AppendLine("      <ComponentRef Id=""$componentId"" />")
    }
    [void]$builder.AppendLine('    </ComponentGroup>')
    [void]$builder.AppendLine('  </Fragment>')
    [void]$builder.AppendLine('</Wix>')

    Set-Content -LiteralPath $OutputPath -Value $builder.ToString()
}

$uiPublish = Join-Path $publishRoot "ui"
$servicePublish = Join-Path $publishRoot "service"
$portableLauncherPublish = Join-Path $publishRoot "portable-launcher"
$portableCleanupPublish = Join-Path $publishRoot "portable-cleanup"

Publish-App (Join-Path $repoRoot "src\AppTunnel.UI\AppTunnel.UI.csproj") $uiPublish
Publish-App (Join-Path $repoRoot "src\AppTunnel.Service\AppTunnel.Service.csproj") $servicePublish
Publish-App (Join-Path $repoRoot "src\AppTunnel.PortableLauncher\AppTunnel.PortableLauncher.csproj") $portableLauncherPublish -SingleFile
Publish-App (Join-Path $repoRoot "src\AppTunnel.PortableCleanup\AppTunnel.PortableCleanup.csproj") $portableCleanupPublish -SingleFile
Try-BuildNativeProject (Join-Path $repoRoot "native\AppTunnel.Router.WfpBridge\AppTunnel.Router.WfpBridge.vcxproj")
Try-BuildNativeProject (Join-Path $repoRoot "native\AppTunnel.Router.WfpDriver\AppTunnel.Router.WfpDriver.vcxproj")

New-Item -ItemType Directory -Force -Path $installerStage, $portableStage, $installerOutput, $portableOutput | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $installerStage "runtime"), (Join-Path $installerStage "tools") | Out-Null

$serviceAlwaysOverwrite = @(
    "AppTunnel.Service.exe",
    "AppTunnel.Service.dll",
    "AppTunnel.Service.pdb",
    "AppTunnel.Service.deps.json",
    "AppTunnel.Service.runtimeconfig.json"
)

Copy-PublishTree $uiPublish (Join-Path $installerStage "runtime")
Copy-PublishTree $servicePublish (Join-Path $installerStage "runtime") $serviceAlwaysOverwrite
Copy-Item (Join-Path $repoRoot "build\Cleanup-AppTunnel.ps1") (Join-Path $installerStage "tools\Cleanup-AppTunnel.ps1")
Copy-RouterAssets (Join-Path $installerStage "runtime") -FlattenIntoRoot

$portableRoot = Join-Path $portableStage "App Tunnel Portable"
New-Item -ItemType Directory -Force -Path $portableRoot | Out-Null
$portableLayoutDirs = @(
    (Join-Path $portableRoot "runtime"),
    (Join-Path $portableRoot "data"),
    (Join-Path $portableRoot "logs")
)
foreach ($dir in $portableLayoutDirs) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

Copy-PublishTree $uiPublish (Join-Path $portableRoot "runtime")
Copy-PublishTree $servicePublish (Join-Path $portableRoot "runtime") $serviceAlwaysOverwrite
Copy-Item (Join-Path $portableLauncherPublish "AppTunnelPortable.exe") (Join-Path $portableRoot "AppTunnelPortable.exe")
Copy-Item (Join-Path $portableCleanupPublish "AppTunnelPortableCleanup.exe") (Join-Path $portableRoot "AppTunnelPortableCleanup.exe")
Copy-RouterAssets (Join-Path $portableRoot "runtime")
Set-Content -LiteralPath (Join-Path $portableRoot "data\.keep") -Value "Portable data lives here."
Set-Content -LiteralPath (Join-Path $portableRoot "logs\.keep") -Value "Portable logs live here."
Copy-Item (Join-Path $repoRoot "docs\portable-edition.md") (Join-Path $portableRoot "portable-edition.md")
Copy-Item (Join-Path $repoRoot "docs\wfp-backend.md") (Join-Path $portableRoot "wfp-backend.md")

$generatedWxs = Join-Path $repoRoot "packaging\AppTunnel.Installer\GeneratedFiles.wxs"
New-WixFragment (Join-Path $installerStage "runtime") $generatedWxs

dotnet build $wixProject -c $Configuration -p:OutputPath="$installerOutput\"
if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed."
}

Compress-Archive -Path (Join-Path $portableRoot "*") -DestinationPath (Join-Path $portableOutput "AppTunnel-Portable-$Version.zip")

Write-Host "Installer output: $installerOutput"
Write-Host "Portable output: $portableOutput"

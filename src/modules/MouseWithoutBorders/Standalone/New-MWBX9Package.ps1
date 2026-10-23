#Requires -Version 5.1
<#
.SYNOPSIS
Collects a standalone MWB-X9 build into MWB-X9-<version>-<platform>.zip.

.DESCRIPTION
Build the App, Service and Helper projects with /p:MwbStandalone=true first, in a
clean checkout (see the standalone job in .github/workflows/mwb-x9-build.yml).

The three projects build into the shared <repo>\<platform>\<configuration> folder.
This script packages only the files the three apps need from there:
- every runtime, native and resource file that the apps' .deps.json files list,
  which covers the .NET runtime, NuGet packages and project references;
- the files the builds recorded in obj\...\*.FileListAbsolute.txt, such as the
  apphost exe, .runtimeconfig.json and manifests;
- the two native DLLs the apps load, PowerToys.GPOWrapper.dll and PowerToys.Interop.dll.
Debug symbols are left out.

The package folder holds bin\ (the app), the install scripts, README.md,
BUILDINFO.txt and SHA256SUMS.txt. The zip contains that folder, and
<zip>.sha256 holds the zip's own hash.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$OutputDir = 'out',
    [string]$Platform = 'x64',
    [string]$Configuration = 'Release',
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$buildDir = [IO.Path]::GetFullPath((Join-Path $RepoRoot "$Platform\$Configuration")).TrimEnd('\')
$prefix = $buildDir + '\'
$name = "MWB-X9-$Version-$Platform"
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$packageDir = Join-Path $OutputDir $name
$binDir = Join-Path $packageDir 'bin'

$requiredFiles = @(
    'PowerToys.MouseWithoutBorders.exe',
    'PowerToys.MouseWithoutBorders.dll',
    'PowerToys.MouseWithoutBordersService.exe',
    'PowerToys.MouseWithoutBordersService.dll',
    'PowerToys.MouseWithoutBordersHelper.exe',
    'PowerToys.MouseWithoutBordersHelper.dll',
    'PowerToys.GPOWrapper.dll',
    'PowerToys.Interop.dll',
    'coreclr.dll',
    'System.Private.CoreLib.dll',
    'System.Windows.Forms.dll'
)

# Files the three builds wrote into the shared output folder.
$files = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::OrdinalIgnoreCase)
# Lists from other builds name other output folders and are filtered out by $prefix below.
$lists = Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'src') -Recurse -File -Filter '*.FileListAbsolute.txt'
foreach ($list in $lists) {
    foreach ($line in Get-Content -LiteralPath $list.FullName) {
        $path = $line.Trim()
        if ($path -and $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetExtension($path) -notin @('.pdb', '.lib', '.exp', '.lastcodeanalysissucceeded') -and
            [IO.Path]::GetFileName($path) -ne 'backup_restore_settings.json' -and
            (Test-Path -LiteralPath $path -PathType Leaf)) {
            [void]$files.Add([IO.Path]::GetFullPath($path))
        }
    }
}

# Everything the apps load, from the asset lists in their .deps.json files. Self-contained builds copy
# the .NET runtime into the output folder without recording it in FileListAbsolute.txt.
$depsMissing = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($app in 'PowerToys.MouseWithoutBorders', 'PowerToys.MouseWithoutBordersService', 'PowerToys.MouseWithoutBordersHelper') {
    $depsFile = Join-Path $buildDir "$app.deps.json"
    if (-not (Test-Path -LiteralPath $depsFile)) {
        throw "$depsFile is missing. Build the $app project with /p:MwbStandalone=true first."
    }

    foreach ($appFile in "$app.exe", "$app.dll", "$app.deps.json", "$app.runtimeconfig.json") {
        [void]$files.Add((Join-Path $buildDir $appFile))
    }

    $deps = Get-Content -LiteralPath $depsFile -Raw | ConvertFrom-Json
    foreach ($target in $deps.targets.PSObject.Properties) {
        foreach ($library in $target.Value.PSObject.Properties) {
            $relativePaths = @()
            foreach ($kind in 'runtime', 'native') {
                if ($library.Value.PSObject.Properties[$kind]) {
                    $relativePaths += @($library.Value.$kind.PSObject.Properties | ForEach-Object { Split-Path -Leaf $_.Name })
                }
            }

            if ($library.Value.PSObject.Properties['resources']) {
                $relativePaths += @($library.Value.resources.PSObject.Properties | ForEach-Object { Join-Path $_.Value.locale (Split-Path -Leaf $_.Name) })
            }

            foreach ($relativePath in $relativePaths) {
                $path = Join-Path $buildDir $relativePath
                if (Test-Path -LiteralPath $path -PathType Leaf) {
                    [void]$files.Add($path)
                }
                else {
                    [void]$depsMissing.Add($relativePath)
                }
            }
        }
    }
}

if ($depsMissing.Count -gt 0) {
    throw "These .deps.json assets aren't in ${buildDir}: $($depsMissing -join ', ')"
}

foreach ($native in 'PowerToys.GPOWrapper.dll', 'PowerToys.Interop.dll') {
    [void]$files.Add((Join-Path $buildDir $native))
}

$missing = @($requiredFiles | Where-Object { -not $files.Contains((Join-Path $buildDir $_)) -or -not (Test-Path -LiteralPath (Join-Path $buildDir $_)) })
if ($missing.Count -gt 0) {
    throw "These files are missing from $buildDir or from the build file lists: $($missing -join ', ')"
}

if (Test-Path -LiteralPath $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $binDir -Force | Out-Null
foreach ($file in $files) {
    $relative = $file.Substring($prefix.Length)
    $destination = Join-Path $binDir $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file -Destination $destination
}

foreach ($file in 'Install-MWBX9.ps1', 'Uninstall-MWBX9.ps1', 'README.md') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $packageDir
}

$commit = (& git -C $RepoRoot rev-parse HEAD 2>$null)
@(
    "MWB-X9 standalone $Version ($Platform $Configuration, MwbStandalone=true)",
    "Commit: $commit",
    "Built: $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss')) UTC"
) | Set-Content -LiteralPath (Join-Path $packageDir 'BUILDINFO.txt') -Encoding ASCII

# One "<SHA-256>  <relative path>" line per file, like sha256sum.
$sums = Get-ChildItem -LiteralPath $packageDir -Recurse -File |
    Sort-Object FullName |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.FullName.Substring($packageDir.Length + 1) }
$sums | Set-Content -LiteralPath (Join-Path $packageDir 'SHA256SUMS.txt') -Encoding ASCII

$zip = Join-Path $OutputDir "$name.zip"
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($packageDir, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
'{0}  {1}' -f (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash, (Split-Path -Leaf $zip) |
    Set-Content -LiteralPath "$zip.sha256" -Encoding ASCII

# List what stayed out of the package, so a reviewer can spot a missing runtime file.
$left = @(Get-ChildItem -LiteralPath $buildDir -Recurse -File |
    Where-Object { -not $files.Contains($_.FullName) } |
    ForEach-Object { $_.FullName.Substring($prefix.Length) })
Write-Host "Packaged $($files.Count) files into $zip ($([math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)) MB)."
Write-Host "Left out $($left.Count) files from ${buildDir}:"
$left | ForEach-Object { Write-Host "  $_" }

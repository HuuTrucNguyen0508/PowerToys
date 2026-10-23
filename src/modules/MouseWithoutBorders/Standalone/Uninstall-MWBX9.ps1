#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
Removes the MWB-X9 standalone app.

.DESCRIPTION
Run this script elevated. It stops MWB-X9, deletes the MWBX9.Service service,
the MWB-X9 firewall rule, the shortcuts and the install folder. Running it
again is safe.

.PARAMETER InstallDir
Install folder. Defaults to C:\Program Files\MWB-X9. If MWBX9.Service points to
another folder, that folder is removed as well.

.PARAMETER RemoveData
Also delete the settings and logs of every user and of the SYSTEM account
(each profile's AppData\Local\Microsoft\PowerToys\MWB-X9). The settings
contain the security key. Without this switch, the settings stay, so a
reinstall keeps the pairing and layout.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\Uninstall-MWBX9.ps1 -RemoveData
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'MWB-X9'),
    [switch]$RemoveData
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$ServiceName = 'MWBX9.Service'
$FirewallRuleName = 'MWB-X9'
$ShortcutName = 'MWB-X9.lnk'
$MarkerName = '.mwb-x9-install'

function Get-ProfilePath {
    param([string]$Sid)

    $key = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$Sid"
    $item = Get-ItemProperty -LiteralPath $key -Name ProfileImagePath -ErrorAction SilentlyContinue
    if (-not $item) {
        return $null
    }

    [Environment]::ExpandEnvironmentVariables($item.ProfileImagePath)
}

# Reads a folder such as 'Local AppData' or 'Startup' for another user.
# The user's registry hive is loaded only while they are signed in, so fall back to the default location.
function Get-UserShellFolder {
    param([string]$Sid, [string]$ProfilePath, [string]$ValueName, [string]$DefaultRelativePath)

    $value = $null
    $key = "Registry::HKEY_USERS\$Sid\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"
    if (Test-Path -LiteralPath $key) {
        $value = (Get-Item -LiteralPath $key).GetValue($ValueName, $null, 'DoNotExpandEnvironmentNames')
    }

    if (-not $value) {
        return Join-Path $ProfilePath $DefaultRelativePath
    }

    $value = $value -ireplace [regex]::Escape('%USERPROFILE%'), $ProfilePath.Replace('$', '$$')
    [Environment]::ExpandEnvironmentVariables($value)
}

# Includes the SYSTEM profile (S-1-5-18), where the service-mode workers write their logs.
function Get-UserProfileEntry {
    Get-ChildItem -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList' |
        ForEach-Object {
            $path = Get-ProfilePath $_.PSChildName
            if ($path) {
                [pscustomobject]@{ Sid = $_.PSChildName; ProfilePath = $path }
            }
        }
}

function Stop-InstalledProcess {
    param([string]$Dir)

    $prefix = [IO.Path]::GetFullPath($Dir).TrimEnd('\') + '\'
    $processes = @(Get-CimInstance -ClassName Win32_Process -Filter "Name LIKE 'PowerToys.MouseWithoutBorders%'" |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })

    foreach ($process in $processes) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }

    foreach ($process in $processes) {
        Wait-Process -Id $process.ProcessId -Timeout 10 -ErrorAction SilentlyContinue
    }
}

function Remove-Folder {
    param([string]$Path)

    $ancestor = Get-Item -LiteralPath $Path -Force
    while ($ancestor) {
        if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing to delete through reparse point $($ancestor.FullName). Remove the link manually."
        }
        $ancestor = $ancestor.Parent
    }
    if (Get-ChildItem -LiteralPath $Path -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1) {
        throw "Refusing to delete $Path because it contains a reparse point. Remove the link manually."
    }

    for ($attempt = 1; (Test-Path -LiteralPath $Path) -and $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force
        }
        catch {
            if ($attempt -eq 5) {
                throw
            }

            Start-Sleep -Seconds 2
        }
    }
}

# --- Find every install folder ----------------------------------------------------------------

$installDirs = @([IO.Path]::GetFullPath($InstallDir).TrimEnd('\'))

$service = Get-CimInstance -ClassName Win32_Service -Filter "Name = '$ServiceName'"
if ($service -and $service.PathName -match '^"([^"]+)"') {
    $serviceDir = Split-Path -Parent $Matches[1]
    if ($installDirs -notcontains $serviceDir) {
        $installDirs += $serviceDir
    }
}

# Leave the current folder so it can be deleted, even when this script runs from the install folder.
Set-Location -LiteralPath $env:SystemRoot

# --- Stop MWB-X9 and remove the service -------------------------------------------------------

if ($service) {
    if ($service.State -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    }

    & sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -eq 1072) {
        Write-Warning "$ServiceName is marked for deletion. Close the Services console or restart Windows to finish removing it."
    }
    elseif ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1060) {
        throw "sc.exe delete $ServiceName failed with exit code $LASTEXITCODE."
    }
}

foreach ($dir in $installDirs) {
    Stop-InstalledProcess $dir
}

# --- Firewall and shortcuts -------------------------------------------------------------------

Get-NetFirewallRule -Name $FirewallRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Get-NetFirewallRule -DisplayName $FirewallRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule

$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) $ShortcutName
if (Test-Path -LiteralPath $startMenuShortcut) {
    Remove-Item -LiteralPath $startMenuShortcut -Force
}

$profiles = @(Get-UserProfileEntry)
foreach ($userProfile in $profiles) {
    $startup = Get-UserShellFolder $userProfile.Sid $userProfile.ProfilePath 'Startup' 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup'
    $shortcut = Join-Path $startup $ShortcutName
    if (Test-Path -LiteralPath $shortcut) {
        Remove-Item -LiteralPath $shortcut -Force
    }
}

# --- Install folders --------------------------------------------------------------------------

foreach ($dir in $installDirs) {
    if (-not (Test-Path -LiteralPath $dir)) {
        continue
    }

    # Only delete folders that Install-MWBX9.ps1 created.
    if (-not (Test-Path -LiteralPath (Join-Path $dir $MarkerName) -PathType Leaf)) {
        Write-Warning "Skipped $dir because it doesn't look like an MWB-X9 install folder."
        continue
    }

    Remove-Folder $dir
    Write-Host "Removed $dir"
}

# --- Settings and logs ------------------------------------------------------------------------

if ($RemoveData) {
    foreach ($userProfile in $profiles) {
        $localAppData = Get-UserShellFolder $userProfile.Sid $userProfile.ProfilePath 'Local AppData' 'AppData\Local'
        $powerToysDir = Join-Path $localAppData 'Microsoft\PowerToys'
        $dataDir = Join-Path $powerToysDir 'MWB-X9'
        if (Test-Path -LiteralPath $dataDir) {
            Remove-Folder $dataDir
            Write-Host "Removed $dataDir"
        }

        # MWB-X9 creates Microsoft\PowerToys when PowerToys isn't installed; remove it only if it's now empty.
        if ((Test-Path -LiteralPath $powerToysDir) -and -not (Get-ChildItem -LiteralPath $powerToysDir -Force | Select-Object -First 1)) {
            Remove-Item -LiteralPath $powerToysDir -Force
        }
    }

    # MWB writes errors to the Application event log under these sources. PowerToys MWB uses the same
    # names, so keep them when PowerToys is installed.
    $powerToysInstalled = (Get-Service -Name 'PowerToys.MWB.Service' -ErrorAction SilentlyContinue) -or
        (Test-Path -LiteralPath (Join-Path $env:ProgramFiles 'PowerToys\PowerToys.exe')) -or
        @($profiles | Where-Object { Test-Path -LiteralPath (Join-Path $_.ProfilePath 'AppData\Local\PowerToys\PowerToys.exe') }).Count -gt 0
    if (-not $powerToysInstalled) {
        foreach ($source in 'MouseWithoutBorders', 'MouseWithoutBordersHelper') {
            if ([Diagnostics.EventLog]::SourceExists($source)) {
                [Diagnostics.EventLog]::DeleteEventSource($source)
            }
        }
    }
}

Write-Host 'MWB-X9 is uninstalled.'
if (-not $RemoveData) {
    Write-Host 'Settings and logs were kept. Run again with -RemoveData to delete them.'
}

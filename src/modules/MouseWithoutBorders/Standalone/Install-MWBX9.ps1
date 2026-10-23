#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
Installs the MWB-X9 standalone app (Mouse Without Borders for up to 9 machines).

.DESCRIPTION
Run this script elevated from the extracted MWB-X9 package folder. It:
- copies the files in bin\ to the install folder;
- registers the MWBX9.Service launcher service for one user, the same way the
  PowerToys MWB module registers PowerToys.MWB.Service;
- adds the inbound firewall rule MWB-X9;
- creates a Start Menu shortcut and, with -Autostart, a logon shortcut.

Running it again converges to the same state, so it also updates or repairs an
install and switches between service and non-service mode.

.PARAMETER InstallDir
Install folder. Must be directly under Program Files. Defaults to C:\Program Files\MWB-X9.

.PARAMETER User
The account that uses MWB on this laptop, as DOMAIN\name or name. Service mode
reads that user's settings, and only that user can start the service. Defaults
to the user signed in at the console.

.PARAMETER NoService
Don't use service mode. The shortcuts start the app as the signed-in user, and
an existing MWBX9.Service is removed. Without service mode, MWB can't control
UAC prompts or the lock screen.

.PARAMETER Autostart
Also start MWB when the user signs in. Without this switch, an existing logon
shortcut is removed.

.PARAMETER AllowPublicNetwork
Also open the firewall on networks that Windows marks as Public. By default the
rule covers only Private and Domain networks.

.PARAMETER TcpPort
The TCP port set in MWB (TCPPort in settings.json). MWB uses this port and the
next one. Defaults to 15100.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\Install-MWBX9.ps1 -Autostart
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'MWB-X9'),
    [string]$User,
    [switch]$NoService,
    [switch]$Autostart,
    [switch]$AllowPublicNetwork,
    [ValidateRange(1, 65534)]
    [int]$TcpPort = 15100
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$ServiceName = 'MWBX9.Service'
$FirewallRuleName = 'MWB-X9'
$ShortcutName = 'MWB-X9.lnk'
$MarkerName = '.mwb-x9-install'
$AppExe = 'PowerToys.MouseWithoutBorders.exe'
$ServiceExe = 'PowerToys.MouseWithoutBordersService.exe'

function Resolve-TargetUser {
    param([string]$Name)

    if (-not $Name) {
        $Name = (Get-CimInstance -ClassName Win32_ComputerSystem).UserName
    }

    if (-not $Name) {
        $Name = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        Write-Warning "No one is signed in at the console, so MWB-X9 is set up for $Name. Use -User to choose another account."
    }

    $account = New-Object Security.Principal.NTAccount($Name)
    $sid = $account.Translate([Security.Principal.SecurityIdentifier]).Value
    [pscustomobject]@{ Name = $Name; Sid = $sid }
}

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

function Stop-MwbService {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
    }
}

function Remove-MwbService {
    if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
        return
    }

    Stop-MwbService
    & sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -eq 1072) {
        Write-Warning "$ServiceName is marked for deletion. Close the Services console or restart Windows to finish removing it."
    }
    elseif ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1060) {
        throw "sc.exe delete $ServiceName failed with exit code $LASTEXITCODE."
    }
}

function Set-Shortcut {
    param([string]$Path, [string]$Target, [string]$Arguments, [string]$WorkingDirectory)

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = "$Target,0"
    $shortcut.Description = 'Mouse Without Borders for up to 9 machines'
    $shortcut.Save()
}

function Assert-ProtectedPath {
    param([string]$Path)

    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Refusing reparse point $Path. Install files and ancestors must be real local paths."
    }

    $acl = Get-Acl -LiteralPath $Path
    $trusted = @('S-1-5-18', 'S-1-5-32-544', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted) {
        throw "Refusing $Path because its owner can replace SYSTEM service files. Use an administrator-owned folder under Program Files."
    }

    # At the volume root, creating a sibling is safe. Deleting or changing this ancestor is not.
    $writeMask = 0x500D0156
    if ($item.FullName.TrimEnd('\') -eq [IO.Path]::GetPathRoot($item.FullName).TrimEnd('\')) { $writeMask = 0x500D0040 }
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and
            -not ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -and
            $rule.IdentityReference.Value -notin $trusted -and
            ([int]$rule.FileSystemRights -band $writeMask)) {
            throw "Refusing writable path $Path. Only SYSTEM and administrators may change service files or their ancestors."
        }
    }
}

# --- Check the package and the install folder -------------------------------------------------

$InstallDir = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
$programFiles = [Environment]::GetFolderPath('ProgramFiles').TrimEnd('\')
if ((Split-Path -Parent $InstallDir) -ne $programFiles) {
    throw 'InstallDir must be a direct child of Program Files. User-writable parents let another user replace the SYSTEM service.'
}

$ancestor = Get-Item -LiteralPath $programFiles
while ($ancestor) {
    Assert-ProtectedPath $ancestor.FullName
    $ancestor = $ancestor.Parent
}

if (Test-Path -LiteralPath $InstallDir) {
    Assert-ProtectedPath $InstallDir
    Get-ChildItem -LiteralPath $InstallDir -Force -Recurse | ForEach-Object { Assert-ProtectedPath $_.FullName }
}

$source = Join-Path $PSScriptRoot 'bin'
if (-not (Test-Path -LiteralPath (Join-Path $source $AppExe))) {
    throw "Run this script from the extracted MWB-X9 package folder. $source\$AppExe is missing."
}

# robocopy /MIR below deletes everything else in the install folder, so only mirror into an empty
# folder or an earlier MWB-X9 install.
if ((Test-Path -LiteralPath $InstallDir) -and
    (Get-ChildItem -LiteralPath $InstallDir -Force | Select-Object -First 1) -and
    -not (Test-Path -LiteralPath (Join-Path $InstallDir $MarkerName))) {
    throw "$InstallDir is not empty and is not an MWB-X9 install. Choose another -InstallDir."
}

$target = Resolve-TargetUser $User
$profilePath = Get-ProfilePath $target.Sid
if (-not $profilePath) {
    throw "$($target.Name) has no user profile on this computer yet. Sign in as that user once, then run this script again."
}

$localAppData = Get-UserShellFolder $target.Sid $profilePath 'Local AppData' 'AppData\Local'
$startupDir = Get-UserShellFolder $target.Sid $profilePath 'Startup' 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup'
if (-not [IO.Path]::IsPathRooted($localAppData) -or $localAppData.IndexOfAny([char[]]@('"', "`r", "`n")) -ge 0) {
    throw 'The target user LocalAppData must be an absolute path without quotes or newlines.'
}

$otherMwb = @(Get-CimInstance -ClassName Win32_Process -Filter "Name = '$AppExe'" |
    Where-Object { $_.ExecutablePath -and -not $_.ExecutablePath.StartsWith($InstallDir + '\', [StringComparison]::OrdinalIgnoreCase) })
if ($otherMwb.Count -gt 0) {
    Write-Warning "Another Mouse Without Borders is running from $($otherMwb[0].ExecutablePath). It uses the same TCP ports, so turn it off in PowerToys before you start MWB-X9."
}

# --- Copy the files ---------------------------------------------------------------------------

Write-Host "Installing MWB-X9 to $InstallDir for $($target.Name)"
Stop-MwbService
Stop-InstalledProcess $InstallDir

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)')
Set-Acl -LiteralPath $InstallDir -AclObject $acl
Set-Content -LiteralPath (Join-Path $InstallDir $MarkerName) -Value 'MWB-X9 install folder. Uninstall-MWBX9.ps1 deletes this folder.' -Encoding ASCII

& robocopy.exe $source $InstallDir /MIR /R:3 /W:1 /NFL /NDL /NJH /NJS /NP /XF $MarkerName 'Install-MWBX9.ps1' 'Uninstall-MWBX9.ps1' 'README.md' | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "robocopy failed with exit code $LASTEXITCODE."
}

foreach ($file in 'Install-MWBX9.ps1', 'Uninstall-MWBX9.ps1', 'README.md') {
    $from = Join-Path $PSScriptRoot $file
    if (Test-Path -LiteralPath $from) {
        Copy-Item -LiteralPath $from -Destination $InstallDir -Force
    }
}

Get-ChildItem -LiteralPath $InstallDir -Recurse -File | Unblock-File

Assert-ProtectedPath $InstallDir
Get-ChildItem -LiteralPath $InstallDir -Force -Recurse | ForEach-Object { Assert-ProtectedPath $_.FullName }

$appPath = Join-Path $InstallDir $AppExe

# --- Service ----------------------------------------------------------------------------------

if ($NoService) {
    Remove-MwbService
}
else {
    $servicePathArgument = $localAppData -replace '(\\+)$', '$1$1'
    $binPath = '"{0}" "{1}"' -f (Join-Path $InstallDir $ServiceExe), $servicePathArgument

    $existing = Get-CimInstance -ClassName Win32_Service -Filter "Name = '$ServiceName'"
    if ($existing) {
        # LocalSystem, demand start (dllmain.cpp:319-332). The service type and error control
        # never change after New-Service below creates the service.
        $result = Invoke-CimMethod -InputObject $existing -MethodName Change -Arguments @{
            DisplayName   = $ServiceName
            PathName      = $binPath
            StartMode     = 'Manual'
            StartName     = 'LocalSystem'
            StartPassword = ''
        }
        if ($result.ReturnValue -ne 0) {
            throw "Couldn't update $ServiceName (Win32_Service.Change returned $($result.ReturnValue)). If it is marked for deletion, restart Windows and run this script again."
        }
    }
    else {
        # New-Service creates a LocalSystem, own-process service with normal error control.
        New-Service -Name $ServiceName -DisplayName $ServiceName -BinaryPathName $binPath -StartupType Manual `
            -Description 'Starts MWB-X9 (Mouse Without Borders) on the secure desktop so it can control UAC prompts and the lock screen.' | Out-Null
    }

    # Port of generateSecurityDescriptor (ModuleInterface/generateSecurityDescriptor.h:5-9) for the
    # target user's SID. The last DACL entry lets that user start, stop and query the service.
    $sddl = 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)(A;;CR;;;AU)(A;;CCLCSWRPWPDTLOCRRC;;;PU)(A;;RPWPDTLO;;;' +
        $target.Sid + ')S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)'

    # dllmain.cpp:356 applies it with DACL_SECURITY_INFORMATION, so only the D: part takes effect.
    # Apply the DACL on every run; dllmain.cpp sets it only when it creates the service.
    $dacl = $sddl.Substring(0, $sddl.IndexOf(')S:(') + 1)
    & sc.exe sdset $ServiceName $dacl | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe sdset $ServiceName failed with exit code $LASTEXITCODE."
    }
}

# --- Firewall ---------------------------------------------------------------------------------

Get-NetFirewallRule -Name $FirewallRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Get-NetFirewallRule -DisplayName $FirewallRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule

$firewallProfiles = @('Domain', 'Private')
if ($AllowPublicNetwork) {
    $firewallProfiles += 'Public'
}

New-NetFirewallRule -Name $FirewallRuleName -DisplayName $FirewallRuleName `
    -Description 'Lets other MWB-X9 machines on the local subnet connect to Mouse Without Borders.' `
    -Direction Inbound -Action Allow -Program $appPath -Protocol TCP -LocalPort ('{0}-{1}' -f $TcpPort, ($TcpPort + 1)) `
    -RemoteAddress LocalSubnet -Profile $firewallProfiles | Out-Null

# --- Shortcuts --------------------------------------------------------------------------------

$arguments = 'UseService'
if ($NoService) {
    $arguments = ''
}

$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) $ShortcutName
Set-Shortcut $startMenuShortcut $appPath $arguments $InstallDir

# Keep at most one logon shortcut: the target user's, and only with -Autostart.
foreach ($userProfile in Get-UserProfileEntry) {
    $userStartup = Get-UserShellFolder $userProfile.Sid $userProfile.ProfilePath 'Startup' 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup'
    $stale = Join-Path $userStartup $ShortcutName
    if (Test-Path -LiteralPath $stale) {
        Remove-Item -LiteralPath $stale -Force
    }
}

$autostartShortcut = Join-Path $startupDir $ShortcutName
if ($Autostart) {
    Set-Shortcut $autostartShortcut $appPath $arguments $InstallDir
}

# --- Summary ----------------------------------------------------------------------------------

$mode = 'service mode (MWBX9.Service)'
if ($NoService) {
    $mode = 'non-service mode'
}

Write-Host "MWB-X9 is installed in $mode for $($target.Name)."
Write-Host "  Settings: $localAppData\Microsoft\PowerToys\MWB-X9\settings.json"
Write-Host "  Start Menu: $startMenuShortcut"
if ($Autostart) {
    Write-Host "  Starts at sign-in: $autostartShortcut"
}
Write-Host "Sign in as $($target.Name) and start MWB-X9 from the Start Menu to pair this machine."

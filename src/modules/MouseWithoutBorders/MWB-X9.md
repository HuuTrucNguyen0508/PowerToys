# MWB-X9: Mouse Without Borders with up to 9 machines

This fork of PowerToys v0.102.0 raises the Mouse Without Borders (MWB) limit from 4 machines to 9, laid out as a 3×3 grid.

It has not been tested on real hardware yet. Treat it as experimental until the checks in [Before you rely on it](#before-you-rely-on-it) pass.

## Standalone app

You can also run the fork without PowerToys, as one portable folder with install and uninstall scripts. This suits laptops that only need Mouse Without Borders, such as a temporary provisioning setup. The [package README](Standalone/README.md) covers install, pairing, the 3×3 layout, service mode, uninstall and unsigned-file warnings.

- **Get it.** The `standalone` job of `.github/workflows/mwb-x9-build.yml` uploads `MWB-X9-<version>-x64.zip`. To build it yourself, build `App/MouseWithoutBorders.csproj`, `App/Service/MouseWithoutBordersService.csproj` and `App/Helper/MouseWithoutBordersHelper.csproj` with `/p:MwbStandalone=true` in a clean checkout, then run `Standalone/New-MWBX9Package.ps1 -Version <version>`.
- **What the build switch changes.** `/p:MwbStandalone=true` defines `MWB_STANDALONE` and sets the product name to `MWB-X9`. Without it, the build is the normal PowerToys build.
  - The original MWB window and setup wizard are always on, unless the `MwbUseOriginalUserInterface` policy disables them. A fresh install opens the wizard.
  - Settings and logs are in `%LOCALAPPDATA%\Microsoft\PowerToys\MWB-X9`. In service mode, the SYSTEM copies log under `C:\Windows\System32\config\systemprofile\AppData\Local\Microsoft\PowerToys\MWB-X9`.
  - The service is `MWBX9.Service`. `Install-MWBX9.ps1` registers it the way the PowerToys module registers `PowerToys.MWB.Service`: LocalSystem, demand start, and the same DACL for the user.
  - The single-instance mutex, event and window title use `MWB-X9`, so the app never mistakes a PowerToys MWB for a second copy of itself.
  - The app and helper don't wait for `PowerToys.exe`, don't start an ETW trace, and don't open the Settings IPC pipe.
- **Same rules as the DLL build.** Every machine in a grid runs the same standalone package. Don't mix it with PowerToys machines, and don't run MWB-X9 and PowerToys MWB on the same laptop: they use the same TCP ports.
- **Not changed.** PowerToys policies under `HKLM\SOFTWARE\Policies\PowerToys` still apply. Telemetry stays off unless PowerToys diagnostic data was turned on for the user. The settings window can't change keyboard shortcuts, so the defaults from `settings.json` apply.

## What changed and why

Upstream MWB has no 4-machine limit in its network protocol. The limit lives in local code: a `MAX_MACHINE = 4` constant, a hard-coded 2×2 layout, and a few literal `4`s. This fork changes only that code:

- **Capacity.** `MachineStuff.MAX_MACHINE` is 9. The grid is `MachineStuff.MATRIX_COLUMNS` (3) wide. The machine pool, layout and hotkeys all derive from these two constants.
- **Layout and navigation.** Edge switching works on any grid. Moving off an edge goes to the nearest machine in the same row or column. Empty slots and disconnected machines are skipped. The cursor never moves diagonally and never spills into another row. With "wrap" on, it wraps within the same row or column. In upstream, two-row mode did not skip disconnected machines. Now both modes do.
- **Saved layouts are kept.** Upstream reset any saved layout that did not have exactly 4 slots. The fork pads or trims it instead. An existing 2×2 layout keeps its positions in the 3×3 grid:

  ```text
  A B        A B .
  C D   ->   C D .
             . . .
  ```

  The settings file format doesn't change. `MachineMatrixString` is still a flat, row-major list of names with `""` for an empty slot.
- **New machines don't reshuffle the layout.** Upstream rebuilt the whole layout in pool order whenever it learned a new machine. The fork puts the new machine in the first empty slot and leaves the rest alone.
- **Joining no longer reconnects existing machines.** Upstream closed and reopened every connection whenever a machine joined, so the whole grid dropped for a moment. The fork sends the new layout on the connections that are already up and opens only the connections to the new machine.
  Automatic joins use the optional Matrix flag `8`. Updated peers merge concurrent joins and settle on the same layout. Explicit layout edits still replace the layout, so you can remove machines. Older mwb-x9 builds ignore this flag and remain wire-compatible, but every peer needs the updated build for concurrent-join convergence. Wait for joins to finish before rearranging or removing machines; an in-flight join can otherwise restore an older position or a live machine.
- **Pair by name or IP.** In the setup wizard, and in PowerToys Settings, you can enter the other computer's name or its IPv4 or IPv6 address. MWB connects to the address directly. Once the machine there proves the security key, its real name replaces the address in the layout.
- **IPv4 addresses are shared automatically.** Each layout packet also carries the IPv4 address the sender sees for that slot, so machines can reach each other on a network without name resolution. Every machine also records the address of each machine that connects to it. Learned addresses are added to the IP mappings as `NAME IP #learned` lines. A newer address for the same name replaces the learned line; lines you wrote yourself are never changed. If the `MwbDisableUserDefinedIpMappingRules` policy blocks IP mappings, learned addresses are kept in memory until MWB restarts. IPv6 still needs names or IP mappings: the free bytes in a layout packet only fit an IPv4 address.
  Learning happens only after the peer proves the shared key. The cache and saved mappings keep at most 36 learned lines, and names containing whitespace or control characters are rejected. User lines have no automatic eviction. Settings reloads and editor writes merge the learned cache back into the mappings. A peer with the key can advertise another machine's address, but connecting to that address still requires the key.
- **Layout broadcasts.** Each layout packet now carries the sender's slot count in bytes that official builds ignore. A fork machine ignores layouts from a sender with a different slot count, including official builds, which send 0. It stages slots separately for each TCP connection and applies a layout only after every slot arrives in order. Concurrent broadcasts from one machine are serialized.
- **Hotkeys.** Ctrl+Alt+1…9 and Ctrl+Alt+F1…F9 switch to slots 1–9.
- **Smaller fixes.**
  - Policy IP mappings (`MwbPolicyDefinedIpMappingRules`) now apply even when the user's IP mappings are empty.
  - Duplicate-packet detection is per sender.
  - A machine never evicts itself from its own pool.
  - A 4-entry machine pool saved by an official build loads instead of being wiped.
- **Legacy layout editor.** The original MWB settings window shows 3 rows of tiles. In one-row mode it widens to fit 9 tiles, up to the width of the screen.

The new logic is in dependency-free files: `App/Core/MatrixLayout.cs`, `App/Core/GridNavigator.cs`, `App/Core/MatrixPacketBuffer.cs`, `App/Core/PeerAddress.cs` and `App/Core/Name2IpMap.cs`. Their unit tests are in `MouseWithoutBorders.UnitTests/Core/`.

## Fleet rule: every machine runs the same fork build

Every machine in the group must run this fork, built from the same PowerToys release that is installed on it (v0.102.0).

- **No mixed fleets.** Official 4-machine builds don't understand slots 5–9. Upstream MWB also overwrites its peers' layouts with its own 4-slot version. This fork ignores official layouts to protect the 9-slot grid, so an official machine and a fork machine never agree on a layout.
- **No mixed releases.** MWB encryption changed between releases: v0.100.x and v0.102.0 can't authenticate each other. Build the fork from the tag that is installed. Rebuild it when you upgrade PowerToys.

## Build on Windows

Requirements: Windows 11, Visual Studio with the PowerToys workloads (see `doc/devdocs/readme.md`), and this branch checked out.

1. Find the installed PowerToys version: Settings → General, or the file properties of the installed `PowerToys.MouseWithoutBorders.dll`. For example: `0.102.0`.
2. Restore packages once from the repo root:

   ```powershell
   .\tools\build\build-essentials.ps1 -Platform x64 -Configuration Release
   ```

3. Build only the MWB app project. Set `Version` to the installed version:

   ```powershell
   cd src\modules\MouseWithoutBorders\App
   ..\..\..\..\tools\build\build.ps1 -Platform x64 -Configuration Release '/p:Version=0.102.0'
   ```

   The version matters. MWB puts its version into the names of its single-instance mutex and event. Local builds default to `0.0.1`. A DLL with a different version could run next to the installed one, and both would compete for the same ports.

4. The output is `x64\Release\PowerToys.MouseWithoutBorders.dll` under the repo root. Check its file version: it must match the installed DLL.

## Deploy to each machine

Replace only the DLL. Keep Microsoft's signed `PowerToys.MouseWithoutBorders.exe`. The official, signed Settings app only talks to an MWB process whose `.exe` has Microsoft's signature, so a rebuilt `.exe` breaks the Settings connection. The `.exe` is a small launcher that loads the DLL.

On each machine, as an administrator:

1. Quit PowerToys from the tray icon. Make sure no `PowerToys.MouseWithoutBorders.exe` process is still running.
2. Find the install folder. It is usually `C:\Program Files\PowerToys`, or `%LOCALAPPDATA%\PowerToys` for a per-user install.
3. Back up the original DLL and record its hash:

   ```powershell
   $dir = 'C:\Program Files\PowerToys'
   Copy-Item "$dir\PowerToys.MouseWithoutBorders.dll" "$env:USERPROFILE\MWB-backup\PowerToys.MouseWithoutBorders.dll.orig" -Force
   Get-FileHash "$dir\PowerToys.MouseWithoutBorders.dll" -Algorithm SHA256 | Out-File "$env:USERPROFILE\MWB-backup\original.sha256.txt"
   ```

   Create the `MWB-backup` folder first.
4. Back up the MWB settings: `%LOCALAPPDATA%\Microsoft\PowerToys\MouseWithoutBorders\settings.json`. Don't copy this file to other machines. In service mode, its pairing key is encrypted for the local user.
5. Copy the fork DLL over `PowerToys.MouseWithoutBorders.dll`.
6. Start PowerToys. In Settings → Mouse Without Borders, check that the connection status and pairing still work.

### Turn off automatic updates

A PowerToys update or repair reinstalls the official DLL. On each machine, set the "disable automatic update download" policy, which is registry value `AutomaticUpdateDownloadDisabled` (DWORD `1`) under `HKLM\SOFTWARE\Policies\PowerToys`. After any PowerToys repair or reinstall, copy the fork DLL in again.

## Edit the layout

Use the original MWB settings window:

1. In PowerToys Settings → Mouse Without Borders, turn on **Show the original Mouse Without Borders UI**, then restart MWB.
2. Open the original window from the MWB tray icon.
3. Make sure **Grid (3×3)** is checked. Upstream calls this check box **Two Row**. Drag the tiles to match where the screens physically sit, then select **Apply**. The layout is sent to every connected machine.

The official PowerToys Settings page still shows the 9 slots in 2 or 4 columns, not 3×3. That is only how it draws them. Dragging there still swaps slots correctly, but the picture doesn't match the grid. The policy `MwbUseOriginalUserInterface` must not be set to disabled, or the original window is blocked.

## Roll back

On each machine:

1. Quit PowerToys.
2. Copy the backed-up `PowerToys.MouseWithoutBorders.dll.orig` back into the install folder as `PowerToys.MouseWithoutBorders.dll`. Check that its SHA-256 matches `original.sha256.txt`.
3. Restore the backed-up `settings.json`. Without it, the official build resets a 9-slot layout to empty and loads only the first 4 machines from the pool.
4. Start PowerToys. Remove the update policy if you no longer want it.

Alternatively, run PowerToys repair or reinstall the same official version. The background service was never modified, so it needs no rollback.

## Clean up a machine completely

1. In Settings → Mouse Without Borders, uninstall the service if service mode was used.
2. Uninstall PowerToys.
3. Check that `sc.exe query PowerToys.MWB.Service` reports that the service does not exist.
4. Check that `netsh advfirewall firewall show rule name=all | findstr /i MouseWithoutBorders` shows no rules.
5. Delete `%LOCALAPPDATA%\Microsoft\PowerToys\MouseWithoutBorders`. In service mode, also delete the same folder under `C:\Windows\System32\config\systemprofile\AppData\Local`.
6. Remove any policies you added under `HKLM\SOFTWARE\Policies\PowerToys`.

## Known limitations

- **No mixed fleets.** All machines must run this fork, built from the same release. There is no mode where official machines join a fork grid.
- **The Settings page is cosmetic only.** It shows 9 tiles in 2 or 4 columns. Use the original window to arrange the grid.
- **Untested on hardware.** Not yet verified:
  - the DLL-only swap with the signed `.exe`;
  - service mode and UAC prompts on slots 5–9;
  - sleep and resume;
  - a cold start of all 9 machines at once.

  The unit tests cover only the layout and navigation logic.
- **Holes can be refilled.** A machine missing from its own layout adds itself to the first empty slot and saves the result. It leaves holes alone when it is already placed. Newly discovered peers and the official Settings page can also fill empty slots.
- **Connection checks on edge hits.** Navigation tests candidates in the selected row or column and stops at the nearest connected machine. Each connection check can send a small test packet.
- **Name resolution.** Machines find each other by name (DNS), by the IP mappings in settings, or by the IPv4 addresses they learn from each other. On a network without DNS, pair each new machine by the first machine's IP address. IPv6-only networks still need names or IP mappings.
  Typed IPv4 addresses use four decimal octets, including when an octet has leading zeros. Do not append a port. Incoming connections use the authenticated machine name without a reverse DNS lookup. On a laptop with several network interfaces, a shared address may be unreachable from another laptop; add a reachable address in IP mappings if needed.
- **Validate remote machine IP Address needs DNS.** This option (policy `MwbValidateRemoteIp`) checks DNS-resolved addresses with a reverse lookup, so leave it off on a network without DNS. It is off by default. Typed and learned addresses skip this check, as IP mappings always did. **Same subnet only** (`MwbSameSubnetOnly`, off by default) compares only the first two bytes of IPv4 addresses, so it doesn't block a closed LAN.

## A 12-machine build

Set `MachineStuff.MAX_MACHINE = 12` in `App/Core/MachineStuff.cs` and rebuild. Everything else derives from it, and `MATRIX_COLUMNS = 3` gives a 3×4 grid (3 columns, 4 rows). Hotkeys then cover Ctrl+Alt+F1…F12 and Ctrl+Alt+1…9; there is no digit hotkey for slots 10–12. A 9-machine build and a 12-machine build ignore each other's layouts, so the fleet rule applies here too: every machine runs the same build.

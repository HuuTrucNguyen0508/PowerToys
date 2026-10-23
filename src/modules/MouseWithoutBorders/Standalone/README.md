# MWB-X9 standalone

MWB-X9 is Mouse Without Borders from PowerToys v0.102.0, changed to support up to 9 machines in a 3×3 grid. This package runs on its own: you don't need PowerToys. One mouse and keyboard control every laptop on the same network, with a shared clipboard and file drag-and-drop.

This build is experimental and has not been tested on real hardware yet. Try it on two or three laptops before you rely on it for a full set of 9.

## Before you start

- **Every machine runs this same build.** Use the same package version on all laptops. Official PowerToys builds and other fork builds can't join an MWB-X9 grid.
- **One Mouse Without Borders per laptop.** If PowerToys is installed, turn its Mouse Without Borders off. Both use TCP ports 15100–15101.
- **Same network.** The laptops must be on one local subnet. They find each other by computer name, or by IPv4 addresses they share with each other, so a closed network without DNS works. On an IPv6-only network, add IP mappings on the **IP Mappings** tab of the settings window.
- **Windows 10 or 11, x64.** You need administrator rights to install.

### Unsigned files

This build isn't code-signed, so Windows security features may block it:

- **SmartScreen and Mark-of-the-Web.** Files from a downloaded zip are marked as coming from the internet. Before you extract the zip, unblock it:

  ```powershell
  Unblock-File .\MWB-X9-0.102.0.0-x64.zip
  ```

  The install script also unblocks the files it copies.
- **AppLocker or WDAC.** If the laptops enforce application control, allow the install folder or the file hashes in `SHA256SUMS.txt`. Otherwise the app, the service or the clipboard helper won't start.
- **Microsoft Defender or other antivirus.** The app hooks the keyboard and mouse and runs a service as SYSTEM, which some products flag. If a file is quarantined, check its hash against `SHA256SUMS.txt` before you allow it.

To check the extracted files against `SHA256SUMS.txt`, run this in the package folder:

```powershell
Get-Content .\SHA256SUMS.txt | ForEach-Object {
    $hash, $path = $_ -split '  ', 2
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash) { "Mismatch: $path" }
}
```

No output means every file matches.

## Install

Do this on each laptop.

1. Extract the zip and open the extracted folder.
2. Open PowerShell as an administrator in that folder.
3. Run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Install-MWBX9.ps1 -Autostart
   ```

The script:

- copies the app to `C:\Program Files\MWB-X9`;
- registers the `MWBX9.Service` service for the user who is signed in at the console;
- adds the inbound firewall rule `MWB-X9` for TCP 15100–15101 from the local subnet, on Private and Domain networks;
- adds **MWB-X9** to the Start Menu and, with `-Autostart`, starts it when that user signs in.

Options:

| Option | Effect |
|---|---|
| `-User DOMAIN\name` | Set up for another account instead of the console user. That account must have signed in once. |
| `-NoService` | Run without service mode. See [Service mode and UAC](#service-mode-and-uac). |
| `-Autostart` | Start MWB-X9 when the user signs in. |
| `-AllowPublicNetwork` | Also open the firewall on networks that Windows marks as Public. |
| `-InstallDir <path>` | Use another direct child of Program Files. The folder, its contents and its ancestors must have administrator ownership and no non-admin write permissions. Links and junctions are refused. |
| `-TcpPort <port>` | Open a different port pair if you changed `TCPPort` in the settings. |

Running the script again updates or repairs the install. It also switches modes: the options of the last run apply.

## Pair the laptops

1. On the first laptop, sign in as the user you installed for and start **MWB-X9** from the Start Menu.
2. The setup wizard opens. At **Have you already installed Mouse without Borders on another computer?**, select **No**. The wizard shows a security code and this computer's name. Leave it open.
3. On each other laptop, start **MWB-X9**, select **Yes**, and enter the security code and the first laptop's name or IP address. On a network without DNS, use the IP address. To find it, run `ipconfig` on the first laptop.

All laptops share the same security code. Each new laptop takes the first free slot in the 3×3 grid. Laptops that are already connected stay connected while another one joins.

Use a plain IP address without a port. IPv4 octets are decimal even when they start with zero. To get concurrent-join convergence, update every laptop to this package. Wait for joining to finish before moving or removing tiles.

The laptops tell each other the IPv4 addresses they see, so a new laptop also reaches the others without DNS. The addresses they learn appear on the **IP Mappings** tab as `NAME IP #learned` lines and are updated when an address changes. Lines you add yourself are never changed. IPv6 addresses aren't shared.

MWB keeps up to 36 learned mappings and preserves them when settings are edited. If a laptop has several network interfaces and a shared address is unreachable, add its reachable LAN address in IP mappings.

Leave **Validate remote machine IP Address** off on a network without DNS: it checks addresses with a reverse DNS lookup.

## Arrange the 3×3 layout

1. Right-click the MWB-X9 tray icon and select **Settings**.
2. On the **Machine Setup** tab, make sure **Grid (3×3)** is selected.
3. Drag the tiles to match where the screens sit on the desks. To add a machine that isn't shown, select an empty tile's check box and type the computer name.
4. Select **Apply**. The layout is sent to every connected laptop.

The mouse moves to the next machine in the same row or column and never moves diagonally. Empty slots and disconnected machines are skipped.

Keyboard shortcuts:

| Shortcut | Action |
|---|---|
| Ctrl+Alt+F1 … F9, Ctrl+Alt+1 … 9 | Switch to slot 1–9 |
| Win+Ctrl+Alt+E | Turn Easy Mouse on or off |
| Win+Ctrl+Alt+L | Lock all machines (press twice) |
| Win+Ctrl+Alt+R | Reconnect |

The settings window can't change these shortcuts. They come from `settings.json` (see [Files and folders](#files-and-folders)).

## Service mode and UAC

By default, MWB-X9 runs in service mode. The Start Menu and sign-in shortcuts run `PowerToys.MouseWithoutBorders.exe UseService`, which starts `MWBX9.Service`. The service starts two copies of MWB as SYSTEM, one on the user's desktop and one on the secure desktop, then stops. That lets you control UAC prompts, the lock screen and the Ctrl+Alt+Del screen from another machine.

- The service uses the upstream PowerToys permissions. The target user, administrators, SYSTEM and the legacy Power Users group can start it. Other interactive users and Authenticated Users receive no start or stop rights. For another user, run the install script again with `-User`.
- The service starts only after that user signs in and the shortcut runs. Before the first sign-in after a restart, MWB-X9 isn't running.
- If the service can't start, MWB-X9 shows a tooltip and runs without service mode.
- With `-NoService`, MWB-X9 runs as the signed-in user and can't control UAC prompts or the lock screen.
- In service mode, the security key is stored encrypted for that user. It can't be copied to another laptop, and it doesn't carry over when you switch between service and non-service mode. After a switch, open **Settings** from the tray icon, enter the shared key in **Security Key**, and select **Apply**.
- The policy `MwbAllowServiceMode` (under `HKLM\SOFTWARE\Policies\PowerToys`) can block service mode, and `MwbUseOriginalUserInterface` can block the settings window. PowerToys policies apply to MWB-X9 too.

## Uninstall

On each laptop, open PowerShell as an administrator and run:

```powershell
powershell -ExecutionPolicy Bypass -File 'C:\Program Files\MWB-X9\Uninstall-MWBX9.ps1' -RemoveData
```

The script stops MWB-X9, deletes `MWBX9.Service`, the `MWB-X9` firewall rule, the shortcuts and the install folder.

With `-RemoveData` it also deletes, for every user and for the SYSTEM account, the settings and logs folder `AppData\Local\Microsoft\PowerToys\MWB-X9`. When PowerToys isn't installed, it also removes the `MouseWithoutBorders` event log sources. Without `-RemoveData`, the settings stay, so a reinstall keeps the pairing and layout.

Uninstall deletes an install folder only when it has the `.mwb-x9-install` marker. It refuses to delete through links or junctions. Remove those links manually before retrying.

To check that nothing is left:

```powershell
sc.exe query MWBX9.Service                      # "The specified service does not exist"
Get-NetFirewallRule -Name MWB-X9                # no rule found
Test-Path 'C:\Program Files\MWB-X9'             # False
```

## Files and folders

| Path | Contents |
|---|---|
| `C:\Program Files\MWB-X9` | The app, the service, the clipboard helper and the .NET runtime. |
| `%LOCALAPPDATA%\Microsoft\PowerToys\MWB-X9\settings.json` | Settings, layout and security key for the user. |
| `%LOCALAPPDATA%\Microsoft\PowerToys\MWB-X9\Logs` | Logs for the user. |
| `C:\Windows\System32\config\systemprofile\AppData\Local\Microsoft\PowerToys\MWB-X9\Logs` | Logs of the service-mode copies. |
| `Desktop\PowerToys.MouseWithoutBorders` and `Desktop\MouseWithoutBorders` | Screen captures and files received by drag-and-drop. These are your files; uninstall doesn't delete them. |

PowerToys keeps its own Mouse Without Borders settings in `%LOCALAPPDATA%\Microsoft\PowerToys\MouseWithoutBorders`. MWB-X9 doesn't use or change them.

// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;

using Microsoft.PowerToys.Telemetry;
using MouseWithoutBorders.Class;

// <summary>
//     Machine setup/switching implementation.
// </summary>
// <history>
//     2008 created by Truong Do (ductdo).
//     2009-... modified by Truong Do (TruongDo).
//     2023- Included in PowerToys.
// </history>
namespace MouseWithoutBorders.Core;

internal static class MachineStuff
{
    private static readonly Lock McMatrixLock = new();

    internal const byte MAX_MACHINE = 9;

    // Grid width when the matrix is not a single row. Rows = MAX_MACHINE / MATRIX_COLUMNS.
    internal const byte MATRIX_COLUMNS = 3;
    internal const long HEARTBEAT_TIMEOUT = 1500000; // 30 Mins
    private const int SKIP_PIXELS = 1;
    private const int JUMP_PIXELS = 2;

#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter
    internal static ID desMachineID;
#pragma warning restore SA1307
#pragma warning disable SA1306 // Field should begin with a lower-case letter
    internal static string DesMachineName = string.Empty;
#pragma warning restore SA1306
#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter
    internal static ID newDesMachineID;
    internal static ID newDesMachineIdEx;
    internal static ID dropMachineID;
    internal static long lastJump = Common.GetTick();
    internal static MyRectangle desktopBounds = new();
    internal static MyRectangle primaryScreenBounds = new();
#pragma warning restore SA1307
    private static MachinePool _machinePool;

    internal static MachinePool MachinePool
    {
        get
        {
            _machinePool ??= new MachinePool();
            return _machinePool;
        }
    }

    internal static MyRectangle PrimaryScreenBounds => MachineStuff.primaryScreenBounds;

#pragma warning disable SA1306 // Field should begin with a lower-case letter
    internal static MouseLocation SwitchLocation = new();
#pragma warning restore SA1306

    internal static ID NewDesMachineID
    {
        get => MachineStuff.newDesMachineID;
        set => MachineStuff.newDesMachineID = value;
    }

    internal static MyRectangle DesktopBounds => MachineStuff.desktopBounds;

#if OLD_VERSION
    static bool MoveToMyNeighbourIfNeeded(int x, int y)
    {
        if (Math.Abs(x) > 10) LastX = x;
        if (Math.Abs(y) > 10) LastY = y;
        if (GetTick() - lastJump < 500 || desMachineID == IP.ALL) return false;
        if (desMachineID == machineID)
        {
            if (x < desktopBounds.Left + skipPixels) return MoveLeft(x, y, x - desktopBounds.Left, 0);
        }
        else
        {
            if (x < primaryScreenBounds.Left + skipPixels)
            {
                if (MoveLeft(x, y, x - primaryScreenBounds.Left, 0))
                {
                    return true;
                }
                else
                {
                    if (desktopBounds.Left < primaryScreenBounds.Left)
                    {
                        RequestedX_Ex = primaryScreenBounds.Left;
                        RequestedY_Ex = y;
                        return true;
                    }
                }
            }
        }

        if (desMachineID == machineID)
        {
            if (x > desktopBounds.Right - skipPixels) return MoveRight(x, y, x - desktopBounds.Right, 0);
        }
        else
        {
            if (x > primaryScreenBounds.Right - skipPixels)
            {
                if (MoveRight(x, y, x - primaryScreenBounds.Right, 0))
                {
                    return true;
                }
                else
                {
                    if (desktopBounds.Right > primaryScreenBounds.Right)
                    {
                        RequestedX_Ex = primaryScreenBounds.Right;
                        RequestedY_Ex = y;
                        return true;
                    }
                }
            }
        }

        if (desMachineID == machineID)
        {
            if (y < desktopBounds.Top + skipPixels) return MoveUp(x, y, 0, y - desktopBounds.Top);
        }
        else
        {
            if (y < primaryScreenBounds.Top + skipPixels)
            {
                if (MoveUp(x, y, 0, y - primaryScreenBounds.Top))
                {
                    return true;
                }
                else
                {
                    if (desktopBounds.Top < primaryScreenBounds.Top)
                    {
                        RequestedX_Ex = x;
                        RequestedY_Ex = primaryScreenBounds.Top;
                        return true;
                    }
                }
            }
        }

        if (desMachineID == machineID)
        {
            if (y > desktopBounds.Bottom - skipPixels) return MoveDown(x, y, 0, y - desktopBounds.Bottom);
        }
        else
        {
            if (y > primaryScreenBounds.Bottom - skipPixels)
            {
                if (MoveDown(x, y, 0, y - primaryScreenBounds.Bottom))
                {
                    return true;
                }
                else
                {
                    if (desktopBounds.Bottom > primaryScreenBounds.Bottom)
                    {
                        RequestedX_Ex = x;
                        RequestedY_Ex = primaryScreenBounds.Bottom;
                        return true;
                    }
                }
            }
        }

        return false;
    }
#else

    private static Point ConvertToUniversalValue(Point p, MyRectangle r)
    {
        if (!p.IsEmpty)
        {
            p.X = (p.X - r.Left) * 65535 / (r.Right - r.Left);
            p.Y = (p.Y - r.Top) * 65535 / (r.Bottom - r.Top);
        }

        return p;
    }

    /* Let's say we have 3 machines A, B, and C. A is the controller machine.
     * (x, y) is the current Mouse position in pixel.
     * If Setting.Values.MoveMouseRelatively then (x, y) can be from any machine having the value bounded by desktopBounds (can be negative)
     * Else (x, y) is from the controller machine which is bounded by ONLY primaryScreenBounds (>=0);
     *
     * The return point is from 0 to 65535 which is then mapped to the desktop of the new controlled machine by the SendInput method.
     *  Let's say user is switching from machine B to machine C:
     *      If Setting.Values.MoveMouseRelatively the this method is called by B and the return point is calculated by B and sent back to A, A will use it to move Mouse to the right position when switching to C.
     *      Else this method is called by A and the return point is calculated by A.
     * */

    internal static Point MoveToMyNeighbourIfNeeded(int x, int y, ID desMachineID)
    {
        newDesMachineIdEx = desMachineID;

        if (Math.Abs(x) > 10)
        {
            Common.LastX = x;
        }

        if (Math.Abs(y) > 10)
        {
            Common.LastY = y;
        }

        if ((Common.GetTick() - lastJump < 100) || desMachineID == ID.ALL)
        {
            return Point.Empty;
        }

        if (Setting.Values.BlockMouseAtCorners)
        {
            lock (WinAPI.SensitivePoints)
            {
                foreach (Point p in WinAPI.SensitivePoints)
                {
                    if (Math.Abs(p.X - x) < 100 && Math.Abs(p.Y - y) < 100)
                    {
                        return Point.Empty;
                    }
                }
            }
        }

        /* If Mouse is moving in the controller machine and this method is called by the controller machine.
         * Or if Mouse is moving in the controlled machine and this method is called by the controlled machine and Setting.Values.MoveMouseRelative.
         * */
        if (desMachineID == Common.MachineID)
        {
            if (x < desktopBounds.Left + SKIP_PIXELS)
            {
                return MoveLeft(x, y);
            }
            else if (x >= desktopBounds.Right - SKIP_PIXELS)
            {
                return MoveRight(x, y);
            }
            else if (y < desktopBounds.Top + SKIP_PIXELS)
            {
                return MoveUp(x, y);
            }
            else if (y >= desktopBounds.Bottom - SKIP_PIXELS)
            {
                return MoveDown(x, y);
            }
        }

        /* If Mouse is moving in the controlled machine and this method is called by the controller machine and !Setting.Values.MoveMouseRelative.
         * Mouse location is scaled from the primary screen bound of the controller machine regardless of how many monitors the controlled machine may have.
         * */
        else
        {
            if (x < primaryScreenBounds.Left + SKIP_PIXELS)
            {
                return MoveLeft(x, y);
            }
            else if (x >= primaryScreenBounds.Right - SKIP_PIXELS)
            {
                return MoveRight(x, y);
            }
            else if (y < primaryScreenBounds.Top + SKIP_PIXELS)
            {
                return MoveUp(x, y);
            }
            else if (y >= primaryScreenBounds.Bottom - SKIP_PIXELS)
            {
                return MoveDown(x, y);
            }
        }

        return Point.Empty;
    }

#endif

    // Sets newDesMachineIdEx to the nearest live machine next to the current one in the matrix.
    // Leaves it unchanged when there is none, so the caller does not switch.
    private static void SelectNeighbour(MatrixDirection direction)
    {
        string currentMachine = NameFromID(desMachineID);
        if (currentMachine == null)
        {
            return;
        }

        string[] matrix = MachineMatrix;
        int current = Array.FindIndex(matrix, m => currentMachine.Trim().Equals(m?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (current < 0)
        {
            return;
        }

        ID targetId = ID.NONE;
        int target = GridNavigator.Next(
            matrix.Length,
            MatrixLayout.Columns(matrix.Length, Setting.Values.MatrixOneRow, MATRIX_COLUMNS),
            current,
            direction,
            Setting.Values.MatrixCircle,
            i => !string.IsNullOrEmpty(matrix[i]) && (targetId = IdFromName(matrix[i])) != ID.NONE && Common.IsConnectedTo(targetId));

        if (target >= 0)
        {
            newDesMachineIdEx = targetId;
        }
    }

    private static Point MoveRight(int x, int y)
    {
        SelectNeighbour(MatrixDirection.Right);

        // THIS LOGIC IS THE SAME FOR Move*(int x, int y) METHODS.
        if (newDesMachineIdEx != desMachineID)
        {
            Logger.LogDebug("Move Right");

            if (!Setting.Values.MoveMouseRelatively)
            {
                if (newDesMachineIdEx == Common.MachineID)
                {
                    /* Switching back to the controller machine, we need to scale up to the desktopBounds from primaryScreenBounds (sine !Setting.Values.MoveMouseRelatively).
                     * primaryScreenBounds => 65535 => desktopBounds, so that the Mouse position is mapped to the right position when the controller machine has multiple monitors.
                     * */
                    return ConvertToUniversalValue(new Point(primaryScreenBounds.Left + JUMP_PIXELS, y), primaryScreenBounds);
                }
                else
                {
                    if (desMachineID == Common.MachineID)
                    {
                        /* Switching FROM the controller machine, since Mouse was not bounded/locked to the primary screen,
                         * Mouse position can just be mapped from desktopBounds to desktopBounds
                         * desktopBounds => 65535 => desktopBounds.
                         * */
                        return ConvertToUniversalValue(new Point(desktopBounds.Left + JUMP_PIXELS, y), desktopBounds);
                    }
                    else
                    {
                        /* Switching between two machines where non of them is the controller machine.
                         * Since the current Mouse position is "mapped" from the primary monitor of the controller machine,
                         * new Mouse position for the new controlled machine needs to be calculated from this as well.
                         * primaryScreenBounds => 65535 => desktopBounds
                         * */
                        return ConvertToUniversalValue(new Point(primaryScreenBounds.Left + JUMP_PIXELS, y), primaryScreenBounds);
                    }
                }
            }
            else
            {
                /* In the case where Mouse is moved relatively, Mouse position is simply mapped from desktopBounds to desktopBounds.
                 * desktopBounds => 65535 => desktopBounds.
                 * */
                return ConvertToUniversalValue(new Point(desktopBounds.Left + JUMP_PIXELS, y), desktopBounds);
            }
        }

        return Point.Empty;
    }

    private static Point MoveLeft(int x, int y)
    {
        SelectNeighbour(MatrixDirection.Left);

        if (newDesMachineIdEx != desMachineID)
        {
            Logger.LogDebug("Move Left");

            return !Setting.Values.MoveMouseRelatively
                ? newDesMachineIdEx == Common.MachineID
                    ? ConvertToUniversalValue(new Point(primaryScreenBounds.Right - JUMP_PIXELS, y), primaryScreenBounds)
                    : desMachineID == Common.MachineID
                        ? ConvertToUniversalValue(new Point(desktopBounds.Right - JUMP_PIXELS, y), desktopBounds)
                        : ConvertToUniversalValue(new Point(primaryScreenBounds.Right - JUMP_PIXELS, y), primaryScreenBounds)
                : ConvertToUniversalValue(new Point(desktopBounds.Right - JUMP_PIXELS, y), desktopBounds);
        }

        return Point.Empty;
    }

    private static Point MoveUp(int x, int y)
    {
        if (Setting.Values.MatrixOneRow)
        {
            return Point.Empty;
        }

        SelectNeighbour(MatrixDirection.Up);

        if (newDesMachineIdEx != desMachineID)
        {
            Logger.LogDebug("Move Up");

            return !Setting.Values.MoveMouseRelatively
                ? newDesMachineIdEx == Common.MachineID
                    ? ConvertToUniversalValue(new Point(x, primaryScreenBounds.Bottom - JUMP_PIXELS), primaryScreenBounds)
                    : desMachineID == Common.MachineID
                        ? ConvertToUniversalValue(new Point(x, desktopBounds.Bottom - JUMP_PIXELS), desktopBounds)
                        : ConvertToUniversalValue(new Point(x, primaryScreenBounds.Bottom - JUMP_PIXELS), primaryScreenBounds)
                : ConvertToUniversalValue(new Point(x, desktopBounds.Bottom - JUMP_PIXELS), desktopBounds);
        }

        return Point.Empty;
    }

    private static Point MoveDown(int x, int y)
    {
        if (Setting.Values.MatrixOneRow)
        {
            return Point.Empty;
        }

        SelectNeighbour(MatrixDirection.Down);

        if (newDesMachineIdEx != desMachineID)
        {
            Logger.LogDebug("Move Down");

            return !Setting.Values.MoveMouseRelatively
                ? newDesMachineIdEx == Common.MachineID
                    ? ConvertToUniversalValue(new Point(x, primaryScreenBounds.Top + JUMP_PIXELS), primaryScreenBounds)
                    : desMachineID == Common.MachineID
                        ? ConvertToUniversalValue(new Point(x, desktopBounds.Top + JUMP_PIXELS), desktopBounds)
                        : ConvertToUniversalValue(new Point(x, primaryScreenBounds.Top + JUMP_PIXELS), primaryScreenBounds)
                : ConvertToUniversalValue(new Point(x, desktopBounds.Top + JUMP_PIXELS), desktopBounds);
        }

        return Point.Empty;
    }

    internal static bool RemoveDeadMachines(ID ip)
    {
        bool rv = false;

        // Here we are removing a dead machine by IP.
        foreach (MachineInf inf in MachineStuff.MachinePool.ListAllMachines())
        {
            if (inf.Id == ip)
            {
                if (MachinePool.SetMachineDisconnected(inf.Name))
                {
                    rv = true;
                }

                Logger.LogDebug("<><><><><>>><><><<><><><><><><><><><><>><><><><><><><><><><><" + inf.Name);
            }
        }

        return rv;
    }

    internal static void RemoveDeadMachines()
    {
        // list of live/dead machines is now automatically up-to-date
        // if it changed we need to update the UI.
        // for now assume it changed.
        // Common.MachinePool.ResetIPAddressesForDeadMachines();
        // DoSomethingInUIThread(UpdateMenu);
        MachineStuff.UpdateMachinePoolStringSetting();

        // Make sure MachinePool still holds this machine.
        if (MachineStuff.MachinePool.LearnMachine(Common.MachineName))
        {
            _ = MachineStuff.MachinePool.TryUpdateMachineID(Common.MachineName, Common.MachineID, false);
        }
    }

    internal static string AddToMachinePool(DATA package)
    {
        // Log("********** AddToMachinePool called: " + package.src.ToString(CultureInfo.InvariantCulture));

        // There should be no duplicates in machine pool.
        string name = package.MachineName;

        // a few things happening here:
        // 1) find a matching machine (by name)
        // 2) update its ID and time
        // 3) logging
        // 4) updating some variables - desMachineID/newDesMachineID
        // 5) return the matched name (trimmed) - only in the event of a match
        if (MachineStuff.MachinePool.TryFindMachineByName(name, out MachineInf machineInfo))
        {
            _ = MachineStuff.MachinePool.TryUpdateMachineID(machineInfo.Name, machineInfo.Id, true);

            _ = MachineStuff.MachinePool.TryUpdateMachineID(machineInfo.Name, package.Src, true);

            if (machineInfo.Name.Equals(DesMachineName, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogDebug("AddToMachinePool: Des ID updated: " + Common.DesMachineID.ToString() + "/" + package.Src.ToString());
                newDesMachineID = desMachineID = package.Src;
            }

            return machineInfo.Name;
        }
        else
        {
            if (MachineStuff.MachinePool.LearnMachine(name))
            {
                _ = MachineStuff.MachinePool.TryUpdateMachineID(name, package.Src, true);
            }
            else
            {
                Logger.LogDebug("AddToMachinePool: could not add a new machine: " + name);
                return string.Empty;
            }
        }

        // if (machineCount != saved)
        {
            // DoSomethingInUIThread(UpdateMenu);
            MachineStuff.UpdateMachinePoolStringSetting();
        }

        if (package.Src != ID.NONE && package.Src != ID.ALL)
        {
            _ = EnsureMachineInMatrix(name);
            SendMachineMatrix(true);
        }

        Logger.LogDebug("Machine added: " + name + "/" + package.Src.ToString());
        UpdateClientSockets("AddToMachinePool");
        return name;
    }

    /// <summary>
    /// Starts a layout with the machine the user typed, by computer name or IP address, and this machine.
    /// An address stays in the layout only until the machine there says its name (see <see cref="ReplaceAddressWithName"/>);
    /// the machine pool never holds it.
    /// </summary>
    internal static void StartLayoutWith(string otherMachine)
    {
        string other = PeerAddress.TryParseLiteral(otherMachine, out IPAddress address)
            ? address.ToString()
            : otherMachine.Trim().ToUpper(CultureInfo.CurrentCulture);

        MachineMatrix = MatrixLayout.Create(MAX_MACHINE, other, Common.MachineName.Trim());
        MachinePool.Initialize(MachineMatrix.Where(m => !PeerAddress.TryParseLiteral(m, out _)));
        UpdateMachinePoolStringSetting();
    }

    /// <summary>
    /// The machine at an address the user typed has proved its identity with the shared key and claimed <paramref name="machineName"/>.
    /// Puts that name in the layout and the pool instead of the address, and remembers the address for the next connection.
    /// </summary>
    internal static void ReplaceAddressWithName(string typedAddress, string machineName, IPAddress address)
    {
        if (string.IsNullOrWhiteSpace(machineName))
        {
            return;
        }

        machineName = machineName.Trim();
        lock (StagedMatrixLock)
        {
            string[] matrix = (string[])MachineMatrix.Clone();
            if (MatrixLayout.ReplaceName(matrix, typedAddress, machineName))
            {
                MachineMatrix = matrix;
            }
        }

        if (MachinePool.LearnMachine(machineName))
        {
            UpdateMachinePoolStringSetting();
        }

        _ = LearnAddresses(new[] { (machineName, address) });
    }

    internal static bool EnsureMachineInMatrix(string name)
    {
        lock (StagedMatrixLock)
        {
            string[] matrix = (string[])MachineMatrix.Clone();
            bool changed = MatrixLayout.PlaceInFirstEmptySlot(matrix, Common.MachineName);
            changed |= MatrixLayout.PlaceInFirstEmptySlot(matrix, name);
            if (changed)
            {
                MachineMatrix = matrix;
            }

            return changed;
        }
    }

    internal static void UpdateClientSockets(string logHeader)
    {
        Logger.LogDebug("UpdateClientSockets: " + logHeader);
        Common.Sk?.UpdateTCPClients();
    }

    private static SettingsForm settings;

    internal static SettingsForm Settings
    {
        get => MachineStuff.settings;
        set => MachineStuff.settings = value;
    }

    internal static void ShowSetupForm(bool reopenSockets = false)
    {
        Logger.LogDebug("========== BEGIN THE SETUP EXPERIENCE ==========", true);
        var key = Encryption.CreateRandomKey();
        Setting.Values.MyKey = key;
        Encryption.MyKey = key;
        Encryption.GeneratedKey = true;

        if (Process.GetCurrentProcess().SessionId != NativeMethods.WTSGetActiveConsoleSessionId())
        {
            Logger.Log("Not physical console session.");
            _ = MessageBox.Show(
                "Please run the program in the physical console session.\r\nThe program does not work in a remote desktop or virtual machine session.",
                Application.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Stop);
            return;
        }

        if (settings == null)
        {
            settings = new SettingsForm();
            settings.Show();
        }
        else
        {
            settings.Close();
            Common.MMSleep(0.3);
            settings = new SettingsForm();
            settings.Show();
        }

        if (reopenSockets)
        {
            Common.ReopenSockets(true);
        }
    }

    internal static void CloseSetupForm()
    {
        if (settings != null)
        {
            settings.Close();
            settings = null;
        }
    }

    internal static void ShowMachineMatrix()
    {
        if (!Setting.Values.ShowOriginalUI)
        {
            return;
        }

        if (Process.GetCurrentProcess().SessionId != NativeMethods.WTSGetActiveConsoleSessionId())
        {
            Common.ShowToolTip(Application.ProductName + " cannot be used in a remote desktop or virtual machine session.", 5000);
        }

#if NEW_SETTINGS_FORM
        Common.ShowSetupForm();
#else
        if (Setting.Values.FirstRun && !Setting.Values.IsPairingKeyModeMismatch && !Common.AtLeastOneSocketConnected())
        {
            MachineStuff.ShowSetupForm();
        }
        else
        {
            PowerToysTelemetry.Log.WriteEvent(new MouseWithoutBorders.Telemetry.MouseWithoutBordersOldUIOpenedEvent());

            if (Common.MatrixForm == null)
            {
                Common.MatrixForm = new FrmMatrix();
                Common.MatrixForm.Show();

                if (Common.MainForm != null)
                {
                    Common.MainForm.NotifyIcon.Visible = false;
                    Common.MainForm.NotifyIcon.Visible = Setting.Values.ShowOriginalUI;
                }
            }
            else
            {
                Common.MatrixForm.WindowState = FormWindowState.Normal;
                Common.MatrixForm.Activate();
            }
        }
#endif
    }

    private static string[] mcMatrix;

    internal static string[] MachineMatrix
    {
        get
        {
            lock (McMatrixLock)
            {
                if (mcMatrix == null)
                {
                    string s = Setting.Values.MachineMatrixString;
                    string[] stored = string.IsNullOrEmpty(s) ? null : s.Split(',');

                    // Migrates legacy or mis-sized layouts in memory only. Saving here could deadlock because this
                    // getter runs under locks; the next MachineMatrix set persists the normalized layout.
                    mcMatrix = MatrixLayout.Normalize(stored, MAX_MACHINE, !Setting.Values.MatrixOneRow, MATRIX_COLUMNS);

                    if (stored != null && !stored.SequenceEqual(mcMatrix))
                    {
                        Logger.LogDebug($"MachineMatrix normalized from [{s}] to [{string.Join(",", mcMatrix)}]");
                    }
                }

                return mcMatrix;
            }
        }

        set
        {
            lock (McMatrixLock)
            {
                if (value == null)
                {
                    mcMatrix = null; // Force read from registry next time.
                    return;
                }
                else
                {
                    Setting.Values.MachineMatrixString = string.Join(",", mcMatrix = value);
                }
            }

            Common.DoSomethingInUIThread(() =>
            {
                Common.MainForm.ChangeIcon(-1);
                Common.MainForm.UpdateNotifyIcon();
            });
        }
    }

    internal static void UpdateMachinePoolStringSetting()
    {
        Setting.Values.MachinePoolString = MachineStuff.MachinePool.SerializedAsString();
    }

    private static readonly Lock SendMatrixLock = new();

    internal static void SendMachineMatrix() => SendMachineMatrix(false);

    private static void SendMachineMatrix(bool joining)
    {
        lock (SendMatrixLock)
        {
            if (MachineMatrix == null)
            {
                return;
            }

            DATA package = new()
            {
                Type = PackageType.Matrix
                    | (joining ? PackageType.MatrixJoinFlag : 0)
                    | (Setting.Values.MatrixCircle ? PackageType.MatrixSwapFlag : 0)
                    | (Setting.Values.MatrixOneRow ? 0 : PackageType.MatrixTwoRowFlag),
            };
            string[] matrix = MatrixLayout.WithoutAddresses(MachineMatrix);

            for (int i = 0; i < matrix.Length; i++)
            {
                package.MachineName = matrix[i];
                package.MatrixSlotCount = matrix.Length;

                // Tell the others where each machine is, so they can reach it without DNS. Our own slot stays 0:
                // receivers take our address from the connection the packet arrives on.
                package.MatrixSlotIPv4 = string.IsNullOrWhiteSpace(matrix[i]) ? 0 : PeerAddress.ToPacketValue(Common.GetConnectedIPAddressFor(matrix[i]));

                package.Src = (ID)(i + 1);
                package.Des = ID.ALL;

                Common.SkSend(package, null, false);

                Logger.LogDebug($"matrixIncludedMachine sent: [{i + 1}]/[{matrix.Length}]:[{matrix[i]}]");
            }
        }
    }

    private static readonly Lock StagedMatrixLock = new();
    private static long lastIgnoredMatrixLogTime;

    internal static void UpdateMachineMatrix(DATA package, TcpSk tcp)
    {
        uint i = (uint)package.Src;
        int slotCount = package.MatrixSlotCount;
        string matrixIncludedMachine = package.MachineName;

        // Official builds send 4 slots and leave MatrixSlotCount at 0. Applying their layout would overwrite ours.
        if (slotCount != MAX_MACHINE && Common.GetTick() - lastIgnoredMatrixLogTime > 60000)
        {
            lastIgnoredMatrixLogTime = Common.GetTick();
            Logger.Log($"Ignoring machine Matrix package with {slotCount} slots (0 = official build); this build has {MAX_MACHINE}.");
        }

        bool sendMerged = false;
        lock (StagedMatrixLock)
        {
            if (!tcp.MatrixBuffer.TryAdd((int)i, slotCount, (int)package.Type, matrixIncludedMachine, package.MatrixSlotIPv4, out string[] received, out uint[] addresses))
            {
                return;
            }

            List<(string Name, IPAddress Address)> found = new();
            for (int slot = 0; slot < received.Length; slot++)
            {
                found.Add((received[slot], PeerAddress.FromPacketValue(addresses[slot])));
            }

            // The sender leaves its own slot at 0; the connection says where it is.
            found.Add((tcp.MachineName, Common.GetRemoteAddress(tcp.BackingSocket)));
            bool addressesChanged = LearnAddresses(found);

            bool circle = (package.Type & PackageType.MatrixSwapFlag) == PackageType.MatrixSwapFlag;
            bool oneRow = (package.Type & PackageType.MatrixTwoRowFlag) == 0;
            received = MatrixLayout.Normalize(received, MAX_MACHINE, false, MATRIX_COLUMNS);
            string[] current = MachineMatrix;
            bool joining = (package.Type & PackageType.MatrixJoinFlag) != 0;
            if (joining)
            {
                for (int slot = 0; slot < received.Length; slot++)
                {
                    if (PeerAddress.TryParseLiteral(received[slot], out _)
                        || (!current.Contains(received[slot], StringComparer.OrdinalIgnoreCase)
                            && PeerAddress.FromPacketValue(addresses[slot]) == null
                            && !string.Equals(received[slot], tcp.MachineName, StringComparison.OrdinalIgnoreCase)))
                    {
                        received[slot] = string.Empty;
                    }
                }

                if (!MatrixLayout.TryMergeJoin(current, received, out string[] merged))
                {
                    return;
                }

                sendMerged = !MatrixLayout.WithoutAddresses(merged).SequenceEqual(received, StringComparer.OrdinalIgnoreCase);
                received = merged;
            }

            if (Setting.Values.MatrixCircle == circle && Setting.Values.MatrixOneRow == oneRow && received.SequenceEqual(current, StringComparer.OrdinalIgnoreCase))
            {
                // Same layout, but new addresses may reach machines we couldn't connect to.
                if (!addressesChanged && !sendMerged)
                {
                    return;
                }
            }
            else
            {
                ApplyReceivedMatrix(received, circle, oneRow);
            }
        }

        if (sendMerged)
        {
            SendMachineMatrix(true);
        }

        UpdateClientSockets("UpdateMachineMatrix");
    }

    private static void ApplyReceivedMatrix(string[] received, bool circle, bool oneRow)
    {
        InitAndCleanup.ReopenSocketDueToReadError = true;

        Setting.Values.MatrixCircle = circle;
        Setting.Values.MatrixOneRow = oneRow;
        MachineMatrix = received;
        Logger.LogDebug($"MachineMatrix received: [{string.Join(",", received)}]");
        Setting.Values.Changed = true;
    }

    internal static string LearnedAddresses => Setting.Values.LearnedAddresses;

    internal static bool LearnAddresses(IEnumerable<(string Name, IPAddress Address)> found)
    {
        return Setting.Values.LearnAddresses(found.Where(peer => peer.Address != null
            && !string.IsNullOrWhiteSpace(peer.Name) && !PeerAddress.TryParseLiteral(peer.Name, out _)
            && !peer.Name.Trim().Equals(Common.MachineName.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    internal static void SwitchToMachine(string name)
    {
        ID id = MachineStuff.MachinePool.ResolveID(name);

        if (id != ID.NONE)
        {
            // Ask current machine to hide the Mouse cursor
            if (desMachineID != Common.MachineID)
            {
                Common.SendPackage(desMachineID, PackageType.HideMouse);
            }

            NewDesMachineID = Common.DesMachineID = id;
            SwitchLocation.X = Event.XY_BY_PIXEL + primaryScreenBounds.Left + ((primaryScreenBounds.Right - primaryScreenBounds.Left) / 2);
            SwitchLocation.Y = Event.XY_BY_PIXEL + primaryScreenBounds.Top + ((primaryScreenBounds.Bottom - primaryScreenBounds.Top) / 2);
            SwitchLocation.ResetCount();
            Common.UpdateMultipleModeIconAndMenu();
            Common.HideMouseCursor(false);
            _ = Common.EvSwitch.Set();
        }
    }

    internal static void SwitchToMultipleMode(bool multipleMode, bool centerScreen)
    {
        if (multipleMode)
        {
            PowerToysTelemetry.Log.WriteEvent(new MouseWithoutBorders.Telemetry.MouseWithoutBordersMultipleModeEvent());
            NewDesMachineID = Common.DesMachineID = ID.ALL;
        }
        else
        {
            NewDesMachineID = Common.DesMachineID = Common.MachineID;
        }

        if (centerScreen)
        {
            Common.MoveMouseToCenter();
        }

        InitAndCleanup.ReleaseAllKeys();

        Common.UpdateMultipleModeIconAndMenu();
    }

    internal static bool CheckSecondInstance(bool sendMessage = false)
    {
        int h;

        if ((h = NativeMethods.FindWindow(null, Setting.Values.MyID)) > 0)
        {
            return true;
        }

        return false;
    }

#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter
    internal static EventWaitHandle oneInstanceCheck;
#pragma warning restore SA1307

    internal static void AssertOneInstancePerDesktopSession()
    {
        string eventName = $"Global\\{Application.ProductName}-{FrmAbout.AssemblyVersion}-{WinAPI.GetMyDesktop()}-{Common.CurrentProcess.SessionId}";
        oneInstanceCheck = new EventWaitHandle(false, EventResetMode.ManualReset, eventName, out bool created);

        if (!created)
        {
            Logger.TelemetryLogTrace($"Second instance found: {eventName}.", SeverityLevel.Warning, true);
            Common.CurrentProcess.KillProcess(true);
        }
    }

    internal static ID IdFromName(string name)
    {
        return MachineStuff.MachinePool.ResolveID(name);
    }

    internal static string NameFromID(ID id)
    {
        foreach (MachineInf inf in MachineStuff.MachinePool.TryFindMachineByID(id))
        {
            if (!string.IsNullOrEmpty(inf.Name))
            {
                return inf.Name;
            }
        }

        return null;
    }

    internal static bool InMachineMatrix(string name)
    {
        if (MachineMatrix == null || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (string st in MachineMatrix)
        {
            if (!string.IsNullOrWhiteSpace(st) && st.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static void ClearComputerMatrix()
    {
        MachineStuff.MachineMatrix = MatrixLayout.Create(MachineStuff.MAX_MACHINE, Common.MachineName.Trim());
        MachineStuff.MachinePool.Initialize(new string[] { Common.MachineName });
        MachineStuff.UpdateMachinePoolStringSetting();
    }
}

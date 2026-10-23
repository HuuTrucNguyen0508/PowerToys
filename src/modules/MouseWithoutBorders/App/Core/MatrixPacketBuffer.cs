// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace MouseWithoutBorders.Core;

internal sealed class MatrixPacketBuffer(int slotCount)
{
    private string[] slots;
    private uint[] slotAddresses;
    private int nextSlot;
    private int layoutFlags;

    // address is the slot's Matrix packet IPv4 field, kept as received (see PeerAddress).
    internal bool TryAdd(int slot, int senderSlotCount, int flags, string name, uint address, out string[] completed, out uint[] addresses)
    {
        completed = null;
        addresses = null;

        if (senderSlotCount != slotCount || slot < 1 || slot > slotCount)
        {
            slots = null;
            return false;
        }

        if (slot == 1)
        {
            slots = MatrixLayout.CreateEmpty(slotCount);
            slotAddresses = new uint[slotCount];
            nextSlot = 1;
            layoutFlags = flags;
        }

        if (slots == null || slot != nextSlot || flags != layoutFlags)
        {
            slots = null;
            return false;
        }

        slots[slot - 1] = name;
        slotAddresses[slot - 1] = address;
        nextSlot++;

        if (slot != slotCount)
        {
            return false;
        }

        completed = slots;
        addresses = slotAddresses;
        slots = null;
        slotAddresses = null;
        return true;
    }
}

// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace MouseWithoutBorders.Core;

internal sealed class MatrixPacketBuffer(int slotCount)
{
    private string[] slots;
    private int nextSlot;
    private int layoutFlags;

    internal bool TryAdd(int slot, int senderSlotCount, int flags, string name, out string[] completed)
    {
        completed = null;

        if (senderSlotCount != slotCount || slot < 1 || slot > slotCount)
        {
            slots = null;
            return false;
        }

        if (slot == 1)
        {
            slots = MatrixLayout.CreateEmpty(slotCount);
            nextSlot = 1;
            layoutFlags = flags;
        }

        if (slots == null || slot != nextSlot || flags != layoutFlags)
        {
            slots = null;
            return false;
        }

        slots[slot - 1] = name;
        nextSlot++;

        if (slot != slotCount)
        {
            return false;
        }

        completed = slots;
        slots = null;
        return true;
    }
}

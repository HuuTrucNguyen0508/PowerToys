// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

// <summary>
//     Edge navigation across the machine matrix. Pure functions: no settings, sockets or UI.
// </summary>
namespace MouseWithoutBorders.Core;

internal static class GridNavigator
{
    /// <summary>
    /// Finds the slot to switch to when the mouse leaves slot <paramref name="currentIndex"/> in <paramref name="direction"/>.
    /// Scans the same row (Left/Right) or column (Up/Down), nearest first, skipping slots where
    /// <paramref name="isEligible"/> is false. It never moves diagonally or into another row or column.
    /// With <paramref name="wrap"/>, the scan continues from the far end of that row or column back toward the current slot.
    /// </summary>
    /// <param name="slotCount">Number of slots, laid out row-major. The last row may be partial.</param>
    /// <param name="columns">Slots per row. Equal to <paramref name="slotCount"/> for a single row.</param>
    /// <returns>The target slot index, or -1 when there is no eligible slot. Never <paramref name="currentIndex"/>.</returns>
    internal static int Next(int slotCount, int columns, int currentIndex, MatrixDirection direction, bool wrap, Func<int, bool> isEligible)
    {
        ArgumentNullException.ThrowIfNull(isEligible);

        if (slotCount <= 0 || columns <= 0 || currentIndex < 0 || currentIndex >= slotCount)
        {
            return -1;
        }

        columns = Math.Min(columns, slotCount);
        int row = currentIndex / columns;
        int column = currentIndex % columns;

        // Slot indices along the axis of movement, and the current slot's position among them.
        int[] line;
        int position;

        switch (direction)
        {
            case MatrixDirection.Left:
            case MatrixDirection.Right:
                line = new int[Math.Min(columns, slotCount - (row * columns))];
                for (int c = 0; c < line.Length; c++)
                {
                    line[c] = (row * columns) + c;
                }

                position = column;
                break;

            case MatrixDirection.Up:
            case MatrixDirection.Down:
                line = new int[((slotCount - 1 - column) / columns) + 1];
                for (int r = 0; r < line.Length; r++)
                {
                    line[r] = (r * columns) + column;
                }

                position = row;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
        }

        int step = direction is MatrixDirection.Right or MatrixDirection.Down ? 1 : -1;

        for (int distance = 1; distance < line.Length; distance++)
        {
            int p = position + (step * distance);

            if (p < 0 || p >= line.Length)
            {
                if (!wrap)
                {
                    break;
                }

                p = (p + line.Length) % line.Length;
            }

            if (isEligible(line[p]))
            {
                return line[p];
            }
        }

        return -1;
    }
}

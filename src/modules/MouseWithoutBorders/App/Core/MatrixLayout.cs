// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

// <summary>
//     Machine matrix shape and migration helpers. Pure functions: no settings, sockets or UI.
// </summary>
namespace MouseWithoutBorders.Core;

/// <summary>
/// The machine matrix is a flat, row-major array of machine names, one per slot.
/// An empty string marks an empty slot.
/// </summary>
internal static class MatrixLayout
{
    private const int LegacySlotCount = 4;
    private const int LegacyColumns = 2;

    internal static string[] CreateEmpty(int slotCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slotCount);

        string[] matrix = new string[slotCount];
        Array.Fill(matrix, string.Empty);
        return matrix;
    }

    /// <summary>
    /// Creates a matrix with <paramref name="leading"/> in the first slots and the rest empty.
    /// </summary>
    internal static string[] Create(int slotCount, params string[] leading)
    {
        string[] matrix = CreateEmpty(slotCount);

        if (leading != null)
        {
            if (leading.Length > slotCount)
            {
                throw new ArgumentException($"{leading.Length} names do not fit in {slotCount} slots.", nameof(leading));
            }

            for (int i = 0; i < leading.Length; i++)
            {
                matrix[i] = leading[i] ?? string.Empty;
            }
        }

        return matrix;
    }

    /// <summary>
    /// Converts a stored matrix of any length to exactly <paramref name="slotCount"/> slots.
    /// Names are trimmed, later case-insensitive duplicates are emptied, and slot positions are kept where possible.
    /// A legacy 4-slot two-row (2x2) matrix keeps its coordinates in a grid <paramref name="gridColumns"/> wide.
    /// </summary>
    internal static string[] Normalize(string[] stored, int slotCount, bool legacyTwoRow, int gridColumns)
    {
        string[] matrix = CreateEmpty(slotCount);

        if (stored == null || stored.Length == 0)
        {
            return matrix;
        }

        string[] names = WithoutDuplicates(stored);

        if (names.Length == LegacySlotCount && slotCount > LegacySlotCount && legacyTwoRow
            && gridColumns >= LegacyColumns && gridColumns + LegacyColumns <= slotCount)
        {
            for (int i = 0; i < LegacySlotCount; i++)
            {
                matrix[((i / LegacyColumns) * gridColumns) + (i % LegacyColumns)] = names[i];
            }

            return matrix;
        }

        Array.Copy(names, matrix, Math.Min(names.Length, slotCount));

        // Names beyond the last slot move into the first empty slots, in order, while any are left.
        for (int i = slotCount; i < names.Length; i++)
        {
            if (names[i].Length > 0)
            {
                _ = PlaceInFirstEmptySlot(matrix, names[i]);
            }
        }

        return matrix;
    }

    /// <summary>
    /// Puts <paramref name="name"/> into the first empty slot.
    /// Returns false, without changing the matrix, if the name is blank, already present (case-insensitive) or there is no empty slot.
    /// </summary>
    internal static bool PlaceInFirstEmptySlot(string[] matrix, string name)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        name = name.Trim();
        int firstEmpty = -1;

        for (int i = 0; i < matrix.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(matrix[i]))
            {
                if (firstEmpty < 0)
                {
                    firstEmpty = i;
                }
            }
            else if (matrix[i].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (firstEmpty < 0)
        {
            return false;
        }

        matrix[firstEmpty] = name;
        return true;
    }

    /// <summary>
    /// Replaces every slot holding <paramref name="oldName"/> (case-insensitive) with <paramref name="newName"/>.
    /// If <paramref name="newName"/> is already in another slot, those slots are emptied instead, so the name stays unique.
    /// Returns true if the matrix changed.
    /// </summary>
    internal static bool ReplaceName(string[] matrix, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return false;
        }

        oldName = oldName.Trim();
        newName = newName.Trim();
        if (oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool newNamePresent = Array.Exists(matrix, m => newName.Equals(m?.Trim(), StringComparison.OrdinalIgnoreCase));
        bool changed = false;

        for (int i = 0; i < matrix.Length; i++)
        {
            if (oldName.Equals(matrix[i]?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                matrix[i] = newNamePresent ? string.Empty : newName;
                newNamePresent = true;
                changed = true;
            }
        }

        return changed;
    }

    internal static bool TryMergeJoin(string[] current, string[] incoming, out string[] merged)
    {
        Dictionary<string, int> preferredSlots = new(StringComparer.Ordinal);
        foreach (string[] layout in new[] { current, incoming })
        {
            for (int i = 0; i < layout.Length; i++)
            {
                string name = layout[i]?.Trim().ToUpperInvariant();
                if (!string.IsNullOrEmpty(name) && !PeerAddress.TryParseLiteral(name, out _))
                {
                    preferredSlots[name] = preferredSlots.TryGetValue(name, out int previous) ? Math.Min(previous, i) : i;
                }
            }
        }

        merged = null;
        if (current.Length != incoming.Length || preferredSlots.Count > current.Length)
        {
            return false;
        }

        merged = CreateEmpty(current.Length);
        List<string> displaced = new();
        foreach (var pair in preferredSlots.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (merged[pair.Value].Length == 0)
            {
                merged[pair.Value] = pair.Key;
            }
            else
            {
                displaced.Add(pair.Key);
            }
        }

        foreach (string name in displaced)
        {
            _ = PlaceInFirstEmptySlot(merged, name);
        }

        merged = WithPendingAddresses(current, merged);
        return true;
    }

    internal static string[] WithoutAddresses(string[] matrix) =>
        matrix.Select(name => PeerAddress.TryParseLiteral(name, out _) ? string.Empty : name).ToArray();

    internal static string[] WithPendingAddresses(string[] current, string[] received)
    {
        string[] result = (string[])received.Clone();
        foreach (string address in current.Where(name => PeerAddress.TryParseLiteral(name, out _)))
        {
            _ = PlaceInFirstEmptySlot(result, address);
        }

        return result;
    }

    /// <summary>
    /// Number of columns in the layout: one row of all slots, or a grid <paramref name="gridColumns"/> wide.
    /// </summary>
    internal static int Columns(int slotCount, bool oneRow, int gridColumns)
    {
        return oneRow ? slotCount : Math.Min(gridColumns, slotCount);
    }

    private static string[] WithoutDuplicates(string[] stored)
    {
        string[] names = new string[stored.Length];

        for (int i = 0; i < stored.Length; i++)
        {
            string name = stored[i]?.Trim() ?? string.Empty;

            for (int j = 0; j < i && name.Length > 0; j++)
            {
                if (names[j].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    name = string.Empty;
                }
            }

            names[i] = name;
        }

        return names;
    }
}

// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Net;

// <summary>
//     Reads and updates the Name2IP mapping text. Pure functions: no settings, sockets or UI.
// </summary>
namespace MouseWithoutBorders.Core;

/// <summary>
/// Name2IP is text with one "NAME IP" mapping per line, lines separated by CRLF. Tokens after the IP are ignored when connecting,
/// so a learned mapping carries a trailing <see cref="LearnedTag"/> that tells it apart from lines the user wrote.
/// </summary>
internal static class Name2IpMap
{
    internal const string LearnedTag = "#learned";

    private const string LineSeparator = "\r\n";
    private static readonly char[] Blank = { ' ' };

    /// <summary>
    /// Addresses mapped to <paramref name="name"/> (case-insensitive), in line order, without duplicates. Lines that don't parse are skipped.
    /// </summary>
    internal static List<IPAddress> AddressesFor(string text, string name)
    {
        List<IPAddress> addresses = new();

        if (string.IsNullOrWhiteSpace(name))
        {
            return addresses;
        }

        foreach (string line in Lines(text))
        {
            string[] tokens = line.Split(Blank, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length >= 2 && tokens[0].Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)
                && IPAddress.TryParse(tokens[1].Trim(), out IPAddress address) && !addresses.Contains(address))
            {
                addresses.Add(address);
            }
        }

        return addresses;
    }

    /// <summary>
    /// Records that <paramref name="name"/> was reached at <paramref name="address"/>.
    /// Replaces an earlier learned line for the name in place, or appends one. Lines the user wrote are kept as they are;
    /// if one of them already maps the name to this address, no learned line is added.
    /// Returns the text unchanged for a blank name, a name with spaces, or a null address.
    /// </summary>
    internal static string SetLearned(string text, string name, IPAddress address)
    {
        text ??= string.Empty;
        name = name?.Trim();

        if (string.IsNullOrEmpty(name) || name.Contains(' ', StringComparison.Ordinal) || address == null)
        {
            return text;
        }

        address = PeerAddress.Normalize(address);
        List<string> lines = Lines(text);
        int learnedAt = -1;
        bool userMapsAddress = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string[] tokens = lines[i].Split(Blank, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length < 2 || !tokens[0].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tokens.Length >= 3 && tokens[2].Equals(LearnedTag, StringComparison.OrdinalIgnoreCase))
            {
                learnedAt = learnedAt < 0 ? i : learnedAt;
                lines.RemoveAt(i--);
            }
            else if (IPAddress.TryParse(tokens[1], out IPAddress mapped) && PeerAddress.Normalize(mapped).Equals(address))
            {
                userMapsAddress = true;
            }
        }

        if (!userMapsAddress)
        {
            if (learnedAt < 0)
            {
                // Keep a trailing line break at the end.
                learnedAt = lines.Count > 0 && lines[^1].Length == 0 ? lines.Count - 1 : lines.Count;
            }

            lines.Insert(learnedAt, $"{name} {address} {LearnedTag}");
        }

        return string.Join(LineSeparator, lines);
    }

    // Accepts LF as well as CRLF line breaks; SetLearned writes CRLF.
    private static List<string> Lines(string text)
    {
        List<string> lines = new();

        if (!string.IsNullOrEmpty(text))
        {
            foreach (string line in text.Split('\n'))
            {
                lines.Add(line.TrimEnd('\r'));
            }
        }

        return lines;
    }
}

// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

// <summary>
//     Peer IP address helpers. Pure functions: no settings, sockets or UI.
// </summary>
namespace MouseWithoutBorders.Core;

internal static class PeerAddress
{
    /// <summary>
    /// Parses text the user typed as a machine name. True only for a full IP address:
    /// four dotted decimal parts for IPv4, or a colon-separated IPv6 address.
    /// Shorthand that <see cref="IPAddress.TryParse(string, out IPAddress)"/> also accepts, such as "10.1" or "12345", is not an address here.
    /// </summary>
    internal static bool TryParseLiteral(string text, out IPAddress address)
    {
        address = null;
        text = text?.Trim();

        if (string.IsNullOrEmpty(text) || !IPAddress.TryParse(text, out IPAddress parsed))
        {
            return false;
        }

        bool literal = parsed.AddressFamily switch
        {
            AddressFamily.InterNetwork => text.Split('.').Length == 4,
            AddressFamily.InterNetworkV6 => text.Contains(':'),
            _ => false,
        };

        if (literal)
        {
            address = Normalize(parsed);
        }

        return literal;
    }

    /// <summary>
    /// Returns the IPv4 address for an IPv4-mapped IPv6 address (dual-mode sockets report those), otherwise the address itself.
    /// </summary>
    internal static IPAddress Normalize(IPAddress address)
    {
        return address != null && address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    /// <summary>
    /// Encodes an address for a Matrix packet's IPv4 field. The value's little-endian bytes, which is how the packet stores it,
    /// are the address octets in network order. Returns 0 (unknown) for null, IPv6, and addresses that are not unicast:
    /// 0.x.x.x, loopback, multicast and broadcast.
    /// </summary>
    internal static uint ToPacketValue(IPAddress address)
    {
        address = Normalize(address);

        if (!IsShareableIPv4(address))
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(address.GetAddressBytes());
    }

    /// <summary>
    /// Decodes a Matrix packet's IPv4 field. Returns null for 0 (unknown) and for addresses that are never a peer's.
    /// </summary>
    internal static IPAddress FromPacketValue(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        IPAddress address = new(bytes);

        return IsShareableIPv4(address) ? address : null;
    }

    // Unicast IPv4 a peer could listen on. Link-local 169.254.x.x is kept: it is what a LAN without DHCP uses.
    private static bool IsShareableIPv4(IPAddress address)
    {
        return address?.AddressFamily == AddressFamily.InterNetwork
            && address.GetAddressBytes()[0] is > 0 and < 224 and not 127;
    }
}

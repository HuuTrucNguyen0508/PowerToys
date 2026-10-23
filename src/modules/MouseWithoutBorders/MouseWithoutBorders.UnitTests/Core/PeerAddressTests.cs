// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers.Binary;
using System.Net;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class PeerAddressTests
{
    [DataTestMethod]
    [DataRow("192.168.1.20", "192.168.1.20")]
    [DataRow(" 10.0.0.5 ", "10.0.0.5")]
    [DataRow("169.254.10.2", "169.254.10.2")]
    [DataRow("192.168.001.010", "192.168.1.10")]
    [DataRow("010.000.000.009", "10.0.0.9")]
    [DataRow("fe80::1", "fe80::1")]
    [DataRow("FE80::1%12", "fe80::1%12")]
    [DataRow("2001:db8::7", "2001:db8::7")]
    [DataRow("::ffff:192.168.1.20", "192.168.1.20")]
    public void RecognizesFullAddresses(string text, string expected)
    {
        Assert.IsTrue(PeerAddress.TryParseLiteral(text, out IPAddress address));
        Assert.AreEqual(expected, address.ToString());
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow("LAPTOP-7")]
    [DataRow("PC1")]
    [DataRow("12345")]
    [DataRow("10.1")]
    [DataRow("10.0.1")]
    [DataRow("192.168.1.256")]
    [DataRow("laptop.local")]
    [DataRow("192.168.1.20:15101")]
    [DataRow("0xC0.0xA8.1.20")]
    public void TreatsEverythingElseAsAName(string text)
    {
        Assert.IsFalse(PeerAddress.TryParseLiteral(text, out IPAddress address));
        Assert.IsNull(address);
    }

    [TestMethod]
    public void PacketValueStoresTheOctetsInNetworkOrder()
    {
        uint value = PeerAddress.ToPacketValue(IPAddress.Parse("192.168.1.20"));
        byte[] stored = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(stored, value); // DATA is marshalled little-endian on Windows.
        CollectionAssert.AreEqual(new byte[] { 192, 168, 1, 20 }, stored);
        Assert.AreEqual(0x1401A8C0u, value);
    }

    [DataTestMethod]
    [DataRow("192.168.1.20")]
    [DataRow("10.0.0.1")]
    [DataRow("169.254.0.9")]
    [DataRow("223.255.255.254")]
    public void PacketValueRoundTrips(string text)
    {
        IPAddress address = IPAddress.Parse(text);
        Assert.AreEqual(address, PeerAddress.FromPacketValue(PeerAddress.ToPacketValue(address)));
    }

    [TestMethod]
    public void MappedIPv6EncodesAsIPv4()
    {
        IPAddress mapped = IPAddress.Parse("192.168.1.20").MapToIPv6();
        Assert.AreEqual(PeerAddress.ToPacketValue(IPAddress.Parse("192.168.1.20")), PeerAddress.ToPacketValue(mapped));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("0.0.0.0")]
    [DataRow("0.1.2.3")]
    [DataRow("127.0.0.1")]
    [DataRow("224.0.0.1")]
    [DataRow("255.255.255.255")]
    [DataRow("fe80::1")]
    [DataRow("::1")]
    public void UnshareableAddressesEncodeAsUnknown(string text)
    {
        Assert.AreEqual(0u, PeerAddress.ToPacketValue(text == null ? null : IPAddress.Parse(text)));
    }

    [TestMethod]
    public void ZeroDecodesAsUnknown()
    {
        Assert.IsNull(PeerAddress.FromPacketValue(0));
    }

    [DataTestMethod]
    [DataRow(0x0100007Fu)] // 127.0.0.1
    [DataRow(0xFFFFFFFFu)] // 255.255.255.255
    [DataRow(0x010000E0u)] // 224.0.0.1
    public void UnshareableValuesDecodeAsUnknown(uint value)
    {
        Assert.IsNull(PeerAddress.FromPacketValue(value));
    }
}

// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Net;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class MatrixPacketSerializationTests
{
    [TestMethod]
    public void JoinFlagKeepsTheLegacyMatrixPacketShape()
    {
        DATA sent = new()
        {
            Type = PackageType.Matrix | PackageType.MatrixJoinFlag | PackageType.MatrixTwoRowFlag,
            MatrixSlotCount = 9,
            MachineName = "PEER",
        };
        byte[] bytes = sent.Bytes;
        DATA received = new(bytes);
        Assert.AreEqual(64, bytes.Length);
        Assert.IsTrue(received.IsBigPackage);
        Assert.AreEqual(PackageType.Matrix, received.Type & PackageType.Matrix);
        Assert.AreEqual(PackageType.MatrixJoinFlag, received.Type & PackageType.MatrixJoinFlag);
        Assert.AreEqual(PackageType.MatrixTwoRowFlag, received.Type & PackageType.MatrixTwoRowFlag);
        Assert.AreEqual(0u, received.MatrixSlotIPv4);
        Assert.AreEqual("PEER", received.MachineName);
    }

    [TestMethod]
    public void MatrixSlotCountRoundTripsWithoutOverwritingTheName()
    {
        DATA sent = new()
        {
            Type = PackageType.Matrix | PackageType.MatrixTwoRowFlag | PackageType.MatrixSwapFlag,
            Src = (ID)9,
            Des = ID.ALL,
            MatrixSlotCount = 9,
            MachineName = "NINTH-PC",
        };
        byte[] bytes = sent.Bytes;
        Assert.AreEqual(64, bytes.Length);
        Assert.AreEqual(9, BitConverter.ToInt32(bytes, 16));
        DATA received = new(bytes);
        Assert.AreEqual(sent.Type, received.Type);
        Assert.AreEqual(sent.Src, received.Src);
        Assert.AreEqual(sent.Des, received.Des);
        Assert.AreEqual(sent.MatrixSlotCount, received.MatrixSlotCount);
        Assert.AreEqual(sent.MachineName, received.MachineName);
    }

    [TestMethod]
    public void MatrixSlotAddressRoundTripsInNetworkOrderBetweenCountAndName()
    {
        IPAddress address = IPAddress.Parse("192.168.1.20");
        DATA sent = new()
        {
            Type = PackageType.Matrix | PackageType.MatrixTwoRowFlag,
            Src = (ID)3,
            Des = ID.ALL,
            MatrixSlotCount = 9,
            MatrixSlotIPv4 = PeerAddress.ToPacketValue(address),
            MachineName = "LAPTOP-THREE-WITH-A-LONG-NAME-32",
        };
        byte[] bytes = sent.Bytes;
        Assert.AreEqual(64, bytes.Length);
        Assert.AreEqual(9, BitConverter.ToInt32(bytes, 16));
        CollectionAssert.AreEqual(new byte[] { 192, 168, 1, 20 }, bytes[20..24]);
        CollectionAssert.AreEqual(new byte[8], bytes[24..32]);
        DATA received = new(bytes);
        Assert.AreEqual(9, received.MatrixSlotCount);
        Assert.AreEqual(address, PeerAddress.FromPacketValue(received.MatrixSlotIPv4));
        Assert.AreEqual(sent.MachineName, received.MachineName);
    }

    [TestMethod]
    public void MatrixSlotAddressDefaultsToUnknown()
    {
        DATA received = new(new DATA { Type = PackageType.Matrix, MatrixSlotCount = 9, MachineName = "A" }.Bytes);
        Assert.AreEqual(0u, received.MatrixSlotIPv4);
        Assert.IsNull(PeerAddress.FromPacketValue(received.MatrixSlotIPv4));
    }
}

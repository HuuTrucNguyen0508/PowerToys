// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class MatrixPacketSerializationTests
{
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
}

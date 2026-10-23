// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class MatrixPacketBufferTests
{
    [DataTestMethod]
    [DataRow(9)]
    [DataRow(12)]
    public void PublishesOnlyACompleteLayoutIncludingEmptySlots(int count)
    {
        MatrixPacketBuffer buffer = new(count);
        string[] expected = new string[count];
        for (int slot = 1; slot <= count; slot++)
        {
            expected[slot - 1] = slot % 2 == 0 ? string.Empty : $"PC{slot}";
            bool ready = buffer.TryAdd(slot, count, 128, expected[slot - 1], out string[] completed);
            Assert.AreEqual(slot == count, ready);
            if (ready)
            {
                CollectionAssert.AreEqual(expected, completed);
            }
            else
            {
                Assert.IsNull(completed);
            }
        }
    }

    [TestMethod]
    public void InterleavedConnectionsKeepTheirOwnLayouts()
    {
        MatrixPacketBuffer first = new(3);
        MatrixPacketBuffer second = new(3);
        Assert.IsFalse(first.TryAdd(1, 3, 128, "A", out _));
        Assert.IsFalse(second.TryAdd(1, 3, 132, "X", out _));
        Assert.IsFalse(second.TryAdd(2, 3, 132, "Y", out _));
        Assert.IsFalse(first.TryAdd(2, 3, 128, "B", out _));
        Assert.IsTrue(first.TryAdd(3, 3, 128, "C", out string[] a));
        Assert.IsTrue(second.TryAdd(3, 3, 132, "Z", out string[] b));
        string[] expectedA = ["A", "B", "C"];
        string[] expectedB = ["X", "Y", "Z"];
        CollectionAssert.AreEqual(expectedA, a);
        CollectionAssert.AreEqual(expectedB, b);
    }

    [TestMethod]
    public void MissingMiddleSlotDoesNotPublishAPartialLayout()
    {
        MatrixPacketBuffer buffer = new(3);
        Assert.IsFalse(buffer.TryAdd(1, 3, 128, "A", out _));
        Assert.IsFalse(buffer.TryAdd(3, 3, 128, "C", out string[] completed));
        Assert.IsNull(completed);
        Assert.IsFalse(buffer.TryAdd(2, 3, 128, "B", out _));
        Assert.IsFalse(buffer.TryAdd(3, 3, 128, "C", out _));
    }

    [TestMethod]
    public void FirstSlotRestartsAnIncompleteLayout()
    {
        MatrixPacketBuffer buffer = new(2);
        Assert.IsFalse(buffer.TryAdd(1, 2, 128, "old", out _));
        Assert.IsFalse(buffer.TryAdd(1, 2, 132, "new", out _));
        Assert.IsTrue(buffer.TryAdd(2, 2, 132, "peer", out string[] completed));
        string[] expected = ["new", "peer"];
        CollectionAssert.AreEqual(expected, completed);
        Assert.IsFalse(buffer.TryAdd(2, 2, 132, "peer", out _));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(4)]
    [DataRow(12)]
    public void IncompatibleSenderDiscardsThePendingLayout(int senderCount)
    {
        MatrixPacketBuffer buffer = new(9);
        Assert.IsFalse(buffer.TryAdd(1, 9, 128, "A", out _));
        Assert.IsFalse(buffer.TryAdd(2, senderCount, 128, "B", out _));
        for (int slot = 2; slot <= 9; slot++)
        {
            Assert.IsFalse(buffer.TryAdd(slot, 9, 128, "C", out _));
        }
    }

    [TestMethod]
    public void LayoutFlagsCannotChangeDuringASequence()
    {
        MatrixPacketBuffer buffer = new(2);
        Assert.IsFalse(buffer.TryAdd(1, 2, 128, "A", out _));
        Assert.IsFalse(buffer.TryAdd(2, 2, 132, "B", out _));
    }

    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(10)]
    public void InvalidSlotDiscardsThePendingLayout(int slot)
    {
        MatrixPacketBuffer buffer = new(9);
        Assert.IsFalse(buffer.TryAdd(1, 9, 128, "A", out _));
        Assert.IsFalse(buffer.TryAdd(slot, 9, 128, "B", out _));
        for (int next = 2; next <= 9; next++)
        {
            Assert.IsFalse(buffer.TryAdd(next, 9, 128, "C", out _));
        }
    }
}

// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class GridNavigatorTests
{
    // 3x3 slot indices:
    //   0 1 2
    //   3 4 5
    //   6 7 8
    private const int Slots = 9;
    private const int Columns = 3;

    private static readonly MatrixDirection[] Directions = [MatrixDirection.Left, MatrixDirection.Right, MatrixDirection.Up, MatrixDirection.Down];

    [DataTestMethod]
    [DataRow("Left", false, new[] { -1, 0, 1, -1, 3, 4, -1, 6, 7 })]
    [DataRow("Right", false, new[] { 1, 2, -1, 4, 5, -1, 7, 8, -1 })]
    [DataRow("Up", false, new[] { -1, -1, -1, 0, 1, 2, 3, 4, 5 })]
    [DataRow("Down", false, new[] { 3, 4, 5, 6, 7, 8, -1, -1, -1 })]
    [DataRow("Left", true, new[] { 2, 0, 1, 5, 3, 4, 8, 6, 7 })]
    [DataRow("Right", true, new[] { 1, 2, 0, 4, 5, 3, 7, 8, 6 })]
    [DataRow("Up", true, new[] { 6, 7, 8, 0, 1, 2, 3, 4, 5 })]
    [DataRow("Down", true, new[] { 3, 4, 5, 6, 7, 8, 0, 1, 2 })]
    public void FullGridEveryCellEveryDirection(string directionName, bool wrap, int[] expectedFromEachCell)
    {
        MatrixDirection direction = Enum.Parse<MatrixDirection>(directionName);

        for (int cell = 0; cell < Slots; cell++)
        {
            Assert.AreEqual(expectedFromEachCell[cell], GridNavigator.Next(Slots, Columns, cell, direction, wrap, _ => true), $"from {cell}");
        }
    }

    [DataTestMethod]
    [DataRow(5, "Left", false, 3)]
    [DataRow(3, "Right", false, 5)]
    [DataRow(1, "Down", false, 7)]
    [DataRow(7, "Up", false, 1)]
    [DataRow(3, "Left", true, 5)]
    [DataRow(5, "Right", true, 3)]
    public void CentreHoleIsSkippedAlongTheAxis(int from, string directionName, bool wrap, int expected)
    {
        Assert.AreEqual(expected, GridNavigator.Next(Slots, Columns, from, Enum.Parse<MatrixDirection>(directionName), wrap, i => i != 4));
    }

    [TestMethod]
    public void CornerHoleBlocksWithoutWrapAndWrapsPastIt()
    {
        static bool Occupied(int i) => i != 2;

        Assert.AreEqual(-1, GridNavigator.Next(Slots, Columns, 1, MatrixDirection.Right, false, Occupied));
        Assert.AreEqual(0, GridNavigator.Next(Slots, Columns, 1, MatrixDirection.Right, true, Occupied));
        Assert.AreEqual(-1, GridNavigator.Next(Slots, Columns, 5, MatrixDirection.Up, false, Occupied));
        Assert.AreEqual(8, GridNavigator.Next(Slots, Columns, 5, MatrixDirection.Up, true, Occupied));
    }

    [TestMethod]
    public void HoleNeverCausesDiagonalMoveOrRowSpill()
    {
        // Only the top-left corner and the cell diagonal to it are occupied.
        static bool Occupied(int i) => i is 0 or 4;

        foreach (MatrixDirection direction in Directions)
        {
            Assert.AreEqual(-1, GridNavigator.Next(Slots, Columns, 0, direction, true, Occupied), direction.ToString());
            Assert.AreEqual(-1, GridNavigator.Next(Slots, Columns, 4, direction, true, Occupied), direction.ToString());
        }

        // The end of row 0 must not spill into row 1.
        Assert.AreEqual(-1, GridNavigator.Next(Slots, Columns, 2, MatrixDirection.Right, false, i => i is 2 or 3));
    }

    [TestMethod]
    public void EverySingleHoleEightMachineLayoutMatchesReference()
    {
        for (int hole = 0; hole < Slots; hole++)
        {
            int emptySlot = hole;
            AssertMatchesReference(Slots, Columns, i => i != emptySlot, $"hole {hole}");
        }
    }

    [TestMethod]
    public void EverySparseFiveMachineLayoutMatchesReference()
    {
        int layouts = 0;

        for (int mask = 0; mask < 1 << Slots; mask++)
        {
            if (System.Numerics.BitOperations.PopCount((uint)mask) != 5)
            {
                continue;
            }

            int occupied = mask;
            AssertMatchesReference(Slots, Columns, i => (occupied & (1 << i)) != 0, $"mask {mask:b9}");
            layouts++;
        }

        Assert.AreEqual(126, layouts);
    }

    [TestMethod]
    public void DisconnectedMachinesAreSkipped()
    {
        // Row 1 is fully occupied, but the machine in slot 4 is disconnected.
        HashSet<int> connected = [3, 5];

        Assert.AreEqual(5, GridNavigator.Next(Slots, Columns, 3, MatrixDirection.Right, false, connected.Contains));
        Assert.AreEqual(3, GridNavigator.Next(Slots, Columns, 5, MatrixDirection.Left, false, connected.Contains));
        Assert.AreEqual(-1, GridNavigator.Next(Slots, Columns, 1, MatrixDirection.Down, true, connected.Contains));
    }

    [TestMethod]
    public void EligibilityChecksStopAtTheNearestTargetOnTheSelectedAxis()
    {
        List<int> checkedSlots = [];
        int target = GridNavigator.Next(Slots, Columns, 0, MatrixDirection.Down, false, i =>
        {
            checkedSlots.Add(i);
            return i == 6;
        });
        Assert.AreEqual(6, target);
        int[] expected = [3, 6];
        CollectionAssert.AreEqual(expected, checkedSlots);

        checkedSlots.Clear();
        target = GridNavigator.Next(Slots, Columns, 0, MatrixDirection.Right, true, i =>
        {
            checkedSlots.Add(i);
            return true;
        });
        Assert.AreEqual(1, target);
        expected = [1];
        CollectionAssert.AreEqual(expected, checkedSlots);
    }

    [TestMethod]
    public void SoleMachineHasNoTargetInAnyDirection()
    {
        foreach (MatrixDirection direction in Directions)
        {
            Assert.AreEqual(-1, GridNavigator.Next(Slots, Columns, 4, direction, false, i => i == 4), direction.ToString());
            Assert.AreEqual(-1, GridNavigator.Next(Slots, Columns, 4, direction, true, i => i == 4), direction.ToString());
        }
    }

    [TestMethod]
    public void NoEligibleSlotsReturnsMinusOne()
    {
        foreach (MatrixDirection direction in Directions)
        {
            Assert.AreEqual(-1, GridNavigator.Next(Slots, Columns, 0, direction, true, _ => false), direction.ToString());
        }
    }

    [TestMethod]
    public void NeverReturnsCurrentSlot()
    {
        // A single column: Left and Right have nowhere to go even with wrap.
        Assert.AreEqual(-1, GridNavigator.Next(3, 1, 1, MatrixDirection.Left, true, _ => true));
        Assert.AreEqual(-1, GridNavigator.Next(3, 1, 1, MatrixDirection.Right, true, _ => true));
        Assert.AreEqual(2, GridNavigator.Next(3, 1, 1, MatrixDirection.Down, true, _ => true));
    }

    [TestMethod]
    public void OneRowMovesAlongTheRowAndNeverUpOrDown()
    {
        static bool Occupied(int i) => i is not 1 and not 7;

        Assert.AreEqual(2, GridNavigator.Next(Slots, Slots, 0, MatrixDirection.Right, false, Occupied));
        Assert.AreEqual(6, GridNavigator.Next(Slots, Slots, 8, MatrixDirection.Left, false, Occupied));
        Assert.AreEqual(-1, GridNavigator.Next(Slots, Slots, 8, MatrixDirection.Right, false, Occupied));
        Assert.AreEqual(0, GridNavigator.Next(Slots, Slots, 8, MatrixDirection.Right, true, Occupied));
        Assert.AreEqual(8, GridNavigator.Next(Slots, Slots, 0, MatrixDirection.Left, true, Occupied));

        for (int cell = 0; cell < Slots; cell++)
        {
            Assert.AreEqual(-1, GridNavigator.Next(Slots, Slots, cell, MatrixDirection.Up, true, _ => true));
            Assert.AreEqual(-1, GridNavigator.Next(Slots, Slots, cell, MatrixDirection.Down, true, _ => true));
        }

        AssertMatchesReference(Slots, Slots, Occupied, "one row");
    }

    [TestMethod]
    public void TwelveSlotsThreeColumnsByFourRows()
    {
        // 12 slots, 3 columns:
        //   0  1  2
        //   3  4  5
        //   6  7  8
        //   9 10 11
        Assert.AreEqual(3, GridNavigator.Next(12, 3, 0, MatrixDirection.Down, false, _ => true));
        Assert.AreEqual(-1, GridNavigator.Next(12, 3, 10, MatrixDirection.Down, false, _ => true));
        Assert.AreEqual(1, GridNavigator.Next(12, 3, 10, MatrixDirection.Down, true, _ => true));
        Assert.AreEqual(9, GridNavigator.Next(12, 3, 0, MatrixDirection.Up, true, _ => true));
        Assert.AreEqual(9, GridNavigator.Next(12, 3, 11, MatrixDirection.Right, true, _ => true));
        Assert.AreEqual(8, GridNavigator.Next(12, 3, 2, MatrixDirection.Down, false, i => i is not 5));

        for (int hole = 0; hole < 12; hole++)
        {
            int emptySlot = hole;
            AssertMatchesReference(12, 3, i => i != emptySlot, $"3x4 hole {hole}");
            AssertMatchesReference(12, 4, i => i != emptySlot, $"4x3 hole {hole}");
        }
    }

    [TestMethod]
    public void PartialLastRowIsHandled()
    {
        // 8 slots, 3 columns:
        //   0 1 2
        //   3 4 5
        //   6 7
        Assert.AreEqual(5, GridNavigator.Next(8, 3, 2, MatrixDirection.Down, false, _ => true));
        Assert.AreEqual(-1, GridNavigator.Next(8, 3, 5, MatrixDirection.Down, false, _ => true));
        Assert.AreEqual(2, GridNavigator.Next(8, 3, 5, MatrixDirection.Down, true, _ => true));
        Assert.AreEqual(5, GridNavigator.Next(8, 3, 2, MatrixDirection.Up, true, _ => true));
        Assert.AreEqual(-1, GridNavigator.Next(8, 3, 7, MatrixDirection.Right, false, _ => true));
        Assert.AreEqual(6, GridNavigator.Next(8, 3, 7, MatrixDirection.Right, true, _ => true));
        Assert.AreEqual(1, GridNavigator.Next(8, 3, 7, MatrixDirection.Down, true, _ => true));

        AssertMatchesReference(8, 3, _ => true, "partial row");
        AssertMatchesReference(8, 3, i => i != 4, "partial row, centre hole");
    }

    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(9)]
    public void CurrentSlotOutOfRangeReturnsMinusOne(int currentIndex)
    {
        Assert.AreEqual(-1, GridNavigator.Next(Slots, Columns, currentIndex, MatrixDirection.Right, true, _ => true));
    }

    [TestMethod]
    public void NullEligibilityIsRejected()
    {
        _ = Assert.ThrowsException<ArgumentNullException>(() => GridNavigator.Next(Slots, Columns, 0, MatrixDirection.Right, true, null!));
    }

    // Brute-force reference by coordinates: the nearest eligible slot ahead on the same row or column;
    // with wrap, otherwise the farthest eligible slot behind.
    private static int Reference(int slotCount, int columns, int current, MatrixDirection direction, bool wrap, Func<int, bool> isEligible)
    {
        bool horizontal = direction is MatrixDirection.Left or MatrixDirection.Right;
        int sign = direction is MatrixDirection.Right or MatrixDirection.Down ? 1 : -1;
        int Along(int i) => horizontal ? i % columns : i / columns;
        bool SameLine(int i) => horizontal ? i / columns == current / columns : i % columns == current % columns;

        int[] candidates = Enumerable.Range(0, slotCount).Where(i => i != current && SameLine(i) && isEligible(i)).ToArray();
        int[] ahead = candidates.Where(i => sign * (Along(i) - Along(current)) > 0).OrderBy(i => Math.Abs(Along(i) - Along(current))).ToArray();

        if (ahead.Length > 0)
        {
            return ahead[0];
        }

        if (!wrap)
        {
            return -1;
        }

        int[] behind = candidates.Where(i => sign * (Along(i) - Along(current)) < 0).OrderByDescending(i => Math.Abs(Along(i) - Along(current))).ToArray();
        return behind.Length > 0 ? behind[0] : -1;
    }

    private static void AssertMatchesReference(int slotCount, int columns, Func<int, bool> isEligible, string layout)
    {
        for (int current = 0; current < slotCount; current++)
        {
            if (!isEligible(current))
            {
                continue;
            }

            foreach (MatrixDirection direction in Directions)
            {
                foreach (bool wrap in new[] { false, true })
                {
                    int actual = GridNavigator.Next(slotCount, columns, current, direction, wrap, isEligible);

                    Assert.AreEqual(
                        Reference(slotCount, columns, current, direction, wrap, isEligible),
                        actual,
                        $"{layout}: from {current} {direction} wrap={wrap}");

                    if (actual >= 0)
                    {
                        Assert.IsTrue(isEligible(actual), $"{layout}: target {actual} is not eligible");
                        Assert.AreNotEqual(current, actual, $"{layout}: returned the current slot");
                    }
                }
            }
        }
    }
}

// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class MatrixLayoutTests
{
    private const int Slots = 9;
    private const int Columns = 3;

    [TestMethod]
    public void CreateEmptyFillsEverySlotWithEmptyString()
    {
        CollectionAssert.AreEqual(new[] { string.Empty, string.Empty, string.Empty }, MatrixLayout.CreateEmpty(3));
    }

    [TestMethod]
    public void CreateEmptyRejectsZeroSlots()
    {
        _ = Assert.ThrowsException<ArgumentOutOfRangeException>(() => MatrixLayout.CreateEmpty(0));
    }

    [TestMethod]
    public void CreatePutsLeadingNamesFirstAndPadsTheRest()
    {
        CollectionAssert.AreEqual(
            new[] { "A", "B", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty },
            MatrixLayout.Create(Slots, "A", "B"));
    }

    [TestMethod]
    public void CreateRejectsMoreNamesThanSlots()
    {
        _ = Assert.ThrowsException<ArgumentException>(() => MatrixLayout.Create(2, "A", "B", "C"));
    }

    [TestMethod]
    public void NormalizeNullGivesAllEmptySlots()
    {
        CollectionAssert.AreEqual(MatrixLayout.CreateEmpty(Slots), MatrixLayout.Normalize(null!, Slots, true, Columns));
    }

    [TestMethod]
    public void NormalizeEmptyArrayGivesAllEmptySlots()
    {
        CollectionAssert.AreEqual(MatrixLayout.CreateEmpty(Slots), MatrixLayout.Normalize([], Slots, true, Columns));
    }

    [TestMethod]
    public void NormalizeLegacyTwoRowKeepsGridCoordinates()
    {
        string[] result = MatrixLayout.Normalize(["A", "B", "C", "D"], Slots, true, Columns);

        CollectionAssert.AreEqual(new[] { "A", "B", string.Empty, "C", "D", string.Empty, string.Empty, string.Empty, string.Empty }, result);
    }

    [TestMethod]
    public void NormalizeLegacyTwoRowKeepsHoles()
    {
        string[] result = MatrixLayout.Normalize(["A", string.Empty, string.Empty, "D"], Slots, true, Columns);

        CollectionAssert.AreEqual(new[] { "A", string.Empty, string.Empty, string.Empty, "D", string.Empty, string.Empty, string.Empty, string.Empty }, result);
    }

    [TestMethod]
    public void NormalizeLegacyTwoRowIntoTwelveSlotGrid()
    {
        string[] result = MatrixLayout.Normalize(["A", "B", "C", "D"], 12, true, Columns);

        CollectionAssert.AreEqual(
            new[] { "A", "B", string.Empty, "C", "D", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty },
            result);
    }

    [TestMethod]
    public void NormalizeLegacyOneRowPadsInOrder()
    {
        string[] result = MatrixLayout.Normalize(["A", "B", "C", "D"], Slots, false, Columns);

        CollectionAssert.AreEqual(new[] { "A", "B", "C", "D", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty }, result);
    }

    [TestMethod]
    public void NormalizeFourSlotsIntoFourSlotBuildIsUnchanged()
    {
        string[] stored = ["A", "B", "C", "D"];

        CollectionAssert.AreEqual(stored, MatrixLayout.Normalize(stored, 4, true, 2));
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void NormalizeIsIdempotent(bool twoRow)
    {
        string[][] inputs =
        [
            ["A", "B", "C", "D"],
            ["A", string.Empty, "C"],
            ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K"],
            ["a", "A", " b ", null!, "B"],
        ];

        foreach (string[] input in inputs)
        {
            string[] once = MatrixLayout.Normalize(input, Slots, twoRow, Columns);
            CollectionAssert.AreEqual(once, MatrixLayout.Normalize(once, Slots, twoRow, Columns), string.Join(",", input));
        }
    }

    [TestMethod]
    public void NormalizeKeepsFullMatrixAndInteriorAndTrailingHoles()
    {
        string[] stored = ["A", string.Empty, "C", string.Empty, "E", string.Empty, string.Empty, "H", string.Empty];

        CollectionAssert.AreEqual(stored, MatrixLayout.Normalize(stored, Slots, true, Columns));
    }

    [TestMethod]
    public void NormalizeTrimsNamesAndTurnsNullIntoEmpty()
    {
        string[] result = MatrixLayout.Normalize([" A ", null!, "C", null!, null!, null!, null!, null!, " "], Slots, true, Columns);

        CollectionAssert.AreEqual(new[] { "A", string.Empty, "C", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty }, result);
    }

    [TestMethod]
    public void NormalizePadsShortMatrixKeepingPositions()
    {
        string[] result = MatrixLayout.Normalize(["A", string.Empty, "C", string.Empty, "E", "F"], Slots, true, Columns);

        CollectionAssert.AreEqual(new[] { "A", string.Empty, "C", string.Empty, "E", "F", string.Empty, string.Empty, string.Empty }, result);
    }

    [TestMethod]
    public void NormalizeLongMatrixMovesOverflowIntoHoles()
    {
        string[] stored = ["A", string.Empty, "C", "D", "E", "F", "G", string.Empty, "I", "J", string.Empty, "K", "L"];

        string[] result = MatrixLayout.Normalize(stored, Slots, true, Columns);

        string[] expected = ["A", "J", "C", "D", "E", "F", "G", "K", "I"];
        CollectionAssert.AreEqual(expected, result);
    }

    [TestMethod]
    public void NormalizeLongFullMatrixKeepsFirstSlots()
    {
        string[] stored = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L"];

        CollectionAssert.AreEqual(stored[..Slots], MatrixLayout.Normalize(stored, Slots, true, Columns));
    }

    [TestMethod]
    public void NormalizeRemovesLaterCaseInsensitiveDuplicates()
    {
        string[] result = MatrixLayout.Normalize(["A", "B", "a", string.Empty, " b", "C", "A", string.Empty, string.Empty], Slots, true, Columns);

        CollectionAssert.AreEqual(new[] { "A", "B", string.Empty, string.Empty, string.Empty, "C", string.Empty, string.Empty, string.Empty }, result);
    }

    [TestMethod]
    public void NormalizeDropsOverflowDuplicateOfKeptName()
    {
        string[] stored = ["A", string.Empty, "C", "D", "E", "F", "G", "H", "I", "a"];

        CollectionAssert.AreEqual(
            new[] { "A", string.Empty, "C", "D", "E", "F", "G", "H", "I" },
            MatrixLayout.Normalize(stored, Slots, true, Columns));
    }

    [TestMethod]
    public void NormalizeLegacyTwoRowRemovesDuplicates()
    {
        string[] result = MatrixLayout.Normalize(["A", "B", "a", "D"], Slots, true, Columns);

        CollectionAssert.AreEqual(new[] { "A", "B", string.Empty, string.Empty, "D", string.Empty, string.Empty, string.Empty, string.Empty }, result);
    }

    [TestMethod]
    public void NormalizeDoesNotModifyInput()
    {
        string[] stored = [" A ", "B", "C", "D"];

        string[] copy = (string[])stored.Clone();

        _ = MatrixLayout.Normalize(stored, Slots, true, Columns);

        CollectionAssert.AreEqual(copy, stored);
    }

    [TestMethod]
    public void PlaceInFirstEmptySlotFillsFirstInteriorHole()
    {
        string[] matrix = ["A", string.Empty, "C", string.Empty];

        Assert.IsTrue(MatrixLayout.PlaceInFirstEmptySlot(matrix, " NEW "));

        CollectionAssert.AreEqual(new[] { "A", "NEW", "C", string.Empty }, matrix);
    }

    [TestMethod]
    public void PlaceInFirstEmptySlotIgnoresNameAlreadyPresent()
    {
        string[] matrix = [string.Empty, "A", string.Empty];

        Assert.IsFalse(MatrixLayout.PlaceInFirstEmptySlot(matrix, "a"));

        CollectionAssert.AreEqual(new[] { string.Empty, "A", string.Empty }, matrix);
    }

    [TestMethod]
    public void PlaceInFirstEmptySlotReturnsFalseWhenFull()
    {
        string[] matrix = ["A", "B"];

        Assert.IsFalse(MatrixLayout.PlaceInFirstEmptySlot(matrix, "C"));

        Assert.AreEqual("A,B", string.Join(",", matrix));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public void PlaceInFirstEmptySlotIgnoresBlankName(string? name)
    {
        string[] matrix = ["A", string.Empty];

        Assert.IsFalse(MatrixLayout.PlaceInFirstEmptySlot(matrix, name!));

        CollectionAssert.AreEqual(new[] { "A", string.Empty }, matrix);
    }

    [DataTestMethod]
    [DataRow(9, false, 3, 3)]
    [DataRow(9, true, 3, 9)]
    [DataRow(12, false, 3, 3)]
    [DataRow(12, true, 3, 12)]
    [DataRow(2, false, 3, 2)]
    public void ColumnsFollowsRowMode(int slotCount, bool oneRow, int gridColumns, int expected)
    {
        Assert.AreEqual(expected, MatrixLayout.Columns(slotCount, oneRow, gridColumns));
    }

    [TestMethod]
    public void ReplaceNameSwapsAnAddressForTheRealName()
    {
        string[] matrix = ["192.168.1.20", "B", string.Empty];
        Assert.IsTrue(MatrixLayout.ReplaceName(matrix, " 192.168.1.20 ", "LAPTOP-A"));
        CollectionAssert.AreEqual(new[] { "LAPTOP-A", "B", string.Empty }, matrix);
    }

    [TestMethod]
    public void ReplaceNameEmptiesTheSlotWhenTheNameIsAlreadyPlaced()
    {
        string[] matrix = ["fe80::1", "laptop-a", "10.0.0.1"];
        Assert.IsTrue(MatrixLayout.ReplaceName(matrix, "FE80::1", "LAPTOP-A"));
        CollectionAssert.AreEqual(new[] { string.Empty, "laptop-a", "10.0.0.1" }, matrix);
    }

    [TestMethod]
    public void ReplaceNameKeepsOnlyOneCopyWhenTheOldNameRepeats()
    {
        string[] matrix = ["10.0.0.1", string.Empty, "10.0.0.1"];
        Assert.IsTrue(MatrixLayout.ReplaceName(matrix, "10.0.0.1", "A"));
        CollectionAssert.AreEqual(new[] { "A", string.Empty, string.Empty }, matrix);
    }

    [DataTestMethod]
    [DataRow("C", "D")]
    [DataRow("A", "a")]
    [DataRow("", "A")]
    [DataRow("A", " ")]
    public void ReplaceNameLeavesTheMatrixAloneWhenNothingMatches(string oldName, string newName)
    {
        string[] matrix = ["A", "B", string.Empty];
        Assert.IsFalse(MatrixLayout.ReplaceName(matrix, oldName, newName));
        CollectionAssert.AreEqual(new[] { "A", "B", string.Empty }, matrix);
    }

    [TestMethod]
    public void FillingEmptySlotsIsAJoin()
    {
        string[] current = ["A", "B", string.Empty, string.Empty];
        Assert.IsTrue(MatrixLayout.OnlyFillsEmptySlots(current, ["a ", "B", "C", string.Empty]));
        Assert.IsTrue(MatrixLayout.OnlyFillsEmptySlots(current, ["A", "B", string.Empty, "C"]));
        Assert.IsTrue(MatrixLayout.OnlyFillsEmptySlots(current, current));
    }

    [TestMethod]
    public void MovingRemovingOrResizingIsNotAJoin()
    {
        string[] current = ["A", "B", string.Empty, string.Empty];
        Assert.IsFalse(MatrixLayout.OnlyFillsEmptySlots(current, ["B", "A", string.Empty, string.Empty]));
        Assert.IsFalse(MatrixLayout.OnlyFillsEmptySlots(current, ["A", string.Empty, string.Empty, string.Empty]));
        Assert.IsFalse(MatrixLayout.OnlyFillsEmptySlots(current, ["A", "C", string.Empty, string.Empty]));
        Assert.IsFalse(MatrixLayout.OnlyFillsEmptySlots(current, ["A", "B", string.Empty]));
    }
}

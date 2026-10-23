// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Net;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class Name2IpMapTests
{
    private static readonly IPAddress First = IPAddress.Parse("192.168.1.20");
    private static readonly IPAddress Second = IPAddress.Parse("192.168.1.21");

    [TestMethod]
    public void LearningIntoEmptyTextWritesOneLine()
    {
        Assert.AreEqual("LAPTOP-2 192.168.1.20 #learned", Name2IpMap.SetLearned(string.Empty, "LAPTOP-2", First));
        Assert.AreEqual("LAPTOP-2 192.168.1.20 #learned", Name2IpMap.SetLearned(null, " LAPTOP-2 ", First));
    }

    [TestMethod]
    public void LearningANewAddressReplacesTheLearnedLineInPlace()
    {
        string text = "A 10.0.0.1\r\nlaptop-2 192.168.1.20 #learned\r\nB 10.0.0.2";
        Assert.AreEqual(
            "A 10.0.0.1\r\nLAPTOP-2 192.168.1.21 #learned\r\nB 10.0.0.2",
            Name2IpMap.SetLearned(text, "LAPTOP-2", Second));
    }

    [TestMethod]
    public void LearningTheSameAddressAgainChangesNothing()
    {
        string text = "A 10.0.0.1\r\nLAPTOP-2 192.168.1.20 #learned\r\n";
        Assert.AreEqual(text, Name2IpMap.SetLearned(text, "LAPTOP-2", First));
    }

    [TestMethod]
    public void DuplicateLearnedLinesCollapseIntoOne()
    {
        string text = "LAPTOP-2 192.168.1.1 #learned\r\nA 10.0.0.1\r\nlaptop-2 192.168.1.2 #LEARNED";
        Assert.AreEqual("LAPTOP-2 192.168.1.20 #learned\r\nA 10.0.0.1", Name2IpMap.SetLearned(text, "LAPTOP-2", First));
    }

    [TestMethod]
    public void UserLinesForTheSameNameAreKept()
    {
        string text = "LAPTOP-2 10.0.0.9";
        Assert.AreEqual("LAPTOP-2 10.0.0.9\r\nLAPTOP-2 192.168.1.20 #learned", Name2IpMap.SetLearned(text, "LAPTOP-2", First));
    }

    [TestMethod]
    public void UserLineWithTheSameAddressMakesTheLearnedLineRedundant()
    {
        string text = "laptop-2 192.168.1.20\r\nLAPTOP-2 192.168.1.1 #learned";
        Assert.AreEqual("laptop-2 192.168.1.20", Name2IpMap.SetLearned(text, "LAPTOP-2", First));
    }

    [TestMethod]
    public void InvalidAndBlankLinesAreKeptVerbatim()
    {
        string text = "garbage\r\n\r\nC not-an-ip\r\n  D   10.0.0.4  ";
        Assert.AreEqual(text + "\r\nLAPTOP-2 192.168.1.20 #learned", Name2IpMap.SetLearned(text, "LAPTOP-2", First));
    }

    [TestMethod]
    public void ATrailingLineBreakStaysAtTheEnd()
    {
        Assert.AreEqual("A 10.0.0.1\r\nLAPTOP-2 192.168.1.20 #learned\r\n", Name2IpMap.SetLearned("A 10.0.0.1\r\n", "LAPTOP-2", First));
    }

    [TestMethod]
    public void LineFeedOnlyTextIsRewrittenWithCrLf()
    {
        Assert.AreEqual("A 10.0.0.1\r\nB 10.0.0.2\r\nLAPTOP-2 192.168.1.20 #learned", Name2IpMap.SetLearned("A 10.0.0.1\nB 10.0.0.2", "LAPTOP-2", First));
    }

    [TestMethod]
    public void MappedIPv6IsStoredAsIPv4()
    {
        Assert.AreEqual("LAPTOP-2 192.168.1.20 #learned", Name2IpMap.SetLearned(string.Empty, "LAPTOP-2", First.MapToIPv6()));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("TWO WORDS")]
    public void InvalidNamesLeaveTheTextAlone(string name)
    {
        Assert.AreEqual("A 10.0.0.1", Name2IpMap.SetLearned("A 10.0.0.1", name, First));
    }

    [TestMethod]
    public void NullAddressLeavesTheTextAlone()
    {
        Assert.AreEqual("A 10.0.0.1", Name2IpMap.SetLearned("A 10.0.0.1", "A", null));
    }

    [TestMethod]
    public void AddressesForReadsUserAndLearnedLinesCaseInsensitively()
    {
        string text = "laptop-2 10.0.0.9\r\nA 10.0.0.1\nLAPTOP-2 192.168.1.20 #learned\r\nLAPTOP-2 10.0.0.9\r\nLAPTOP-2 nonsense\r\nLAPTOP-2";
        IPAddress[] expected = [IPAddress.Parse("10.0.0.9"), First];
        CollectionAssert.AreEqual(expected, Name2IpMap.AddressesFor(text, "Laptop-2"));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void AddressesForEmptyTextOrNameIsEmpty(string value)
    {
        Assert.AreEqual(0, Name2IpMap.AddressesFor(value, "A").Count);
        Assert.AreEqual(0, Name2IpMap.AddressesFor("A 10.0.0.1", value).Count);
    }

    [TestMethod]
    public void LearnedLinesStillParseAsPlainMappings()
    {
        // Older builds split each line on spaces and read only the first two tokens.
        string line = Name2IpMap.SetLearned(string.Empty, "LAPTOP-2", First);
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual("LAPTOP-2", tokens[0]);
        Assert.AreEqual(First, IPAddress.Parse(tokens[1]));
    }
}

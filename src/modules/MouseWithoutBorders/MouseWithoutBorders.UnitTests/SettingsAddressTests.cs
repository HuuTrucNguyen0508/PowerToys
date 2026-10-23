// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Net;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsAddressTests
{
    [TestMethod]
    public void EditorWritePreservesAddressesLearnedSinceTheLastRefresh()
    {
        var settings = CreateSettings(new MouseWithoutBordersSettings());
        settings.Name2IP = "USER 10.0.0.1";
        Assert.IsTrue(settings.LearnAddresses(new[] { ("PEER", IPAddress.Parse("192.168.1.20")) }));
        settings.Name2IP = "USER 10.0.0.2";

        Assert.AreEqual("USER 10.0.0.2\r\nPEER 192.168.1.20 #learned", settings.Name2IP);
        Assert.IsFalse(settings.LearnAddresses(new[] { ("PEER", IPAddress.Parse("192.168.1.20")) }));
    }

    [TestMethod]
    public void ReloadRestoresLearnedAddressesAfterSettingsWritesStaleMappings()
    {
        var initial = new MouseWithoutBordersSettings();
        initial.Properties.SecurityKey.Value = "TEST-KEY";
        var settings = CreateSettings(initial);
        settings.LearnAddresses(new[] { ("PEER", IPAddress.Parse("192.168.1.21")) });
        var loaded = new MouseWithoutBordersSettings();
        loaded.Properties.SecurityKey.Value = "TEST-KEY";
        loaded.Properties.Name2IP.Value = "USER 10.0.0.1\r\nPEER 192.168.1.20 #learned";
        int saves = 0;
        var previousKey = Encryption.MyKey;
        try
        {
            Encryption.MyKey = "TEST-KEY";
            settings.ApplyLoadedSettings(loaded, () => { }, () => { }, () => { }, () => saves++);
            Assert.AreEqual("USER 10.0.0.1\r\nPEER 192.168.1.21 #learned", settings.Name2IP);
            Assert.AreEqual(1, saves);

            var withoutLearned = new MouseWithoutBordersSettings();
            withoutLearned.Properties.SecurityKey.Value = "TEST-KEY";
            withoutLearned.Properties.Name2IP.Value = "USER 10.0.0.2";
            settings.ApplyLoadedSettings(withoutLearned, () => { }, () => { }, () => { }, () => saves++);
            Assert.AreEqual("USER 10.0.0.2\r\nPEER 192.168.1.21 #learned", settings.Name2IP);
            Assert.AreEqual(2, saves);
        }
        finally
        {
            Encryption.MyKey = previousKey;
        }
    }

    private static Class.Settings CreateSettings(MouseWithoutBordersSettings saved)
    {
        var settings = new Class.Settings(null!, saved, new PassthroughPairingKeyCodec()) { PauseInstantSaving = true };
        if (settings.Name2IpIsGpoConfigured)
        {
            Assert.Inconclusive("This test requires editable IP mappings.");
        }

        return settings;
    }

    [TestMethod]
    public void LogonDesktopKeepsLearnedAddressesWithoutSaving()
    {
        var settings = CreateSettings(new MouseWithoutBordersSettings());
        var wasLogonDesktop = Common.RunOnLogonDesktop;
        try
        {
            Common.RunOnLogonDesktop = true;
            settings.PauseInstantSaving = false;
            Assert.IsTrue(settings.LearnAddresses(new[] { ("PEER", IPAddress.Parse("192.168.1.20")) }));
            Assert.AreEqual("PEER 192.168.1.20 #learned", settings.LearnedAddresses);
            Assert.AreEqual(settings.LearnedAddresses, settings.Name2IP);
        }
        finally
        {
            Common.RunOnLogonDesktop = wasLogonDesktop;
        }
    }

    [TestMethod]
    public void ReloadOfOurOwnSaveDoesNotRollBackANewerLayout()
    {
        var initial = new MouseWithoutBordersSettings();
        initial.Properties.SecurityKey.Value = "TEST-KEY";
        var settings = CreateSettings(initial);
        settings.MachineMatrixString = "A";
        MouseWithoutBordersSettings saved = null!;
        settings.SaveCurrentSettings(snapshot => saved = snapshot);
        settings.MachineMatrixString = "A,B";
        settings.LearnAddresses(new[] { ("B", IPAddress.Parse("192.168.1.20")) });
        Assert.AreEqual("A", saved.Properties.MachineMatrixString.Single());
        Assert.AreEqual(string.Empty, saved.Properties.Name2IP.Value);
        int effects = 0;
        settings.ApplyLoadedSettings(saved, () => effects++, () => effects++, () => effects++, () => effects++);

        Assert.AreEqual("A,B", settings.MachineMatrixString);
        Assert.AreEqual("B 192.168.1.20 #learned", settings.Name2IP);
        Assert.AreEqual(0, effects);
        settings.SaveCurrentSettings(snapshot => saved = snapshot);
        string[] expected = ["A", "B"];
        CollectionAssert.AreEqual(expected, saved.Properties.MachineMatrixString);
        Assert.AreEqual(settings.Name2IP, saved.Properties.Name2IP.Value);
    }
}

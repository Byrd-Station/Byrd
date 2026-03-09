// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Content.Shared._Omu.Saboteur;
using Content.Shared.Security;
using NUnit.Framework;

namespace Content.Tests.Shared._Omu.Saboteur;

[TestFixture]
[TestOf(typeof(SaboteurTierHelper))]
public sealed class SaboteurTierHelperTest
{
    private static readonly List<SaboteurTierConfig> Tiers =
    [
        new() { Threshold = 0, LocId = "t0", MinorTcReward = 2, MajorTcReward = 5 },
        new() { Threshold = 10, LocId = "t1", MinorTcReward = 4, MajorTcReward = 10 },
        new() { Threshold = 25, LocId = "t2", MinorTcReward = 6, MajorTcReward = 15 },
        new() { Threshold = 50, LocId = "t3", MinorTcReward = 8, MajorTcReward = 20 },
    ];

    [TestCase(0, ExpectedResult = 0)]
    [TestCase(5, ExpectedResult = 0)]
    [TestCase(9, ExpectedResult = 0)]
    [TestCase(10, ExpectedResult = 1)]
    [TestCase(24, ExpectedResult = 1)]
    [TestCase(25, ExpectedResult = 2)]
    [TestCase(49, ExpectedResult = 2)]
    [TestCase(50, ExpectedResult = 3)]
    [TestCase(999, ExpectedResult = 3)]
    public int GetTierFromReputation_ReturnsCorrectTier(int reputation)
    {
        return SaboteurTierHelper.GetTierFromReputation(Tiers, reputation);
    }

    [Test]
    public void GetTierFromReputation_EmptyThresholds_ReturnsZero()
    {
        Assert.That(SaboteurTierHelper.GetTierFromReputation([], 100), Is.EqualTo(0));
    }

    [Test]
    public void GetTierFromReputation_SingleThreshold_ReturnsZero()
    {
        List<SaboteurTierConfig> single = [new() { Threshold = 0, LocId = "t0", MinorTcReward = 1, MajorTcReward = 2 }];
        Assert.That(SaboteurTierHelper.GetTierFromReputation(single, 100), Is.EqualTo(0));
    }

    [TestCase(0, ExpectedResult = 10)]
    [TestCase(1, ExpectedResult = 25)]
    [TestCase(2, ExpectedResult = 50)]
    [TestCase(3, ExpectedResult = 0, Description = "At max tier, returns 0")]
    public int GetNextTierThreshold_ReturnsCorrectThreshold(int currentTier)
    {
        return SaboteurTierHelper.GetNextTierThreshold(Tiers, currentTier);
    }

    [Test]
    public void GetTelecrystalReward_Minor_ReturnsTierIndexed()
    {
        Assert.That(SaboteurTierHelper.GetTelecrystalReward(Tiers, 0), Is.EqualTo(2));
        Assert.That(SaboteurTierHelper.GetTelecrystalReward(Tiers, 1), Is.EqualTo(4));
        Assert.That(SaboteurTierHelper.GetTelecrystalReward(Tiers, 2), Is.EqualTo(6));
    }

    [Test]
    public void GetTelecrystalReward_Major_ReturnsTierIndexed()
    {
        Assert.That(SaboteurTierHelper.GetTelecrystalReward(Tiers, 0, isMajor: true), Is.EqualTo(5));
        Assert.That(SaboteurTierHelper.GetTelecrystalReward(Tiers, 1, isMajor: true), Is.EqualTo(10));
    }

    [Test]
    public void GetTelecrystalReward_TierBeyondList_ClampsToLast()
    {
        Assert.That(SaboteurTierHelper.GetTelecrystalReward(Tiers, 99), Is.EqualTo(8));
        Assert.That(SaboteurTierHelper.GetTelecrystalReward(Tiers, 99, isMajor: true), Is.EqualTo(20));
    }

    [Test]
    public void GetTelecrystalReward_EmptyList_ReturnsZero()
    {
        Assert.That(SaboteurTierHelper.GetTelecrystalReward([], 0), Is.EqualTo(0));
    }

    [Test]
    public void GetExposureMultiplier_KnownStatus_ReturnsValue()
    {
        var multipliers = new Dictionary<SecurityStatus, float>
        {
            { SecurityStatus.Suspected, 0.5f },
            { SecurityStatus.Wanted, 0.25f },
        };

        Assert.That(SaboteurTierHelper.GetExposureMultiplier(multipliers, SecurityStatus.Suspected), Is.EqualTo(0.5f));
        Assert.That(SaboteurTierHelper.GetExposureMultiplier(multipliers, SecurityStatus.Wanted), Is.EqualTo(0.25f));
    }

    [Test]
    public void GetExposureMultiplier_UnknownStatus_ReturnsDefault()
    {
        var multipliers = new Dictionary<SecurityStatus, float>
        {
            { SecurityStatus.Wanted, 0.25f },
        };

        Assert.That(SaboteurTierHelper.GetExposureMultiplier(multipliers, SecurityStatus.None), Is.EqualTo(1.0f));
    }

    [TestCase(0.99f, ExpectedResult = true)]
    [TestCase(0.5f, ExpectedResult = true)]
    [TestCase(0.0f, ExpectedResult = true)]
    [TestCase(1.0f, ExpectedResult = false)]
    [TestCase(1.5f, ExpectedResult = false)]
    public bool IsExposed_ReturnsCorrectResult(float multiplier)
    {
        return SaboteurTierHelper.IsExposed(multiplier);
    }

    [Test]
    public void GetMaxTier_ReturnsLastIndex()
    {
        Assert.That(SaboteurTierHelper.GetMaxTier(Tiers), Is.EqualTo(3));
    }

    [Test]
    public void GetMaxTier_SingleElement_ReturnsZero()
    {
        List<SaboteurTierConfig> single = [new() { Threshold = 0, LocId = "t0", MinorTcReward = 1, MajorTcReward = 2 }];
        Assert.That(SaboteurTierHelper.GetMaxTier(single), Is.EqualTo(0));
    }

    [Test]
    public void GetNextTierThreshold_EmptyThresholds_ReturnsZero()
    {
        Assert.That(SaboteurTierHelper.GetNextTierThreshold([], 0), Is.EqualTo(0));
    }

    [TestCase(-5, ExpectedResult = 0, Description = "Negative reputation returns tier 0")]
    public int GetTierFromReputation_NegativeReputation_ReturnsZero(int reputation)
    {
        return SaboteurTierHelper.GetTierFromReputation(Tiers, reputation);
    }
}

// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using Content.Server._Omu.Saboteur;
using Content.Server._Omu.Saboteur.Conditions;
using Content.Server._Omu.Saboteur.Systems;
using Content.Shared._Omu.Saboteur;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests.Server._Omu.Saboteur;

[TestFixture]
[TestOf(typeof(SaboteurConditionCoreSystem))]
public sealed class SaboteurConditionCoreSystemTest
{
    private SaboteurConditionCoreSystem _sys = default!;

    [SetUp]
    public void SetUp()
    {
        _sys = new SaboteurConditionCoreSystem();
    }

    #region CalculateThresholdProgress

    [TestCase(5, 10, 0.5f, ExpectedResult = 1.0f, Description = "Exactly at threshold")]
    [TestCase(3, 10, 0.5f, ExpectedResult = 0.6f, Description = "Below threshold")]
    [TestCase(10, 10, 0.5f, ExpectedResult = 1.0f, Description = "All affected, capped at 1")]
    [TestCase(0, 10, 0.5f, ExpectedResult = 0.0f, Description = "None affected")]
    [TestCase(1, 10, 1.0f, ExpectedResult = 0.1f, Description = "Threshold = 100%")]
    public float CalculateThresholdProgress_ReturnsCorrectProgress(int affected, int total, float threshold)
    {
        return _sys.CalculateThresholdProgress(affected, total, threshold);
    }

    [TestCase(0, 10, 0.5f, Description = "Zero affected, non-zero total")]
    [TestCase(5, 0, 0.5f, Description = "Zero total")]
    [TestCase(5, 10, 0f, Description = "Zero threshold")]
    [TestCase(5, -1, 0.5f, Description = "Negative total")]
    [TestCase(5, 10, -0.5f, Description = "Negative threshold")]
    public void CalculateThresholdProgress_InvalidInputs_ReturnsZero(int affected, int total, float threshold)
    {
        Assert.That(_sys.CalculateThresholdProgress(affected, total, threshold), Is.EqualTo(0f));
    }

    [TestCase(15, 10, 0.5f, ExpectedResult = 1.0f, Description = "Affected exceeds total — capped at 1")]
    public float CalculateThresholdProgress_BoundaryInputs(int affected, int total, float threshold)
    {
        return _sys.CalculateThresholdProgress(affected, total, threshold);
    }

    [Test]
    public void CalculateThresholdProgress_NegativeAffected_NotGuarded()
    {
        // Negative affected is not guarded — the method produces a negative fraction.
        // In practice affected is never negative (entity count), but verify the behavior.
        var result = _sys.CalculateThresholdProgress(-3, 10, 0.5f);
        Assert.That(result, Is.LessThan(0f),
            "Negative affected is not guarded and produces a negative result.");
    }

    [Test]
    public void CalculateThresholdProgress_VerySmallThreshold_ClampedAtOne()
    {
        // threshold near epsilon — fraction / threshold can exceed 1.0
        var result = _sys.CalculateThresholdProgress(1, 10, 0.01f);
        Assert.That(result, Is.EqualTo(1.0f), "Result should be clamped to 1.0 regardless of how small the threshold is.");
    }

    #endregion

    #region CountProgress

    [TestCase(5, 5, ExpectedResult = 1f, Description = "Exact match")]
    [TestCase(10, 5, ExpectedResult = 1f, Description = "Over required")]
    [TestCase(3, 5, ExpectedResult = 0.6f, Description = "Partial")]
    [TestCase(1, 5, ExpectedResult = 0.2f, Description = "Minimal")]
    [TestCase(0, 5, ExpectedResult = 0f, Description = "None")]
    public float CountProgress_ReturnsCorrectProgress(int count, int required)
    {
        return _sys.CountProgress(count, required);
    }

    [TestCase(0, 0, ExpectedResult = 1f, Description = "Zero of zero required is complete")]
    [TestCase(5, 0, ExpectedResult = 1f, Description = "Any count with zero required is complete")]
    [TestCase(-1, 5, ExpectedResult = 0f, Description = "Negative count returns 0")]
    [TestCase(0, -1, ExpectedResult = 1f, Description = "Negative required treated as met")]
    public float CountProgress_EdgeCases(int count, int required)
    {
        return _sys.CountProgress(count, required);
    }

    #endregion

    #region MakeCacheKey

    [Test]
    public void MakeCacheKey_ProducesConsistentFormat()
    {
        var uid = new EntityUid(42);
        var key = _sys.MakeCacheKey(uid);
        Assert.That(key, Is.EqualTo("cond_42"));
    }

    [Test]
    public void MakeCacheKey_DifferentUids_ProduceDifferentKeys()
    {
        var key1 = _sys.MakeCacheKey(new EntityUid(1));
        var key2 = _sys.MakeCacheKey(new EntityUid(2));
        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void MakeCacheKey_InvalidUid_StillProducesKey()
    {
        // EntityUid.Invalid is 0 — should still produce a deterministic key, not throw
        var key = _sys.MakeCacheKey(EntityUid.Invalid);
        Assert.That(key, Is.Not.Null.And.Not.Empty,
            "MakeCacheKey should produce a valid key even for EntityUid.Invalid.");
    }

    [Test]
    public void MakeCacheKey_SameUid_IsIdempotent()
    {
        var uid = new EntityUid(99);
        Assert.That(_sys.MakeCacheKey(uid), Is.EqualTo(_sys.MakeCacheKey(uid)),
            "Same UID must always produce the same cache key.");
    }

    #endregion

    #region GetDomainsForCheckMode

    [TestCase(SabotageMode.Unpowered, SaboteurDirtyDomain.Power)]
    [TestCase(SabotageMode.ApcDisabled, SaboteurDirtyDomain.Power)]
    [TestCase(SabotageMode.CameraInactive, SaboteurDirtyDomain.Camera)]
    [TestCase(SabotageMode.Destroyed, SaboteurDirtyDomain.Entity)]
    [TestCase(SabotageMode.DoorBolted, SaboteurDirtyDomain.Bolts)]
    public void GetDomainsForCheckMode_ReturnsSingleExpectedDomain(SabotageMode mode, SaboteurDirtyDomain expected)
    {
        Assert.That(_sys.GetDomainsForCheckMode(mode), Is.EqualTo(expected));
    }

    [Test]
    public void GetDomainsForCheckMode_KeysEmpty_ReturnsBothDomains()
    {
        var result = _sys.GetDomainsForCheckMode(SabotageMode.KeysEmpty);
        Assert.That(result.HasFlag(SaboteurDirtyDomain.EncryptionKeyHolder),
            "KeysEmpty should include EncryptionKeyHolder domain.");
        Assert.That(result.HasFlag(SaboteurDirtyDomain.Entity),
            "KeysEmpty should include Entity domain.");
    }

    [Test]
    public void GetDomainsForCheckMode_UnknownMode_ReturnsNone()
    {
        // Cast an out-of-range int to SabotageMode to simulate an unknown value
        var unknown = (SabotageMode) 999;
        Assert.That(_sys.GetDomainsForCheckMode(unknown), Is.EqualTo(SaboteurDirtyDomain.None),
            "Unknown sabotage mode should return None domain.");
    }

    [Test]
    public void GetDomainsForCheckMode_AllDefinedModes_ReturnNonNone()
    {
        // Every defined SabotageMode should map to at least one domain
        foreach (var mode in Enum.GetValues<SabotageMode>())
        {
            var domains = _sys.GetDomainsForCheckMode(mode);
            Assert.That(domains, Is.Not.EqualTo(SaboteurDirtyDomain.None),
                    $"SabotageMode.{mode} returned None - every mode should map to a domain.");
        }
    }

    #endregion
}

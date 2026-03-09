// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Security;
using Robust.Shared.Localization;

namespace Content.Shared._Omu.Saboteur;

/// <summary>
/// Static utility class for saboteur tier calculations, TC rewards, and exposure logic.
/// </summary>
public static class SaboteurTierHelper
{

    private static readonly Dictionary<SecurityStatus, string> SecurityStatusLocKeys = new()
    {
        { SecurityStatus.None, "criminal-records-status-none" },
        { SecurityStatus.Suspected, "criminal-records-status-suspected" },
        { SecurityStatus.Wanted, "criminal-records-status-wanted" },
        { SecurityStatus.Detained, "criminal-records-status-detained" },
        { SecurityStatus.Paroled, "criminal-records-status-paroled" },
        { SecurityStatus.Discharged, "criminal-records-status-discharged" },
        { SecurityStatus.Search, "criminal-records-status-search" },
        { SecurityStatus.Perma, "criminal-records-status-perma" },
        { SecurityStatus.Dangerous, "criminal-records-status-dangerous" },
    };

    /// <summary>
    /// Returns the highest tier whose threshold the given reputation meets or exceeds.
    /// </summary>
    public static int GetTierFromReputation(List<SaboteurTierConfig> tiers, int reputation)
    {
        for (var i = tiers.Count - 1; i > 0; i--)
        {
            if (reputation >= tiers[i].Threshold)
                return i;
        }
        return 0;
    }

    /// <summary>
    /// Returns the reputation threshold needed for the next tier, or 0 if already at max.
    /// </summary>
    public static int GetNextTierThreshold(List<SaboteurTierConfig> tiers, int currentTier)
    {
        var next = currentTier + 1;
        return next < tiers.Count ? tiers[next].Threshold : 0;
    }

    /// <summary>
    /// Returns the localised display name for the given tier.
    /// </summary>
    public static string GetTierName(List<SaboteurTierConfig> tiers, int tier)
    {
        if (tiers.Count == 0)
            return tier.ToString();

        var idx = Math.Clamp(tier, 0, tiers.Count - 1);
        return Loc.GetString(tiers[idx].LocId);
    }

    /// <summary>
    /// Returns the TC reward for completing an operation at the given tier.
    /// </summary>
    public static int GetTelecrystalReward(List<SaboteurTierConfig> tiers, int tier, bool isMajor = false)
    {
        if (tiers.Count == 0)
            return 0;

        var idx = Math.Clamp(tier, 0, tiers.Count - 1);
        return isMajor ? tiers[idx].MajorTcReward : tiers[idx].MinorTcReward;
    }

    /// <summary>
    /// Returns the reputation multiplier for a given criminal record status.
    /// </summary>
    public static float GetExposureMultiplier(Dictionary<SecurityStatus, float> exposureMultipliers, SecurityStatus status)
    {
        return exposureMultipliers.GetValueOrDefault(status, 1.0f);
    }

    /// <summary>
    /// Returns true if the saboteur has any exposure penalty active.
    /// </summary>
    public static bool IsExposed(float exposurePenaltyMultiplier)
    {
        return exposurePenaltyMultiplier < 1.0f;
    }

    /// <summary>
    /// Returns the maximum reachable tier index.
    /// </summary>
    public static int GetMaxTier(List<SaboteurTierConfig> tiers)
    {
        return tiers.Count - 1;
    }

    /// <summary>
    /// Returns the localization key for a criminal record status, for display in exposure messages.
    /// </summary>
    public static string GetSecurityStatusLocKey(SecurityStatus status)
    {
        if (SecurityStatusLocKeys.TryGetValue(status, out var key))
            return key;

        // Unmapped status — fall back to "None" display key.
        return "criminal-records-status-none";
    }
}

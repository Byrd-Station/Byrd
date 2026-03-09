// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Omu.Saboteur;

/// <summary>
/// Configuration for a single saboteur reputation tier: threshold, display name, and TC rewards.
/// </summary>
[DataDefinition]
public partial struct SaboteurTierConfig
{
    /// <summary>
    /// Minimum reputation required to reach this tier.
    /// </summary>
    [DataField(required: true)]
    public int Threshold;

    /// <summary>
    /// Localization ID for the tier's display name.
    /// </summary>
    [DataField(required: true)]
    public string LocId;

    /// <summary>
    /// TC reward for completing a minor operation at this tier.
    /// </summary>
    [DataField(required: true)]
    public int MinorTcReward;

    /// <summary>
    /// TC reward for completing a major operation at this tier.
    /// </summary>
    [DataField(required: true)]
    public int MajorTcReward;
}

// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Robust.Shared.GameObjects;

using Content.Server._Omu.Saboteur.Conditions.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Components;

/// <summary>
/// Objective condition requiring the saboteur to make station announcements from the bridge.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurBridgeControlConditionSystem))]
public sealed partial class SaboteurTakeBridgeControlConditionComponent : Component
{
    /// <summary>
    /// Number of announcements the saboteur must make.
    /// </summary>
    [DataField(required: true)]
    public int RequiredCount;

    /// <summary>
    /// Current count of qualifying announcements made.
    /// </summary>
    [ViewVariables]
    public int AnnouncementCount;

    /// <summary>
    /// Dirty-domain cache key for re-evaluation tracking.
    /// </summary>
    [ViewVariables]
    public string CacheKey = string.Empty;
}

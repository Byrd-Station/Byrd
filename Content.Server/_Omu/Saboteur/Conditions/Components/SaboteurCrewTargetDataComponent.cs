// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Robust.Shared.GameObjects;

using Content.Server._Omu.Saboteur.Systems;

namespace Content.Server._Omu.Saboteur.Conditions.Components;

/// <summary>
/// Holds shared crew-targeting data for saboteur objective conditions that
/// target crew members (e.g., flagged-records, plant-evidence).
/// Added at runtime by <see cref="SaboteurCrewTargetingSystem"/> when a
/// crew-target condition is assigned.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurCrewTargetingSystem))]
public sealed partial class SaboteurCrewTargetDataComponent : Component
{
    /// <summary>
    /// Crew members targeted by this condition.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> FlagTargets = new();

    /// <summary>
    /// Original number of targets at assignment time, used for progress calculation.
    /// </summary>
    [ViewVariables]
    public int OriginalFlagTargetCount;

    /// <summary>
    /// Dirty-domain cache key for re-evaluation tracking.
    /// </summary>
    [ViewVariables]
    public string CacheKey = string.Empty;

    /// <summary>
    /// Display name snapshot of the saboteur taken at assignment time,
    /// used to detect self-initiated record changes.
    /// </summary>
    [ViewVariables]
    public string SnapshottedName = string.Empty;
}

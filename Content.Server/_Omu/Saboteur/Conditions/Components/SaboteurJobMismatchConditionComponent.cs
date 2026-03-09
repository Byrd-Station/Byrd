// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Robust.Shared.GameObjects;

using Content.Server._Omu.Saboteur.Conditions.Systems;
using Content.Server._Omu.Saboteur.Conditions;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Components;

/// <summary>
/// Objective condition requiring the saboteur to alter ID cards so that job titles
/// no longer match the original holder's assigned role.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurJobMismatchConditionSystem))]
public sealed partial class SaboteurJobMismatchConditionComponent : Component
{
    /// <summary>
    /// Number of mismatched ID cards required for completion.
    /// </summary>
    [DataField]
    public int RequiredCount = 1;

    /// <summary>
    /// When true, only IDs originally belonging to command staff qualify.
    /// </summary>
    [DataField]
    public bool FilterToCommandRecords;

    /// <summary>
    /// Mismatch detection mode.
    /// </summary>
    [DataField]
    public JobMismatchMode MismatchMode = JobMismatchMode.AnyDifference;

    /// <summary>
    /// Dirty-domain cache key for re-evaluation tracking.
    /// </summary>
    [ViewVariables]
    public string CacheKey = string.Empty;

    /// <summary>
    /// Snapshots of original station records at assignment time, keyed by record key.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, string> SnapshottedRecords = new();

    /// <summary>
    /// Tracked ID card holders mapped to their original job title.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, string> TrackedHolders = new();
}

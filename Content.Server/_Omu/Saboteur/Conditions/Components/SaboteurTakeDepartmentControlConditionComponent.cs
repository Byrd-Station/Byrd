// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Robust.Shared.GameObjects;

using Content.Server._Omu.Saboteur.Conditions.Systems;

namespace Content.Server._Omu.Saboteur.Conditions.Components;

/// <summary>
/// Objective condition requiring the saboteur to make station announcements from
/// a department communications console.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurDepartmentControlConditionSystem))]
public sealed partial class SaboteurDepartmentControlConditionComponent : Component
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
    /// The access tag assigned at objective-assignment time.
    /// The saboteur must use a console whose reader contains this tag.
    /// </summary>
    [ViewVariables]
    public string AssignedDepartmentAccessTag = string.Empty;

    /// <summary>
    /// Dirty-domain cache key for re-evaluation tracking.
    /// </summary>
    [ViewVariables]
    public string CacheKey = string.Empty;
}

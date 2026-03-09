// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;
using Robust.Shared.GameObjects;

using Content.Server._Omu.Saboteur.Conditions.Systems;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Components;

/// <summary>
/// Objective condition requiring the saboteur to subvert a station AI's laws.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurSubvertAIConditionSystem))]
public sealed partial class SaboteurSubvertAIConditionComponent : Component
{
    /// <summary>
    /// Display name snapshot of the saboteur taken at assignment time.
    /// </summary>
    [ViewVariables]
    public string SnapshottedName = string.Empty;

    /// <summary>
    /// Pre-compiled whole-word regex for the snapshotted name, cached at assignment
    /// to avoid recompilation on every progress check.
    /// </summary>
    [ViewVariables]
    public Regex? CachedNamePattern;

    /// <summary>
    /// Dirty-domain cache key for re-evaluation tracking.
    /// </summary>
    [ViewVariables]
    public string CacheKey = string.Empty;
}

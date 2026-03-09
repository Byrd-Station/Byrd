// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Robust.Shared.GameObjects;

using Content.Server._Omu.Saboteur.Conditions.Systems;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Components;

/// <summary>
/// Objective condition requiring the saboteur to create a chain of N crew members
/// with modified criminal records in succession.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurChainOfFoolsConditionSystem))]
public sealed partial class SaboteurChainOfFoolsConditionComponent : Component
{
    /// <summary>
    /// Number of chained record modifications required.
    /// </summary>
    [DataField(required: true)]
    public int RequiredCount;

    /// <summary>
    /// Dirty-domain cache key for re-evaluation tracking.
    /// </summary>
    [ViewVariables]
    public string CacheKey = string.Empty;
}

// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Robust.Shared.GameObjects;

using Content.Server._Omu.Saboteur.Conditions.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Components;

/// <summary>
/// Objective condition requiring the saboteur to emag (subvert) cyborg entities.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurEmagBorgConditionSystem))]
public sealed partial class SaboteurEmagBorgConditionComponent : Component
{
    /// <summary>
    /// Dirty-domain cache key for re-evaluation tracking.
    /// </summary>
    [ViewVariables]
    public string CacheKey = string.Empty;

    /// <summary>
    /// Set of borg entities this saboteur has successfully emagged.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> EmaggedByMe = new();
}

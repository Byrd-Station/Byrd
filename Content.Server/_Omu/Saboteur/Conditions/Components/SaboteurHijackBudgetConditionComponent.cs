// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

using Content.Server._Omu.Saboteur.Conditions.Systems;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Components;

/// <summary>
/// Objective condition requiring the saboteur to drain or redirect a department's cargo budget.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurHijackBudgetConditionSystem))]
public sealed partial class SaboteurHijackBudgetConditionComponent : Component
{
    /// <summary>
    /// Maps departments to specific cargo accounts to target instead of the default.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<DepartmentPrototype>, ProtoId<CargoAccountPrototype>> DepartmentOverrideMap = new();

    /// <summary>
    /// Snapshot of budget balances at assignment time, used to measure how much was drained.
    /// </summary>
    [ViewVariables]
    public Dictionary<ProtoId<CargoAccountPrototype>, int> BudgetAtAssignment = new();

    /// <summary>
    /// Dirty-domain cache key for re-evaluation tracking.
    /// </summary>
    [ViewVariables]
    public string CacheKey = string.Empty;
}

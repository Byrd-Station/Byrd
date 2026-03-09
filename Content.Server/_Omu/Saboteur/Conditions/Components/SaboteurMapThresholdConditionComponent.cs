// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Server._Omu.Saboteur.Conditions;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

using Content.Server._Omu.Saboteur.Conditions.Systems;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Components;

/// <summary>
/// Objective condition based on reaching a sabotage threshold across map entities
/// (e.g., disabling a percentage of power sources, cameras, or door bolts).
/// </summary>
[RegisterComponent, Access(typeof(SaboteurMapThresholdConditionSystem))]
public sealed partial class SaboteurMapThresholdConditionComponent : Component
{
    /// <summary>
    /// Engine component type name that identifies target entities on the map.
    /// </summary>
    [DataField(required: true, customTypeSerializer: typeof(ComponentNameSerializer))]
    public string TargetComponent = string.Empty;

    /// <summary>
    /// How to evaluate whether a target entity is considered sabotaged.
    /// </summary>
    [DataField]
    public SabotageMode SabotageCheck;

    /// <summary>
    /// Fraction of targets (0.0–1.0) that must be sabotaged for the condition to pass.
    /// </summary>
    [DataField]
    public float Threshold;

    /// <summary>
    /// When true, group targets by department and requires <see cref="MinGroupCount"/>
    /// departments to individually exceed the threshold.
    /// </summary>
    [DataField]
    public bool GroupByDepartment;

    /// <summary>
    /// Minimum number of department groups that must exceed the threshold.
    /// Only used when <see cref="GroupByDepartment"/> is true.
    /// </summary>
    [DataField]
    public int MinGroupCount;

    /// <summary>
    /// Optional secondary component type for compound conditions.
    /// </summary>
    [DataField(customTypeSerializer: typeof(ComponentNameSerializer))]
    public string? SecondaryTargetComponent;

    /// <summary>
    /// How to evaluate whether a secondary target is considered sabotaged.
    /// </summary>
    [DataField]
    public SabotageMode SecondarySabotageCheck;

    /// <summary>
    /// Optional encryption channel filter for secondary targets.
    /// </summary>
    [DataField]
    public string? SecondaryChannelFilter;

    /// <summary>
    /// Entities assigned as primary targets at snapshot time.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> AssignedTargets = new();

    /// <summary>
    /// Entities assigned as secondary targets at snapshot time.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> SecondaryTargets = new();

    /// <summary>
    /// Dirty-domain cache key for re-evaluation tracking.
    /// </summary>
    [ViewVariables]
    public string CacheKey = string.Empty;

    /// <summary>
    /// The randomly chosen target department the saboteur must lock down.
    /// Only set when <see cref="GroupByDepartment"/> is true.
    /// </summary>
    [ViewVariables]
    public ProtoId<DepartmentPrototype>? TargetDepartment;

    /// <summary>
    /// Maps department to the set of door entities in that department.
    /// </summary>
    [ViewVariables]
    public Dictionary<ProtoId<DepartmentPrototype>, HashSet<EntityUid>> DepartmentDoors = new();
}

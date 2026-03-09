// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared._Omu.Saboteur;
using Content.Shared.Roles;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

using Content.Server._Omu.Saboteur.Systems;

namespace Content.Server._Omu.Saboteur.Components;

/// <summary>
/// Attached to a saboteur's mind entity to track reputation, tier, exposure, and
/// completed operations for the duration of the round.
/// All fields are runtime-only (not serialized) — they are populated and mutated
/// exclusively by <see cref="SaboteurRuleSystem"/> and <see cref="SaboteurOperationSystem"/>.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurRuleSystem), typeof(SaboteurOperationSystem))]
public sealed partial class SaboteurMindComponent : Component
{
    /// <summary>
    /// Current accumulated reputation score.
    /// Runtime-only: computed by the operation system when objectives are completed.
    /// </summary>
    [DataField, ViewVariables]
    public int Reputation;

    /// <summary>
    /// Set of operation IDs this saboteur has already completed.
    /// Runtime-only: populated as the saboteur finishes operations during the round.
    /// </summary>
    [DataField, ViewVariables]
    public HashSet<ProtoId<SaboteurOperationPrototype>> CompletedOperations = new();

    /// <summary>
    /// Current reputation tier (derived from <see cref="Reputation"/> vs thresholds).
    /// Runtime-only: recalculated whenever <see cref="Reputation"/> changes.
    /// </summary>
    [DataField, ViewVariables]
    public int ReputationTier;

    /// <summary>
    /// Highest criminal record status ever observed for this saboteur.
    /// Used to compute the permanent exposure penalty.
    /// Runtime-only: updated by the rule system when security status changes.
    /// </summary>
    [DataField, ViewVariables]
    public SecurityStatus HighestObservedStatus = SecurityStatus.None;

    /// <summary>
    /// Reputation gain multiplier based on exposure (1.0 = clean, lower = penalised).
    /// Runtime-only: derived from <see cref="HighestObservedStatus"/>.
    /// </summary>
    [DataField, ViewVariables]
    public float ExposurePenaltyMultiplier = 1.0f;

    /// <summary>
    /// Whether this saboteur has been assigned the "Die a Glorious Death" objective.
    /// Runtime-only: set during objective assignment.
    /// </summary>
    [DataField, ViewVariables]
    public bool GloriousDeathActive;

    /// <summary>
    /// The department the saboteur was in when they were selected, used to exclude
    /// their own department from certain objectives.
    /// Runtime-only: captured at antag-selection time.
    /// </summary>
    [DataField, ViewVariables]
    public ProtoId<DepartmentPrototype>? StartingDepartment;

    /// <summary>
    /// The station record key captured at assignment time, used for exposure
    /// tracking so that identity changes cannot evade criminal record checks.
    /// Runtime-only: captured during antag assignment.
    /// </summary>
    [DataField, ViewVariables]
    public StationRecordKey? OriginalRecordKey;

    /// <summary>
    /// The entity UID of this saboteur's uplink, used to grant TC.
    /// Runtime-only: set when the uplink is created during antag assignment.
    /// </summary>
    [DataField, ViewVariables]
    public EntityUid? UplinkEntity;
}

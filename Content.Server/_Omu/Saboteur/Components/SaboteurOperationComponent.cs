// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared._Omu.Saboteur;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

using Content.Server._Omu.Saboteur.Systems;

namespace Content.Server._Omu.Saboteur.Components;

/// <summary>
/// Attached to a saboteur objective entity, linking it to its
/// <see cref="SaboteurOperationPrototype"/> and defining tier, reputation gain, and severity.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurConditionCoreSystem), typeof(SaboteurOperationSystem))]
public sealed partial class SaboteurOperationComponent : Component
{
    /// <summary>
    /// Which operation prototype this objective represents.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<SaboteurOperationPrototype> OperationId;

    /// <summary>
    /// Tier at which this operation becomes available.
    /// </summary>
    [DataField]
    public int Tier;

    /// <summary>
    /// How much reputation completing this operation awards.
    /// </summary>
    [DataField]
    public int ReputationGain = 10;

    /// <summary>
    /// Whether this operation is considered "major" (grants higher TC rewards).
    /// </summary>
    [DataField]
    public bool IsMajor;
}

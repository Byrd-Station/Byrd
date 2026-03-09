// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared.Contraband;
using Robust.Shared.Prototypes;

using Content.Server._Omu.Saboteur.Conditions.Systems;

namespace Content.Server._Omu.Saboteur.Conditions.Components;

/// <summary>
/// Objective condition requiring the saboteur to plant contraband evidence on targeted crew members.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurPlantEvidenceConditionSystem))]
public sealed partial class SaboteurPlantEvidenceConditionComponent : Component
{
    /// <summary>
    /// Minimum contraband severity the planted evidence must have.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<ContrabandSeverityPrototype> ContrabandSeverity;
}

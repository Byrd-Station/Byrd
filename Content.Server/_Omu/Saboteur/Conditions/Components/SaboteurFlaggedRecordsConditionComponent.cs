// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Server._Omu.Saboteur.Conditions.Systems;

namespace Content.Server._Omu.Saboteur.Conditions.Components;

/// <summary>
/// Objective condition requiring the saboteur to flag crew station records (e.g., set criminal status).
/// </summary>
[RegisterComponent, Access(typeof(SaboteurFlaggedRecordsConditionSystem))]
public sealed partial class SaboteurFlaggedRecordsConditionComponent : Component
{
    /// <summary>
    /// Number of records that must be flagged.
    /// </summary>
    [DataField(required: true)]
    public int RequiredCount;

    /// <summary>
    /// When true, only command-level records count toward completion.
    /// </summary>
    [DataField]
    public bool CommandOnly;
}

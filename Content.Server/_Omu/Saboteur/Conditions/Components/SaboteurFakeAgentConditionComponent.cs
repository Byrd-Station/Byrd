// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

using Content.Server._Omu.Saboteur.Conditions.Systems;
using Content.Server._Omu.Saboteur.Conditions;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Components;

/// <summary>
/// Objective condition requiring the saboteur to establish fake agents
/// by placing mindshield-faking implants on crew members.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurFakeAgentConditionSystem))]
public sealed partial class SaboteurFakeAgentConditionComponent : Component
{
    /// <summary>
    /// Strategy for what constitutes a successful fake agent placement.
    /// </summary>
    [DataField]
    public FakeAgentMode Mode = FakeAgentMode.CoverAllJobs;

    /// <summary>
    /// Specific job prototypes to target; only used in certain <see cref="FakeAgentMode"/> values.
    /// </summary>
    [DataField]
    public List<ProtoId<JobPrototype>> TargetJobs = new();

    /// <summary>
    /// Dirty-domain cache key for re-evaluation tracking.
    /// </summary>
    [ViewVariables]
    public string CacheKey = string.Empty;
}

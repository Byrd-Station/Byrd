// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared.Roles;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.ViewVariables;

using Content.Server._Omu.Saboteur.Systems;

namespace Content.Server._Omu.Saboteur.Components;

/// <summary>
/// Stores department-related configuration for saboteur objectives:
/// access tags, head-of-department mappings, and runtime job-to-department lookups.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurDepartmentConfigSystem), Other = AccessPermissions.ReadExecute)]
public sealed partial class SaboteurDepartmentConfigComponent : Component
{
    /// <summary>
    /// Maps an access tag (e.g. "Engineering") to its department ID.
    /// </summary>
    [DataField]
    public Dictionary<string, ProtoId<DepartmentPrototype>> DepartmentAccessTags = new();

    /// <summary>
    /// Maps a primary department name to its head-of-department job prototype
    /// (e.g. "Engineering" → "ChiefEngineer").
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<DepartmentPrototype>, ProtoId<JobPrototype>> DepartmentHeadMap = new();

    /// <summary>
    /// The access tag that identifies command-level access.
    /// </summary>
    [DataField]
    public string CommandAccessTag = "Command";

    /// <summary>
    /// NavMap beacon keyword used to identify the bridge area.
    /// </summary>
    [DataField]
    public string BridgeBeaconKeyword = "Bridge";

    /// <summary>
    /// Prototype ID of the command department, used to populate <see cref="CommandJobs"/>.
    /// </summary>
    [DataField]
    public ProtoId<DepartmentPrototype> CommandDepartmentId = "Command";

    /// <summary>
    /// Runtime-populated set of job IDs that belong to the command department.
    /// </summary>
    [ViewVariables]
    public HashSet<string> CommandJobs = new();

    /// <summary>
    /// Runtime-populated map of job prototype ID → primary department ID.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, ProtoId<DepartmentPrototype>> JobToDepartment = new();
}

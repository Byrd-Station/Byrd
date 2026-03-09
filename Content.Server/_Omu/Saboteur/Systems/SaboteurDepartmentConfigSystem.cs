// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using System.Diagnostics.CodeAnalysis;

using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Systems;

/// <summary>
/// Initialises <see cref="SaboteurDepartmentConfigComponent"/> at round start by
/// populating job-to-department and command-job lookup tables from department prototypes.
/// </summary>
public sealed class SaboteurDepartmentConfigSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private EntityQuery<SaboteurDepartmentConfigComponent> _deptQuery;

    /// <summary>
    /// Cached department config entity UID — set on <see cref="ComponentInit"/>,
    /// cleared on <see cref="ComponentRemove"/>. Avoids per-call enumeration.
    /// </summary>
    private EntityUid? _cachedDeptUid;

    public override void Initialize()
    {
        base.Initialize();
        _deptQuery = GetEntityQuery<SaboteurDepartmentConfigComponent>();

        SubscribeLocalEvent<SaboteurDepartmentConfigComponent, ComponentInit>(OnDeptConfigInit);
        SubscribeLocalEvent<SaboteurDepartmentConfigComponent, ComponentRemove>(OnDeptConfigRemove);
    }

    /// <summary>
    /// Looks up the active department config using the cached UID and a
    /// pre-resolved <see cref="EntityQuery{T}"/> for fast access.
    /// </summary>
    public bool TryGetDeptConfig([NotNullWhen(true)] out SaboteurDepartmentConfigComponent? dept)
    {
        if (_cachedDeptUid is { } uid && _deptQuery.TryGetComponent(uid, out dept))
            return true;

        dept = null;
        return false;
    }

    private void OnDeptConfigRemove(EntityUid uid, SaboteurDepartmentConfigComponent comp, ComponentRemove args)
    {
        if (_cachedDeptUid == uid)
            _cachedDeptUid = null;
    }

    private void OnDeptConfigInit(EntityUid uid, SaboteurDepartmentConfigComponent dept, ComponentInit args)
    {
        if (_cachedDeptUid != null)
            Log.Warning($"Multiple SaboteurDepartmentConfigComponent entities detected ({ToPrettyString(_cachedDeptUid.Value)} and {ToPrettyString(uid)}). Only one should exist.");

        _cachedDeptUid = uid;
        dept.CommandJobs.Clear();
        dept.JobToDepartment.Clear();

        foreach (var deptProto in _prototypeManager.EnumeratePrototypes<DepartmentPrototype>())
        {
            if (!deptProto.Primary)
                continue;

            foreach (var role in deptProto.Roles)
            {
                dept.JobToDepartment[role] = deptProto.ID;
            }
        }

        if (_prototypeManager.TryIndex<DepartmentPrototype>(dept.CommandDepartmentId, out var commandDept))
        {
            foreach (var role in commandDept.Roles)
                dept.CommandJobs.Add(role);
        }
        else
        {
            Log.Warning($"Could not find department prototype '{dept.CommandDepartmentId}' for saboteur checks");
        }
    }

    /// <summary>
    /// Returns whether the given job prototype is classified as a command job
    /// in the department configuration.
    /// </summary>
    public bool IsCommandJob(string jobPrototype)
    {
        if (!TryGetDeptConfig(out var dept))
            return false;

        return dept.CommandJobs.Contains(jobPrototype);
    }
}

// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.CriminalRecords;
using Content.Shared.Objectives.Components;
using Content.Shared.Security;
using Content.Shared.StationRecords;

using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Systems;

/// <summary>
/// Evaluates flagged-records objectives by inspecting security record statuses of targeted crew.
/// </summary>
public sealed class SaboteurFlaggedRecordsConditionSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;
    [Dependency] private readonly SaboteurDepartmentConfigSystem _deptConfig = default!;
    [Dependency] private readonly SaboteurCrewTargetingSystem _crewTargeting = default!;
    [Dependency] private readonly StationRecordsSystem _stationRecords = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaboteurFlaggedRecordsConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<SaboteurFlaggedRecordsConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign,
            after: new[] { typeof(SaboteurOperationSystem) });
        SubscribeLocalEvent<SaboteurFlaggedRecordsConditionComponent, RequirementCheckEvent>(OnRequirementCheck);
    }

    private void OnGetProgress(EntityUid uid, SaboteurFlaggedRecordsConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_core.TryBeginProgressCheck(uid, ref args, out _, out var dirty))
            return;

        if (!TryComp<SaboteurCrewTargetDataComponent>(uid, out var data))
            return;

        var cacheKey = data.CacheKey;
        if (_core.TryGetCached(dirty, uid, cacheKey, out var cached))
        {
            args.Progress = cached;
            return;
        }

        if (data.FlagTargets.Count == 0)
        {
            args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 0f);
            return;
        }

        if (_deptConfig.TryGetDeptConfig(out var dept))
        {
            _crewTargeting.TryRepickDeletedTargets(
                data.FlagTargets,
                args.MindId,
                static (ct, state) =>
                {
                    if (ct.IdCard.FullName == null)
                        return false;

                    if (state.Records.TryGetRecord<GeneralStationRecord>(ct.Key, out var genRecord)
                        && !string.IsNullOrEmpty(genRecord.JobPrototype))
                    {
                        return state.DeptConfig.IsCommandJob(genRecord.JobPrototype) == state.CommandOnly;
                    }

                    return false;
                },
                (Records: _stationRecords, Dept: dept, CommandOnly: comp.CommandOnly, Core: _core, DeptConfig: _deptConfig));
        }

        int count = 0;
        foreach (var target in data.FlagTargets)
        {
            if (!EntityManager.EntityExists(target))
                continue;

            if (!_idCard.TryFindIdCard(target, out var idCard))
                continue;

            if (!TryComp<StationRecordKeyStorageComponent>(idCard, out var keyStorage)
                || keyStorage.Key is not { } key)
                continue;

            if (!_stationRecords.TryGetRecord<CriminalRecord>(key, out var criminal))
                continue;

            if (criminal.Status is not (SecurityStatus.Wanted or SecurityStatus.Detained))
                continue;

            if (string.Equals(criminal.InitiatorName, data.SnapshottedName, StringComparison.OrdinalIgnoreCase))
                continue;

            count++;
        }

        args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey,
            _core.CountProgress(count, comp.RequiredCount));
    }

    private void OnAfterAssign(EntityUid uid, SaboteurFlaggedRecordsConditionComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        if (!_core.TryGetDirtyTracking(out var dirty))
            return;

        _crewTargeting.InitializeAssignment(uid, args, dirty,
            SaboteurDirtyDomain.Records | SaboteurDirtyDomain.Entity);

        if (TryComp<SaboteurCrewTargetDataComponent>(uid, out var data))
        {
            _crewTargeting.UpdateCrewTargetDescription(uid, data, args.Meta,
                static names => new[] { ("targets", (object) string.Join(", ", names)) });
        }
    }

    private void OnRequirementCheck(EntityUid uid, SaboteurFlaggedRecordsConditionComponent comp, ref RequirementCheckEvent args)
    {
        if (args.Cancelled)
            return;

        if (!_core.TryGetRule(out _))
        {
            args.Cancelled = true;
            return;
        }

        if (!_deptConfig.TryGetDeptConfig(out var dept))
        {
            args.Cancelled = true;
            return;
        }

        if (!_crewTargeting.TryPickCrewTargets(
                args.MindId,
                args.Mind.OwnedEntity,
                EnsureComp<SaboteurCrewTargetDataComponent>(uid).FlagTargets,
                comp.RequiredCount,
                static (ct, state) =>
                {
                    if (ct.IdCard.FullName == null)
                        return false;

                    if (state.Records.TryGetRecord<GeneralStationRecord>(ct.Key, out var genRecord)
                        && !string.IsNullOrEmpty(genRecord.JobPrototype))
                    {
                        return state.DeptConfig.IsCommandJob(genRecord.JobPrototype) == state.CommandOnly;
                    }

                    return false;
                },
                (Records: _stationRecords, Dept: dept, CommandOnly: comp.CommandOnly, Core: _core, DeptConfig: _deptConfig)))
        {
            args.Cancelled = true;
        }
    }
}

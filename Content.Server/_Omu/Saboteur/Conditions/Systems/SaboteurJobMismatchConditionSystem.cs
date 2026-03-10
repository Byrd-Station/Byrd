// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Objectives.Components;
using Content.Shared.StationRecords;

using Content.Server._Omu.Saboteur.Conditions;
using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Systems;

/// <summary>
/// Evaluates job-mismatch objectives by comparing ID card job titles against
/// original station records.
/// </summary>
public sealed class SaboteurJobMismatchConditionSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;
    [Dependency] private readonly SaboteurDepartmentConfigSystem _deptConfig = default!;
    [Dependency] private readonly SaboteurCrewTargetingSystem _crewTargeting = default!;
    [Dependency] private readonly StationRecordsSystem _stationRecords = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;

    /// <remarks>Not reentrant-safe - must not be used from callbacks that also use this buffer.</remarks>
    private readonly List<CrewTarget> _scratchCrewTargets = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaboteurJobMismatchConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<SaboteurJobMismatchConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
        SubscribeLocalEvent<SaboteurJobMismatchConditionComponent, RequirementCheckEvent>(OnRequirementCheck);
    }

    private void OnGetProgress(EntityUid uid, SaboteurJobMismatchConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_core.TryBeginProgressCheck(uid, ref args, out var saboteur, out var dirty))
            return;

        if (!_deptConfig.TryGetDeptConfig(out var dept))
            return;

        var cacheKey = comp.CacheKey;
        if (_core.TryGetCached(dirty, uid, cacheKey, out var cached))
        {
            args.Progress = cached;
            return;
        }

        int mismatchCount = 0;

        foreach (var (holderUid, originalJob) in comp.TrackedHolders)
        {
            if (!EntityManager.EntityExists(holderUid))
                continue;

            if (holderUid == saboteur)
                continue;

            if (!_idCard.TryFindIdCard(holderUid, out var idCard))
                continue;

            var currentJob = idCard.Comp.JobPrototype?.Id;
            if (currentJob == null)
                continue;

            var isMismatch = comp.MismatchMode switch
            {
                JobMismatchMode.AnyDifference => originalJob != currentJob,
                JobMismatchMode.DemotedFromCommand => !_deptConfig.IsCommandJob(currentJob),
                _ => originalJob != currentJob,
            };

            if (isMismatch)
                mismatchCount++;
        }

        args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, _core.CountProgress(mismatchCount, comp.RequiredCount));
    }

    private void OnAfterAssign(EntityUid uid, SaboteurJobMismatchConditionComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        if (!_core.TryGetDirtyTracking(out var dirty))
            return;

        if (!_deptConfig.TryGetDeptConfig(out var dept))
            return;

        if (comp.MismatchMode == JobMismatchMode.AnyDifference)
        {
            comp.CacheKey = _core.MakeCacheKey(uid);
            var data = EnsureComp<SaboteurCrewTargetDataComponent>(uid);
            _crewTargeting.InitializeAssignment(uid, args, dirty,
                SaboteurDirtyDomain.Records | SaboteurDirtyDomain.IdCard);

            comp.TrackedHolders.Clear();
            foreach (var holderUid in data.FlagTargets)
            {
                if (!_idCard.TryFindIdCard(holderUid, out var card))
                    continue;

                var originalJob = card.Comp.JobPrototype?.Id;
                if (!string.IsNullOrEmpty(originalJob))
                    comp.TrackedHolders[holderUid] = originalJob;
            }

            _crewTargeting.UpdateCrewTargetDescription(uid, data, args.Meta,
                static names => new[] { ("targets", (object) string.Join(", ", names)) });
            return;
        }

        // DemotedFromCommand: broad tracking of all command crew
        comp.CacheKey = _core.MakeCacheKey(uid);
        _core.RegisterInterest(dirty, uid, SaboteurDirtyDomain.Records | SaboteurDirtyDomain.IdCard);

        comp.SnapshottedRecords.Clear();
        foreach (var station in _station.GetStations())
        {
            if (!TryComp<StationRecordsComponent>(station, out var records))
                continue;

            foreach (var (_, record) in _stationRecords.GetRecordsOfType<GeneralStationRecord>(station, records))
            {
                if (string.IsNullOrEmpty(record.Name) || string.IsNullOrEmpty(record.JobPrototype))
                    continue;

                if (comp.FilterToCommandRecords && !_deptConfig.IsCommandJob(record.JobPrototype))
                    continue;

                comp.SnapshottedRecords[record.Name] = record.JobPrototype;
            }
        }

        comp.TrackedHolders.Clear();
        var idQuery = EntityQueryEnumerator<IdCardComponent>();
        while (idQuery.MoveNext(out var entUid, out var idCard))
        {
            if (idCard.FullName == null)
                continue;

            if (!comp.SnapshottedRecords.TryGetValue(idCard.FullName, out var originalJob))
                continue;

            var holder = _crewTargeting.GetCardHolder(entUid);
            if (holder == null)
                continue;

            comp.TrackedHolders.TryAdd(holder.Value, originalJob);
        }
    }

    private void OnRequirementCheck(EntityUid uid, SaboteurJobMismatchConditionComponent comp, ref RequirementCheckEvent args)
    {
        if (args.Cancelled)
            return;

        if (!_deptConfig.TryGetDeptConfig(out var dept))
        {
            args.Cancelled = true;
            return;
        }

        if (comp.MismatchMode == JobMismatchMode.AnyDifference)
        {
            var data = EnsureComp<SaboteurCrewTargetDataComponent>(uid);
            if (!_crewTargeting.TryPickCrewTargets(
                    args.MindId,
                    args.Mind.OwnedEntity,
                    data.FlagTargets,
                    comp.RequiredCount,
                    static (ct, _) => ct.IdCard.FullName != null && ct.IdCard.JobPrototype != null,
                    0))
            {
                args.Cancelled = true;
            }
            return;
        }

        // DemotedFromCommand: verify candidate count without picking specific targets
        _scratchCrewTargets.Clear();
        _crewTargeting.CollectEligibleCrew(args.Mind.OwnedEntity, _scratchCrewTargets);

        int candidateCount = 0;
        foreach (var ct in _scratchCrewTargets)
        {
            if (ct.IdCard.FullName == null)
                continue;

            if (!_stationRecords.TryGetRecord<GeneralStationRecord>(ct.Key, out var genRecord)
                || string.IsNullOrEmpty(genRecord.JobPrototype))
                continue;

            if (comp.FilterToCommandRecords && !_deptConfig.IsCommandJob(genRecord.JobPrototype))
                continue;

            candidateCount++;
            if (candidateCount >= comp.RequiredCount)
            {
                _scratchCrewTargets.Clear();
                return;
            }
        }

        _scratchCrewTargets.Clear();
        args.Cancelled = true;
    }
}

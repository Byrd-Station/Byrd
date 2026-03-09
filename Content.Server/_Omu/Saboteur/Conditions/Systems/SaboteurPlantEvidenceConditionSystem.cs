// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Contraband;
using Content.Shared.CriminalRecords;
using Content.Shared.Implants.Components;
using Content.Shared.Objectives.Components;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Robust.Shared.Prototypes;

using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Systems;

/// <summary>
/// Evaluates plant-evidence objectives by checking whether targeted crew members
/// are carrying contraband of the required severity.
/// </summary>
public sealed class SaboteurPlantEvidenceConditionSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;
    [Dependency] private readonly SaboteurCrewTargetingSystem _crewTargeting = default!;
    [Dependency] private readonly StationRecordsSystem _stationRecords = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaboteurPlantEvidenceConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<SaboteurPlantEvidenceConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign,
            after: new[] { typeof(SaboteurOperationSystem) });
        SubscribeLocalEvent<SaboteurPlantEvidenceConditionComponent, RequirementCheckEvent>(OnRequirementCheck);
    }

    private void OnGetProgress(EntityUid uid, SaboteurPlantEvidenceConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_core.TryBeginProgressCheck(uid, ref args, out var saboteur, out var dirty))
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

        var target = data.FlagTargets[0];

        if (!EntityManager.EntityExists(target))
        {
            _crewTargeting.TryRepickDeletedTargets(
                data.FlagTargets,
                args.MindId,
                static (ct, state) => !state.System.HasContrabandImplant(ct.Holder, state.Severity),
                (System: this, Severity: (ProtoId<ContrabandSeverityPrototype>?) comp.ContrabandSeverity));

            if (data.FlagTargets.Count == 0 || !EntityManager.EntityExists(data.FlagTargets[0]))
            {
                args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 0f);
                return;
            }
            target = data.FlagTargets[0];
        }

        if (!HasContrabandImplant(target, comp.ContrabandSeverity))
        {
            args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 0f);
            return;
        }

        if (!_idCard.TryFindIdCard(target, out var idCard))
        {
            args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 0f);
            return;
        }

        if (!TryComp<StationRecordKeyStorageComponent>(idCard, out var keyStorage)
            || keyStorage.Key is not { } key)
        {
            args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 0f);
            return;
        }

        if (_stationRecords.TryGetRecord<CriminalRecord>(key, out var criminal)
            && criminal.Status == SecurityStatus.Suspected
            && !string.Equals(criminal.InitiatorName, data.SnapshottedName, StringComparison.OrdinalIgnoreCase))
        {
            args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 1f);
            return;
        }

        args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 0f);
    }

    private void OnAfterAssign(EntityUid uid, SaboteurPlantEvidenceConditionComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        if (!_core.TryGetDirtyTracking(out var dirty))
            return;

        _crewTargeting.InitializeAssignment(uid, args, dirty,
            SaboteurDirtyDomain.Implant | SaboteurDirtyDomain.Records | SaboteurDirtyDomain.Entity);

        if (TryComp<SaboteurCrewTargetDataComponent>(uid, out var data))
        {
            _crewTargeting.UpdateCrewTargetDescription(uid, data, args.Meta,
                static names => new[] { ("target", (object) names[0]) });
        }
    }

    private void OnRequirementCheck(EntityUid uid, SaboteurPlantEvidenceConditionComponent comp, ref RequirementCheckEvent args)
    {
        if (args.Cancelled)
            return;

        if (!_crewTargeting.TryPickCrewTargets(
                args.MindId,
                args.Mind.OwnedEntity,
                EnsureComp<SaboteurCrewTargetDataComponent>(uid).FlagTargets,
                1,
                static (ct, state) => !state.System.HasContrabandImplant(ct.Holder, state.Severity),
                (System: this, Severity: (ProtoId<ContrabandSeverityPrototype>?) comp.ContrabandSeverity)))
        {
            args.Cancelled = true;
        }
    }

    /// <summary>
    /// Checks whether <paramref name="target"/> has an implant whose contraband
    /// severity matches <paramref name="contrabandSeverity"/>.
    /// </summary>
    public bool HasContrabandImplant(EntityUid target, ProtoId<ContrabandSeverityPrototype>? contrabandSeverity)
    {
        if (contrabandSeverity == null)
            return false;

        if (!TryComp<ImplantedComponent>(target, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (TryComp<ContrabandComponent>(implant, out var contraband)
                && contraband.Severity == contrabandSeverity.Value)
            {
                return true;
            }
        }

        return false;
    }
}

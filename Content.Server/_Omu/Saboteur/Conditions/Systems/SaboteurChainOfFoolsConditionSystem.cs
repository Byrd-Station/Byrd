// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared.Access.Components;
using Content.Shared.Objectives.Components;
using Content.Goobstation.Shared.Mindcontrol;

using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Systems;

/// <summary>
/// Evaluates chain-of-fools objectives by counting crew members currently under
/// the saboteur's mind control simultaneously.
/// </summary>
public sealed class SaboteurChainOfFoolsConditionSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;
    [Dependency] private readonly SaboteurCrewTargetingSystem _crewTargeting = default!;

    private readonly List<CrewTarget> _scratchCrewTargets = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaboteurChainOfFoolsConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<SaboteurChainOfFoolsConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
        SubscribeLocalEvent<SaboteurChainOfFoolsConditionComponent, RequirementCheckEvent>(OnRequirementCheck);
    }

    private void OnGetProgress(EntityUid uid, SaboteurChainOfFoolsConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_core.TryBeginProgressCheck(uid, ref args, out var saboteur, out var dirty))
            return;

        var cacheKey = comp.CacheKey;
        if (_core.TryGetCached(dirty, uid, cacheKey, out var cached))
        {
            args.Progress = cached;
            return;
        }

        int controlledCount = 0;
        var query = EntityQueryEnumerator<MindcontrolledComponent>();
        while (query.MoveNext(out _, out var mc))
        {
            if (mc.Master == saboteur)
                controlledCount++;
        }

        args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey,
            _core.CountProgress(controlledCount, comp.RequiredCount));
    }

    private void OnAfterAssign(EntityUid uid, SaboteurChainOfFoolsConditionComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        if (!_core.TryGetDirtyTracking(out var dirty))
            return;

        comp.CacheKey = _core.MakeCacheKey(uid);
        _core.RegisterInterest(dirty, uid, SaboteurDirtyDomain.MindControl);
    }

    /// <summary>
    /// Prevents assigning the chain-of-fools objective when there aren't enough
    /// eligible crew members on station to mind-control.
    /// </summary>
    private void OnRequirementCheck(EntityUid uid, SaboteurChainOfFoolsConditionComponent comp, ref RequirementCheckEvent args)
    {
        if (args.Cancelled)
            return;

        _scratchCrewTargets.Clear();
        _crewTargeting.CollectEligibleCrew(args.Mind.OwnedEntity, _scratchCrewTargets);

        var eligible = _scratchCrewTargets.Count;
        _scratchCrewTargets.Clear();

        if (eligible < comp.RequiredCount)
            args.Cancelled = true;
    }
}

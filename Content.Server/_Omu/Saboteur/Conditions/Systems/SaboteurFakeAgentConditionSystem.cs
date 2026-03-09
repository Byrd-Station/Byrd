// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Mindshield.Components;
using Content.Shared.Objectives.Components;
using Content.Shared.Roles;
using Content.Goobstation.Shared.Mindcontrol;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

using Content.Server._Omu.Saboteur.Conditions;
using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Systems;

/// <summary>
/// Evaluates fake-agent objectives by checking for fake mindshield implants on crew members.
/// </summary>
public sealed class SaboteurFakeAgentConditionSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;
    [Dependency] private readonly SaboteurDepartmentConfigSystem _deptConfig = default!;
    [Dependency] private readonly SaboteurCrewTargetingSystem _crewTargeting = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;

    /// <remarks>Not reentrant-safe — must not be used from callbacks that also use this buffer.</remarks>
    private readonly HashSet<string> _scratchStringSet = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaboteurFakeAgentConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<SaboteurFakeAgentConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
        SubscribeLocalEvent<SaboteurFakeAgentConditionComponent, RequirementCheckEvent>(OnRequirementCheck);
    }

    private void OnGetProgress(EntityUid uid, SaboteurFakeAgentConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_core.TryBeginProgressCheck(uid, ref args, out var saboteur, out var dirty))
            return;

        var cacheKey = comp.CacheKey;
        if (_core.TryGetCached(dirty, uid, cacheKey, out var cached))
        {
            args.Progress = cached;
            return;
        }

        switch (comp.Mode)
        {
            case FakeAgentMode.SelfSoleHolder:
                args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, CheckSelfSoleHolder(saboteur, comp));
                return;
            case FakeAgentMode.PuppetInJob:
                args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, CheckPuppetInJob(saboteur, comp));
                return;
            case FakeAgentMode.CoverAllJobs:
                args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, CheckCoverAllJobs(comp));
                return;
            default:
                args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 0f);
                return;
        }
    }

    private float CheckSelfSoleHolder(EntityUid saboteur, SaboteurFakeAgentConditionComponent comp)
    {
        if (comp.TargetJobs.Count == 0)
            return 0f;

        var requiredJob = comp.TargetJobs[0];

        // Guard against TargetJobs being cleared between checks (e.g. admin reassignment)
        if (!HasComp<FakeMindShieldComponent>(saboteur))
            return 0f;

        if (!_idCard.TryFindIdCard(saboteur, out var idCard))
            return 0f;

        if (idCard.Comp.JobPrototype?.Id != requiredJob.Id)
            return 0f;

        var idQuery = EntityQueryEnumerator<IdCardComponent>();
        while (idQuery.MoveNext(out var otherUid, out var otherIdCard))
        {
            if (otherUid == idCard.Owner)
                continue;

            if (_crewTargeting.GetCardHolder(otherUid) == null)
                continue;

            if (otherIdCard.JobPrototype?.Id == requiredJob.Id)
                return 0f;
        }

        return 1f;
    }

    private float CheckPuppetInJob(EntityUid saboteur, SaboteurFakeAgentConditionComponent comp)
    {
        if (comp.TargetJobs.Count == 0)
            return 0f;

        var targetJob = comp.TargetJobs[0];

        var mcQuery = EntityQueryEnumerator<MindcontrolledComponent>();
        while (mcQuery.MoveNext(out var puppetUid, out var mc))
        {
            if (puppetUid == saboteur || mc.Master != saboteur)
                continue;

            if (!HasComp<FakeMindShieldComponent>(puppetUid))
                continue;

            if (!_idCard.TryFindIdCard(puppetUid, out var puppetIdCard))
                continue;

            if (puppetIdCard.Comp.JobPrototype?.Id == targetJob.Id)
                return 1f;
        }

        return 0f;
    }

    private float CheckCoverAllJobs(SaboteurFakeAgentConditionComponent comp)
    {
        if (comp.TargetJobs.Count == 0)
            return 0f;

        DebugTools.Assert(_scratchStringSet.Count == 0, "Reentrancy: _scratchStringSet not empty");
        _scratchStringSet.Clear();
        foreach (var job in comp.TargetJobs)
            _scratchStringSet.Add(job);

        var requiredCount = comp.TargetJobs.Count;
        int matchCount = 0;

        var query = EntityQueryEnumerator<FakeMindShieldComponent>();
        while (query.MoveNext(out var entUid, out _))
        {
            if (!_idCard.TryFindIdCard(entUid, out var idCard))
                continue;

            var job = idCard.Comp.JobPrototype?.Id;
            if (job == null)
                continue;

            if (_scratchStringSet.Remove(job))
                matchCount++;

            if (matchCount >= requiredCount)
            {
                _scratchStringSet.Clear();
                return 1f;
            }
        }

        _scratchStringSet.Clear();
        return _core.CountProgress(matchCount, requiredCount);
    }

    private void OnAfterAssign(EntityUid uid, SaboteurFakeAgentConditionComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        if (!_core.TryGetDirtyTracking(out var dirty))
            return;

        if (!_deptConfig.TryGetDeptConfig(out var dept))
            return;

        comp.CacheKey = _core.MakeCacheKey(uid);

        switch (comp.Mode)
        {
            case FakeAgentMode.SelfSoleHolder:

                if (TryComp<SaboteurMindComponent>(args.Mind.Owner, out var mindComp)
                    && mindComp.StartingDepartment is { } startDept)
                {
                    if (dept.DepartmentHeadMap.TryGetValue(startDept, out var headJob))
                    {
                        comp.TargetJobs = new List<ProtoId<JobPrototype>> { headJob };
                    }
                }

                _core.RegisterInterest(dirty, uid, SaboteurDirtyDomain.IdCard | SaboteurDirtyDomain.FakeMindShield);
                break;

            case FakeAgentMode.PuppetInJob:
                _core.RegisterInterest(dirty, uid, SaboteurDirtyDomain.MindControl | SaboteurDirtyDomain.FakeMindShield | SaboteurDirtyDomain.IdCard);
                break;

            case FakeAgentMode.CoverAllJobs:
                _core.RegisterInterest(dirty, uid, SaboteurDirtyDomain.FakeMindShield | SaboteurDirtyDomain.IdCard);
                break;
        }
    }

    private void OnRequirementCheck(EntityUid uid, SaboteurFakeAgentConditionComponent comp, ref RequirementCheckEvent args)
    {
        if (args.Cancelled)
            return;

        switch (comp.Mode)
        {
            case FakeAgentMode.SelfSoleHolder:
                if (!TryComp<SaboteurMindComponent>(args.Mind.Owner, out var mindComp)
                    || mindComp.StartingDepartment is not { } reqDept)
                {
                    args.Cancelled = true;
                    return;
                }

                if (!_core.TryGetRule(out _)
                    || !_deptConfig.TryGetDeptConfig(out var deptCfg)
                    || !deptCfg.DepartmentHeadMap.ContainsKey(reqDept))
                {
                    args.Cancelled = true;
                }
                break;

            case FakeAgentMode.PuppetInJob:
            case FakeAgentMode.CoverAllJobs:
                if (comp.TargetJobs.Count == 0)
                    args.Cancelled = true;
                break;
        }
    }
}

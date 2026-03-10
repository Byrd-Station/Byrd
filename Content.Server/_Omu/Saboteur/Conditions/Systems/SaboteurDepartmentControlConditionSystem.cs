// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq;
using Content.Server.Communications;
using Content.Shared.Access.Components;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;
using Content.Server._Omu.Saboteur;

namespace Content.Server._Omu.Saboteur.Conditions.Systems;

/// <summary>
/// Evaluates department-control objectives by tracking station announcements made
/// from a console whose access reader matches the assigned department access tag.
/// </summary>
public sealed class SaboteurDepartmentControlConditionSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;
    [Dependency] private readonly SaboteurDepartmentConfigSystem _deptConfig = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaboteurDepartmentControlConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<SaboteurDepartmentControlConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign,
            after: new[] { typeof(SaboteurOperationSystem) });
        SubscribeLocalEvent<SaboteurDepartmentControlConditionComponent, RequirementCheckEvent>(OnRequirementCheck);

        SubscribeLocalEvent<SaboteurAnnouncementMadeEvent>(OnAnnouncementMade);
    }

    private void OnGetProgress(EntityUid uid, SaboteurDepartmentControlConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_core.TryBeginProgressCheck(uid, ref args, out _, out var dirty))
            return;

        var cacheKey = comp.CacheKey;
        if (_core.TryGetCached(dirty, uid, cacheKey, out var cached))
        {
            args.Progress = cached;
            return;
        }

        args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey,
            _core.CountProgress(comp.AnnouncementCount, comp.RequiredCount));
    }

    private void OnAfterAssign(EntityUid uid, SaboteurDepartmentControlConditionComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        if (!_core.TryGetDirtyTracking(out var dirty))
            return;

        if (!_deptConfig.TryGetDeptConfig(out var dept) || dept.DepartmentAccessTags.Count == 0)
            return;

        comp.AssignedDepartmentAccessTag = _random.Pick(dept.DepartmentAccessTags.Keys.ToList());
        comp.CacheKey = _core.MakeCacheKey(uid);
        _core.RegisterInterest(dirty, uid, SaboteurDirtyDomain.Announcement);

        if (dept.DepartmentAccessTags.TryGetValue(comp.AssignedDepartmentAccessTag, out var deptProtoId)
            && TryComp<SaboteurOperationComponent>(uid, out var opComp))
        {
            var proto = _prototypeManager.Index(opComp.OperationId);
            var deptName = Loc.GetString($"department-{deptProtoId}");
            _metaData.SetEntityDescription(uid, Loc.GetString($"{proto.LocId}-desc", ("department", deptName)), args.Meta);
        }
    }

    private void OnRequirementCheck(EntityUid uid, SaboteurDepartmentControlConditionComponent comp, ref RequirementCheckEvent args)
    {
        if (args.Cancelled)
            return;

        if (!_deptConfig.TryGetDeptConfig(out var dept))
        {
            args.Cancelled = true;
            return;
        }

        var query = EntityQueryEnumerator<CommunicationsConsoleComponent, AccessReaderComponent>();
        while (query.MoveNext(out _, out _, out var reader))
        {
            foreach (var accessTag in dept.DepartmentAccessTags.Keys)
            {
                if (ReaderHasAccessTag(accessTag, reader))
                    return;
            }
        }

        args.Cancelled = true;
    }

    private void OnAnnouncementMade(ref SaboteurAnnouncementMadeEvent args)
    {
        if (!TryComp<AccessReaderComponent>(args.ConsoleUid, out var reader))
            return;

        foreach (var objUid in args.Mind.Objectives)
        {
            if (!TryComp<SaboteurDepartmentControlConditionComponent>(objUid, out var deptComp))
                continue;

            if (ReaderHasAccessTag(deptComp.AssignedDepartmentAccessTag, reader))
                deptComp.AnnouncementCount++;

            break;
        }
    }

    /// <summary>
    /// Checks whether the given access reader contains the specified access tag.
    /// </summary>
    public bool ReaderHasAccessTag(string accessTag, AccessReaderComponent reader)
    {
        foreach (var accessSet in reader.AccessLists)
        {
            if (accessSet.Contains(accessTag))
                return true;
        }
        return false;
    }
}

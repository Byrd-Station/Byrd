// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared.Emag.Components;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Shared.Silicons.Borgs.Components;

using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;
using Content.Server._Omu.Saboteur;

namespace Content.Server._Omu.Saboteur.Conditions.Systems;

/// <summary>
/// Evaluates emag-borg objectives by tracking which borgs the saboteur has subverted.
/// </summary>
public sealed class SaboteurEmagBorgConditionSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaboteurEmagBorgConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<SaboteurEmagBorgConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
        SubscribeLocalEvent<SaboteurEmagBorgConditionComponent, RequirementCheckEvent>(OnRequirementCheck);

        SubscribeLocalEvent<SaboteurBorgEmaggedByAgentEvent>(OnBorgEmaggedByAgent);
    }

    private void OnGetProgress(EntityUid uid, SaboteurEmagBorgConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_core.TryBeginProgressCheck(uid, ref args, out _, out var dirty))
            return;

        var cacheKey = comp.CacheKey;
        if (_core.TryGetCached(dirty, uid, cacheKey, out var cached))
        {
            args.Progress = cached;
            return;
        }

        foreach (var borgUid in comp.EmaggedByMe)
        {
            if (EntityManager.EntityExists(borgUid)
                && HasComp<BorgChassisComponent>(borgUid)
                && HasComp<EmaggedComponent>(borgUid))
            {
                args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 1f);
                return;
            }
        }

        args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 0f);
    }

    private void OnAfterAssign(EntityUid uid, SaboteurEmagBorgConditionComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        if (!_core.TryGetDirtyTracking(out var dirty))
            return;

        comp.CacheKey = _core.MakeCacheKey(uid);
        _core.RegisterInterest(dirty, uid, SaboteurDirtyDomain.Emag);
    }

    private void OnRequirementCheck(EntityUid uid, SaboteurEmagBorgConditionComponent comp, ref RequirementCheckEvent args)
    {
        if (args.Cancelled)
            return;

        var query = EntityQueryEnumerator<BorgChassisComponent>();
        if (!query.MoveNext(out _, out _))
            args.Cancelled = true;
    }

    private void OnBorgEmaggedByAgent(ref SaboteurBorgEmaggedByAgentEvent args)
    {
        foreach (var objUid in args.Mind.Objectives)
        {
            if (TryComp<SaboteurEmagBorgConditionComponent>(objUid, out var emagComp))
            {
                emagComp.EmaggedByMe.Add(args.BorgUid);
                break;
            }
        }
    }
}

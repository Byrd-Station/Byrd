// SPDX-FileCopyrightText: 2026 Raze500
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Omu.Server.ExtraResource.EntitySystems;
using Content.Omu.Shared.ExtraResource.Components;
using Content.Omu.Shared.Resomi.Components;
using Content.Omu.Shared.Resomi.EntitySystems;
using Content.Omu.Shared.Resomi.Events;
using Content.Server.Body.Systems;
using Content.Shared.Actions;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Omu.Server.Resomi.EntitySystems;

public sealed class NestingSystem : SharedNestingSystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ExtraResourceSystem _extraResource = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NestingComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NestingComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NestingComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<NestingComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<NestingComponent, ToggleNestActionEvent>(OnToggleNest);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<NestingComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // check if a wind-up is pending and has completed
            if (comp.PendingToggleAt is { } pendingAt && curTime >= pendingAt)
            {
                comp.PendingToggleAt = null;
                if (comp.IsNesting)
                    DoExitNest(uid, comp);
                else
                    DoEnterNest(uid, comp);
                continue;
            }

            if (!comp.IsNesting || comp.NextHeal > curTime)
                continue;

            // stop healing if the frozen component was removed somehow
            if (!HasComp<NestingFrozenComponent>(uid))
            {
                comp.IsNesting = false;
                Dirty(uid, comp);
                continue;
            }

            // heal like a medical bed - crit healing gets a bonus multiplier
            var modifier = _mobState.IsCritical(uid) ? comp.CritHealingModifier : 1.0f;
            _damageable.TryChangeDamage(uid, -modifier * comp.HealingPerUpdate, true, origin: uid);
            _bloodstream.TryModifyBleedAmount(uid, -modifier * comp.BleedHealPerUpdate);
            comp.NextHeal += comp.UpdateInterval;
        }
    }

    private void OnMapInit(EntityUid uid, NestingComponent comp, MapInitEvent args)
    {
        _actions.AddAction(uid, ref comp.ToggleNestActionEntity, comp.ToggleNestAction);
    }

    private void OnShutdown(EntityUid uid, NestingComponent comp, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, comp.ToggleNestActionEntity);

        if (comp.ContinuousEffectEntity.HasValue)
            QueueDel(comp.ContinuousEffectEntity.Value);
    }

    private void OnMobStateChanged(Entity<NestingComponent> ent, ref MobStateChangedEvent args)
    {
        // force exit if the entity dies while nesting
        if (args.NewMobState == MobState.Dead && ent.Comp.IsNesting)
            DoExitNest(ent.Owner, ent.Comp);
    }

    private void OnDamageModify(Entity<NestingComponent> ent, ref DamageModifyEvent args)
    {
        if (!ent.Comp.IsNesting || args.Origin == ent.Owner)
            return;

        // reduce all incoming damage while nesting
        var updated = new DamageSpecifier();
        foreach (var (type, value) in args.Damage.DamageDict)
            updated.DamageDict[type] = value > 0 ? ent.Comp.NestDamageReduction * value : value;

        args.Damage = updated;
    }

    private void OnToggleNest(EntityUid uid, NestingComponent comp, ToggleNestActionEvent args)
    {
        // cannot nest while handcuffed
        if (TryComp<CuffableComponent>(uid, out var cuffable) && cuffable.CuffedHandCount > 0)
        {
            _popup.PopupEntity(Loc.GetString("nesting-cuffed"), uid, uid, PopupType.SmallCaution);
            return;
        }

        // ignore if a wind-up is already pending
        if (comp.PendingToggleAt.HasValue)
            return;

        // start the half-second wind-up before actually changing state
        comp.PendingToggleAt = _timing.CurTime + comp.WindUpTime;
        Dirty(uid, comp);
    }

    private void DoEnterNest(EntityUid uid, NestingComponent comp)
    {
        comp.IsNesting = true;
        comp.NextHeal = _timing.CurTime;

        EnsureComp<NestingFrozenComponent>(uid);
        _audio.PlayPvs(comp.NestEnterSound, uid);

        var coords = Transform(uid).Coordinates;
        Spawn(comp.NestEnterEffect, coords);
        comp.ContinuousEffectEntity = Spawn(comp.NestContinuousEffect, coords);

        Dirty(uid, comp);

        // trigger a speed refresh so restfulness modifier applies immediately
        if (TryComp<ExtraResourceComponent>(uid, out var extraResource))
            _extraResource.OnNestingStateChanged(uid, extraResource, true);

        var ev = new NestingAnimationEvent(GetNetEntity(uid), GetNetCoordinates(coords), NestingAnimationType.Enter);
        RaiseNetworkEvent(ev, Filter.Pvs(uid, entityManager: EntityManager));
    }

    private void DoExitNest(EntityUid uid, NestingComponent comp)
    {
        comp.IsNesting = false;

        RemComp<NestingFrozenComponent>(uid);
        _audio.PlayPvs(comp.NestExitSound, uid);

        var coords = Transform(uid).Coordinates;

        if (comp.ContinuousEffectEntity.HasValue)
        {
            QueueDel(comp.ContinuousEffectEntity.Value);
            comp.ContinuousEffectEntity = null;
        }

        Spawn(comp.NestExitEffect, coords);
        Dirty(uid, comp);

        if (TryComp<ExtraResourceComponent>(uid, out var extraResource))
            _extraResource.OnNestingStateChanged(uid, extraResource, false);

        var ev = new NestingAnimationEvent(GetNetEntity(uid), GetNetCoordinates(coords), NestingAnimationType.Exit);
        RaiseNetworkEvent(ev, Filter.Pvs(uid, entityManager: EntityManager));
    }
}

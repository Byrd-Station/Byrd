// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Body.Systems;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared._Omu.Resomi.Components;
using Content.Shared._Omu.Resomi.EntitySystems;
using Content.Shared._Omu.Resomi.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Omu.Resomi.EntitySystems;

public sealed class NestingSystem : SharedNestingSystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NestingComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NestingComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NestingComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<NestingComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<NestingComponent, EnterNestActionEvent>(OnEnterNest);
        SubscribeLocalEvent<NestingComponent, ExitNestActionEvent>(OnExitNest);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<NestingComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.IsNesting || comp.NextHeal > curTime)
                continue;

            // Stop healing if entity is somehow moving (left the nest position)
            if (!HasComp<NestingFrozenComponent>(uid))
            {
                comp.IsNesting = false;
                Dirty(uid, comp);
                continue;
            }

            var modifier = _mobState.IsCritical(uid) ? -comp.CritHealingModifier : -1.0f;
            _damageable.TryChangeDamage(uid, modifier * comp.HealingPerUpdate, true, origin: uid);
            _bloodstream.TryModifyBleedAmount(uid, modifier * comp.BleedHealPerUpdate);
            comp.NextHeal += comp.UpdateInterval;
        }
    }

    private void OnMapInit(EntityUid uid, NestingComponent comp, MapInitEvent args)
    {
        _actions.AddAction(uid, ref comp.EnterNestActionEntity, comp.EnterNestAction);
    }

    private void OnShutdown(EntityUid uid, NestingComponent comp, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, comp.EnterNestActionEntity);
        _actions.RemoveAction(uid, comp.ExitNestActionEntity);

        if (comp.ContinuousEffectEntity.HasValue)
            QueueDel(comp.ContinuousEffectEntity.Value);
    }

    private void OnMobStateChanged(Entity<NestingComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead && ent.Comp.IsNesting)
            RaiseLocalEvent(ent.Owner, new ExitNestActionEvent());
    }

    /// <summary>
    /// Reduces all incoming positive damage by NestDamageReduction while nesting (pain tolerance).
    /// </summary>
    private void OnDamageModify(Entity<NestingComponent> ent, ref DamageModifyEvent args)
    {
        if (!ent.Comp.IsNesting || args.Origin == ent.Owner)
            return;

        var updated = new DamageSpecifier();
        foreach (var (type, value) in args.Damage.DamageDict)
        {
            updated.DamageDict[type] = value > 0 ? ent.Comp.NestDamageReduction * value : value;
        }
        args.Damage = updated;
    }

    private void OnEnterNest(EntityUid uid, NestingComponent comp, EnterNestActionEvent args)
    {
        comp.IsNesting = true;
        comp.NextHeal = _timing.CurTime;

        EnsureComp<NestingFrozenComponent>(uid);

        _actions.RemoveAction(uid, comp.EnterNestActionEntity);
        _actions.AddAction(uid, ref comp.ExitNestActionEntity, comp.ExitNestAction);

        _audio.PlayPvs(comp.NestEnterSound, uid);

        // Spawn enter animation effect
        var coords = Transform(uid).Coordinates;
        Spawn(comp.NestEnterEffect, coords);

        // Spawn continuous effect and track it
        comp.ContinuousEffectEntity = Spawn(comp.NestContinuousEffect, coords);

        Dirty(uid, comp);

        var ev = new NestingAnimationEvent(GetNetEntity(uid), GetNetCoordinates(coords), NestingAnimationType.Enter);
        RaiseNetworkEvent(ev, Filter.Pvs(uid, entityManager: EntityManager));
    }

    private void OnExitNest(EntityUid uid, NestingComponent comp, ExitNestActionEvent args)
    {
        comp.IsNesting = false;

        RemComp<NestingFrozenComponent>(uid);

        _actions.RemoveAction(uid, comp.ExitNestActionEntity);
        _actions.AddAction(uid, ref comp.EnterNestActionEntity, comp.EnterNestAction);
        _actions.SetCooldown(comp.EnterNestActionEntity, comp.NestCooldown);

        _audio.PlayPvs(comp.NestExitSound, uid);

        var coords = Transform(uid).Coordinates;

        // Remove the continuous effect and spawn exit animation
        if (comp.ContinuousEffectEntity.HasValue)
        {
            QueueDel(comp.ContinuousEffectEntity.Value);
            comp.ContinuousEffectEntity = null;
        }

        Spawn(comp.NestExitEffect, coords);

        Dirty(uid, comp);

        var ev = new NestingAnimationEvent(GetNetEntity(uid), GetNetCoordinates(coords), NestingAnimationType.Exit);
        RaiseNetworkEvent(ev, Filter.Pvs(uid, entityManager: EntityManager));
    }
}
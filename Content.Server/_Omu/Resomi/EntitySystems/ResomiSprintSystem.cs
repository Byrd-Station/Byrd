// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Actions;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared._Omu.Resomi.Components;
using Content.Shared._Omu.Resomi.EntitySystems;
using Content.Shared._Omu.Resomi.Events;
using Robust.Shared.Timing;

namespace Content.Server._Omu.Resomi.EntitySystems;

/// <summary>
/// Server-side Resomi sprint system.
/// Sprinting drains the entity's real stamina via TakeStaminaDamage each frame.
/// If stamcrit is reached while sprinting, a 20-second speed debuff
/// is applied after the Resomi recovers.
/// </summary>
public sealed class ResomiSprintSystem : SharedResomiSprintSystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly SharedStaminaSystem _stamina = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ResomiImpulseComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ResomiImpulseComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ResomiImpulseComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ResomiImpulseComponent, ToggleSprintActionEvent>(OnToggleSprint);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ResomiImpulseComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            // Exhaustion debuff timer ended
            if (comp.IsExhausted && curTime >= comp.ExhaustionEnd)
            {
                comp.IsExhausted = false;
                Dirty(uid, comp);
                _speed.RefreshMovementSpeedModifiers(uid);
                continue;
            }

            // Waiting for stamcrit to end → transition to speed debuff
            if (comp.IsExhaustedPending)
            {
                if (TryComp<StaminaComponent>(uid, out var stamina) && !stamina.Critical)
                {
                    comp.IsExhaustedPending = false;
                    comp.IsExhausted = true;
                    comp.ExhaustionEnd = curTime + comp.ExhaustionDuration;
                    Dirty(uid, comp);
                    _speed.RefreshMovementSpeedModifiers(uid);
                }
                continue;
            }

            if (!comp.IsSprinting)
                continue;

            // Drain stamina directly each frame while sprinting
            if (TryComp<StaminaComponent>(uid, out var stam))
            {
                _stamina.TakeStaminaDamage(uid, comp.StaminaDrainRate * frameTime, stam,
                    visual: false, immediate: true, logDamage: false);

                // Stamcrit while sprinting → force exhaustion
                if (stam.Critical)
                    ForceExhaustion(uid, comp);
            }
        }
    }

    private void OnMapInit(EntityUid uid, ResomiImpulseComponent comp, MapInitEvent args)
    {
        _actions.AddAction(uid, ref comp.SprintToggleActionEntity, comp.SprintToggleAction);
    }

    private void OnShutdown(EntityUid uid, ResomiImpulseComponent comp, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, comp.SprintToggleActionEntity);
    }

    private void OnMobStateChanged(Entity<ResomiImpulseComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead && ent.Comp.IsSprinting)
            StopSprint(ent.Owner, ent.Comp);
    }

    private void OnToggleSprint(EntityUid uid, ResomiImpulseComponent comp, ToggleSprintActionEvent args)
    {
        if (comp.IsSprinting)
            StopSprint(uid, comp);
        else
            StartSprint(uid, comp);
    }

    private void StartSprint(EntityUid uid, ResomiImpulseComponent comp)
    {
        if (comp.IsExhausted || comp.IsExhaustedPending)
            return;

        comp.IsSprinting = true;
        Dirty(uid, comp);
        _speed.RefreshMovementSpeedModifiers(uid);
    }

    private void StopSprint(EntityUid uid, ResomiImpulseComponent comp)
    {
        if (!comp.IsSprinting)
            return;

        comp.IsSprinting = false;
        Dirty(uid, comp);
        _speed.RefreshMovementSpeedModifiers(uid);
    }

    private void ForceExhaustion(EntityUid uid, ResomiImpulseComponent comp)
    {
        StopSprint(uid, comp);
        comp.IsExhaustedPending = true;
        Dirty(uid, comp);
        _speed.RefreshMovementSpeedModifiers(uid);
    }

}
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
    }

    private void OnMobStateChanged(Entity<NestingComponent> ent, ref MobStateChangedEvent args)
    {
        // Force exit nest if the Resomi dies
        if (args.NewMobState == MobState.Dead && ent.Comp.IsNesting)
            RaiseLocalEvent(ent.Owner, new ExitNestActionEvent());
    }

    private void OnEnterNest(EntityUid uid, NestingComponent comp, EnterNestActionEvent args)
    {
        comp.IsNesting = true;
        comp.NextHeal = _timing.CurTime;
        Dirty(uid, comp);

        EnsureComp<NestingFrozenComponent>(uid);

        _actions.RemoveAction(uid, comp.EnterNestActionEntity);
        _actions.AddAction(uid, ref comp.ExitNestActionEntity, comp.ExitNestAction);

        _audio.PlayPvs(comp.NestEnterSound, uid);

        var ev = new NestingAnimationEvent(GetNetEntity(uid), GetNetCoordinates(Transform(uid).Coordinates), NestingAnimationType.Enter);
        RaiseNetworkEvent(ev, Filter.Pvs(uid, entityManager: EntityManager));
    }

    private void OnExitNest(EntityUid uid, NestingComponent comp, ExitNestActionEvent args)
    {
        comp.IsNesting = false;
        Dirty(uid, comp);

        RemComp<NestingFrozenComponent>(uid);

        _actions.RemoveAction(uid, comp.ExitNestActionEntity);
        _actions.AddAction(uid, ref comp.EnterNestActionEntity, comp.EnterNestAction);
        _actions.SetCooldown(comp.EnterNestActionEntity, comp.NestCooldown);

        _audio.PlayPvs(comp.NestExitSound, uid);

        var ev = new NestingAnimationEvent(GetNetEntity(uid), GetNetCoordinates(Transform(uid).Coordinates), NestingAnimationType.Exit);
        RaiseNetworkEvent(ev, Filter.Pvs(uid, entityManager: EntityManager));
    }
}

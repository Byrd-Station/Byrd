using Content.Shared._Omu.RadiusBuff.Components;
using Content.Shared._Shitmed.Damage;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Damage;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Shared._Omu.RadiusBuff.Systems;

public sealed class ShredderStatusEffectSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly DamageableSystem _dmg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;


    public override void Initialize()
    {
        SubscribeLocalEvent<ShredderBuffComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovespeed);
        SubscribeLocalEvent<ShredderStatusEffectComponent, StatusEffectAppliedEvent>(OnEffectAdded);
        SubscribeLocalEvent<ShredderStatusEffectComponent, StatusEffectRemovedEvent>(OnEffectRemoved);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var query = EntityQueryEnumerator<ShredderBuffComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NextCheck >= _timing.CurTime)
                continue;

            TryApplyHealing(uid, comp);

            if (!_net.IsClient && comp.Visual != null)
            {
                var effect = Spawn(comp.Visual, Transform(uid).Coordinates);
                _transform.SetParent(effect, Transform(effect), uid);
            }

            comp.NextCheck = _timing.CurTime + comp.Delay;

            Dirty(uid, comp);
        }
    }

    private bool TryApplyHealing(EntityUid uid, ShredderBuffComponent comp)
    {
        if (comp.HealBuff == null)
            return false;

        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return false;

        _dmg.TryChangeDamage(uid,
                comp.HealBuff,
                true,
                false,
                damageable,
                targetPart: TargetBodyPart.All,
                splitDamage: SplitDamageBehavior.SplitEnsureAll);

        return true;
    }

    private void OnRefreshMovespeed(EntityUid ent, ShredderBuffComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        if (comp.SpeedBuff != 1.0f)
            args.ModifySpeed(comp.SpeedBuff, comp.SpeedBuff);
    }

    private void OnEffectAdded(EntityUid ent, ShredderStatusEffectComponent comp, StatusEffectAppliedEvent args)
    {
        var uid = args.Target;
        if (!_net.IsClient) // Lazy fix for an error. This should probably be ran in Content.Client instead.
            EnsureComp<ShredderBuffComponent>(uid);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnEffectRemoved(EntityUid ent, ShredderStatusEffectComponent comp, StatusEffectRemovedEvent args)
    {
        var uid = args.Target;
        if (!_net.IsClient)
            RemComp<ShredderBuffComponent>(uid);
        _movement.RefreshMovementSpeedModifiers(uid);
    }
}

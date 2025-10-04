using Content.Goobstation.Shared._Omu.AdminEvents.TemuViro.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;


namespace Content.Goobstation.Shared._Omu.AdminEvents.TemuViro;

public abstract class SharedTemuViroSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TemuViroComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(EntityUid uid, TemuViroComponent component, ComponentStartup args)
    {
        if (component.EffectTime == TimeSpan.Zero)
        {
            SetNextEffectTime(uid, component);
        }
    }

    private void SetNextEffectTime(EntityUid uid, TemuViroComponent component)
    {
        component.EffectTime = _gameTiming.CurTime + TimeSpan.FromSeconds(
            Random.Shared.Next(
                (int)component.MinEffectTime.TotalSeconds,
                (int)component.MaxEffectTime.TotalSeconds + 1
            )
        );
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<TemuViroComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.IsCured)
                continue;

            if (_gameTiming.CurTime < comp.EffectTime)
                continue;

            // Check if effect should trigger based on RandomChance
            comp.RandomChance = Random.Shared.NextDouble();
            if (comp.RandomChance < 50f)
            {
                // Apply effects & Popup
                ApplyPoisonDamage(uid, comp);
                _popupSystem.PopupEntity("You feel nauseous", uid, PopupType.Medium);
            }
            SetNextEffectTime(uid, comp);
        }
    }

    private void ApplyPoisonDamage(EntityUid uid, TemuViroComponent comp)
    {
        if (comp.PoisonDamage >= comp.MaxPoisonDamage)
            return;

        var damage = Random.Shared.Next(comp.MinPoisonDamagePerEffect, comp.MaxPoisonDamagePerEffect + 1);
        damage = (int)Math.Min(damage, comp.MaxPoisonDamage - comp.PoisonDamage);

        if (damage > 0 && _prototypeManager.TryIndex<DamageTypePrototype>("Poison", out var damageType))
        {
            var damageSpecifier = new DamageSpecifier(damageType, damage);
            _damageable.TryChangeDamage(uid, damageSpecifier);
            comp.PoisonDamage += damage;
        }
    }
}

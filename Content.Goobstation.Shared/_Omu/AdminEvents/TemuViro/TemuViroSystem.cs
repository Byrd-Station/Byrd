// SPDX-FileCopyrightText: 2025 RichardBlonski <48651647+RichardBlonski@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Shared._Omu.AdminEvents.TemuViro.Components;
using Content.Goobstation.Shared._Omu.AdminEvents.TemuViro.Events;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Drunk;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;


namespace Content.Goobstation.Shared._Omu.AdminEvents.TemuViro;

public abstract class SharedTemuViroSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDrunkSystem _drunkSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TemuViroComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<TemuViroComponent, OnCuredEvent>(OnCured);
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
                _popupSystem.PopupEntity("You feel nauseous", uid, PopupType.MediumCaution);

                // Drunk
                ApplyDrunkEffect(uid, comp);

                // Vomit
                if (comp.NextVomitTime == TimeSpan.Zero)
                {
                    comp.NextVomitTime = _gameTiming.CurTime + TimeSpan.FromSeconds(5f);
                    return;
                }
                    if (_gameTiming.CurTime >= comp.NextVomitTime)
                    {
                        var netEntity = GetNetEntity(uid);
                        var vomitEvent = new OnVomitEvent(netEntity);
                        RaiseLocalEvent(uid, ref vomitEvent);
                        comp.NextVomitTime = TimeSpan.Zero;
                    }
            }
            SetNextEffectTime(uid, comp);
        }
    }

    #region Poision
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
    #endregion

    #region Drunk
    private void ApplyDrunkEffect(EntityUid uid, TemuViroComponent comp)
    {
        TryComp<StatusEffectsComponent>(uid, out var status);

        // Calculate drunk strength based on poison damage (0-45 damage maps to 0-100 strength)
        float drunkStrength = Math.Clamp(comp.PoisonDamage / 45f * 100f, 0f, 100f);
        _drunkSystem.SetDrunkeness(uid, drunkStrength, status);
    }

    #endregion

    #region Cured Event
    // This gets called by the server when we have set Cured to true
    private void OnCured(EntityUid uid, TemuViroComponent comp, OnCuredEvent args)
    {
        _popupSystem.PopupEntity("You feel better.", uid, PopupType.Medium);
        _drunkSystem.TryRemoveDrunkenness(uid);
    }
    #endregion
}

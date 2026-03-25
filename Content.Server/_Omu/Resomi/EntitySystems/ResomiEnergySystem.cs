// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Alert;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared._Omu.Resomi.Components;
using Robust.Shared.Timing;

namespace Content.Server._Omu.Resomi.EntitySystems;

/// <summary>
/// Manages the Resomi Energy bar.
/// Energy drains passively and recharges while nesting.
/// When empty, applies a speed debuff until recharged.
/// </summary>
public sealed class ResomiEnergySystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ResomiEnergyComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ResomiEnergyComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ResomiEnergyComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovespeed);
    }

    private void OnMapInit(EntityUid uid, ResomiEnergyComponent comp, MapInitEvent args)
    {
        UpdateAlert(uid, comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ResomiEnergyComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var isNesting = HasComp<NestingFrozenComponent>(uid);

            var prev = comp.Energy;

            if (isNesting)
                comp.Energy = Math.Min(comp.MaxEnergy, comp.Energy + comp.RechargeRate * frameTime);
            else
                comp.Energy = Math.Max(0f, comp.Energy - comp.DrainRate * frameTime);

            // Popup when crossing into exhaustion
            if (prev > 0f && comp.Energy <= 0f)
                _popup.PopupEntity(Loc.GetString("resomi-energy-exhausted"), uid, PopupType.LargeCaution);

            // Popup when recovering from exhaustion
            if (prev <= 0f && comp.Energy > comp.DangerThreshold)
                _popup.PopupEntity(Loc.GetString("resomi-energy-recovered"), uid, PopupType.Small);

            var prevSeverity = GetSeverity(prev, comp.MaxEnergy);
            var newSeverity = GetSeverity(comp.Energy, comp.MaxEnergy);

            UpdateAlert(uid, comp);

            var wasExhausted = comp.IsExhausted;
            comp.IsExhausted = comp.Energy <= 0f;

            if (comp.IsExhausted != wasExhausted || prevSeverity != newSeverity)
            {
                Dirty(uid, comp);
                _speed.RefreshMovementSpeedModifiers(uid);
            }
        }
    }

    private void UpdateAlert(EntityUid uid, ResomiEnergyComponent comp)
    {
        _alerts.ShowAlert(uid, comp.EnergyAlert, GetSeverity(comp.Energy, comp.MaxEnergy));
    }

    private static short GetSeverity(float energy, float max)
    {
        var pct = max > 0f ? energy / max : 0f;
        if (pct > 0.70f) return 1;
        if (pct > 0.40f) return 2;
        if (pct > 0.10f) return 3;
        return 4;
    }

    private void OnRefreshMovespeed(EntityUid uid, ResomiEnergyComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        var pct = comp.Energy / comp.MaxEnergy;

        // Graduated slow like hunger/thirst:
        // ≤10%  → 0.70x (heavy slow / exhausted)
        // ≤40%  → 0.85x (noticeable slow)
        // ≤70%  → 0.95x (slight slow)
        // >70%  → no debuff
        if (pct <= 0.10f)
            args.ModifySpeed(comp.ExhaustionSpeedMultiplier, comp.ExhaustionSpeedMultiplier);
        else if (pct <= 0.40f)
            args.ModifySpeed(0.85f, 0.85f);
        else if (pct <= 0.70f)
            args.ModifySpeed(0.95f, 0.95f);
    }


    private void OnShutdown(EntityUid uid, ResomiEnergyComponent comp, ComponentShutdown args)
    {
        _alerts.ClearAlertCategory(uid, comp.EnergyAlertCategory);
    }
}
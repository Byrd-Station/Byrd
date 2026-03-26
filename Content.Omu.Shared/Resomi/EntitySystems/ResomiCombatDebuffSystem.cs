// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Omu.Shared.Resomi.Components;
using Content.Shared._Shitmed.Weapons.Ranged.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Omu.Shared.Resomi.EntitySystems;

/// <summary>
/// Applies passive combat debuffs to Resomis to reflect their small, agile-but-fragile physique.
/// Resomis are built for speed and mobility, not sustained combat — these debuffs push players
/// toward evasion and support roles rather than frontline fighting.
///
/// Debuffs applied:
/// - 15% less outgoing melee damage (lighter body mass, weaker strikes)
/// - Increased bullet spread (small frame makes handling large weapons unstable)
/// - Heavy stamina drain when firing rifles, shotguns, snipers, launchers, HMGs and LMGs
///   (pistols, revolvers and SMGs are exempt — Resomis can still use sidearms effectively)
/// </summary>
public sealed class ResomiCombatDebuffSystem : EntitySystem
{
    [Dependency] private readonly SharedStaminaSystem _stamina = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ResomiCombatDebuffComponent, GetUserMeleeDamageEvent>(OnMeleeDamage);

        // DeltaV's SharedGunSystem.Holder.cs raises GunRefreshModifiersEvent on the holder (user)
        // entity whenever the gun's modifiers are refreshed. By subscribing here on the Resomi
        // component we cleanly intercept those events without duplicating GunComponent subscriptions.
        SubscribeLocalEvent<ResomiCombatDebuffComponent, GunRefreshModifiersEvent>(OnGunRefresh);

        // GunShotBodyEvent is raised on the shooter (user) by SharedGunSystem after every shot.
        // We check if the gun has ResomiGunStaminaDrainComponent (present on heavy weapons only)
        // and drain stamina accordingly.
        SubscribeLocalEvent<ResomiCombatDebuffComponent, GunShotBodyEvent>(OnGunShot);
    }

    /// <summary>
    /// Reduces outgoing melee damage for Resomis.
    /// Raised directly on the Resomi (attacker), so the component filter is sufficient.
    /// </summary>
    private void OnMeleeDamage(Entity<ResomiCombatDebuffComponent> ent, ref GetUserMeleeDamageEvent args)
    {
        args.Damage *= ent.Comp.MeleeDamageMultiplier;
    }

    /// <summary>
    /// Applies bullet spread debuff whenever a Resomi holds a gun.
    /// SharedGunSystem raises this event on gun.Comp.Holder (the wielder),
    /// so we receive it here without needing separate equip/unequip hooks.
    ///
    /// Spread is added to both MinAngle and MaxAngle so every shot is affected,
    /// not just the worst-case cone. This makes the penalty visible even on the first shot.
    /// </summary>
    private void OnGunRefresh(Entity<ResomiCombatDebuffComponent> ent, ref GunRefreshModifiersEvent args)
    {
        // Add flat degree spread to both angle bounds.
        // At 8 degrees the inaccuracy is clearly noticeable at medium range without
        // making point-blank self-defence completely useless.
        var spread = Angle.FromDegrees(ent.Comp.SpreadIncreaseDegrees);
        args.MinAngle += spread;
        args.MaxAngle += spread;
    }

    /// <summary>
    /// Drains heavy stamina from a Resomi when they fire a heavy weapon.
    /// Only guns with <see cref="ResomiGunStaminaDrainComponent"/> trigger the drain —
    /// pistols, revolvers, and SMGs are deliberately exempt so Resomis still have viable sidearms.
    /// </summary>
    private void OnGunShot(EntityUid uid, ResomiCombatDebuffComponent comp, GunShotBodyEvent args)
    {
        // Only drain for guns that carry the heavy-weapon marker.
        if (!TryComp<ResomiGunStaminaDrainComponent>(args.GunUid, out var drain))
            return;

        _stamina.TakeStaminaDamage(uid, drain.StaminaDrain);
    }
}

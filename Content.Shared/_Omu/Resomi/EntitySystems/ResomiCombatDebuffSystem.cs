// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Omu.Resomi.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;

namespace Content.Shared._Omu.Resomi.EntitySystems;

/// <summary>
/// Applies passive combat debuffs to Resomis to reflect their small, agile-but-fragile physique.
/// Resomis are built for speed and mobility, not sustained combat — these debuffs push players
/// toward evasion and support roles rather than frontline fighting.
///
/// Debuffs applied:
/// - 15% less outgoing melee damage (lighter body mass, weaker strikes)
/// - Increased bullet spread (small frame makes handling large weapons unstable)
/// - Increased camera recoil (amplifies felt kickback from the same instability)
/// </summary>
public sealed class ResomiCombatDebuffSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<ResomiCombatDebuffComponent, GetUserMeleeDamageEvent>(OnMeleeDamage);

        // GunRefreshModifiersEvent is raised directed on the GUN entity, not the shooter.
        // We subscribe on GunComponent and check args.User to apply the debuff only to Resomis.
        // This is the correct SS14 pattern — subscribing on ResomiCombatDebuffComponent here
        // would never fire because Resomis do not have a GunComponent themselves.
        SubscribeLocalEvent<GunComponent, GunRefreshModifiersEvent>(OnGunRefresh);
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
    /// Increases bullet spread and camera recoil when a Resomi fires any gun.
    /// Only applies if the shooter (<see cref="GunRefreshModifiersEvent.User"/>) has
    /// <see cref="ResomiCombatDebuffComponent"/> — all other species are unaffected.
    ///
    /// Spread is added to both MinAngle and MaxAngle so every shot is affected,
    /// not just the worst-case cone. This makes the penalty visible even on the first shot.
    /// </summary>
    private void OnGunRefresh(Entity<GunComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (args.User == null || !TryComp<ResomiCombatDebuffComponent>(args.User, out var debuff))
            return;

        // Add flat degree spread to both angle bounds.
        // At 8 degrees the inaccuracy is clearly noticeable at medium range without
        // making point-blank self-defence completely useless.
        var spread = Angle.FromDegrees(debuff.SpreadIncreaseDegrees);
        args.MinAngle += spread;
        args.MaxAngle += spread;

        // Scale camera shake on top of spread so the feedback matches the inaccuracy.
        args.CameraRecoilScalar *= debuff.RecoilMultiplier;
    }
}
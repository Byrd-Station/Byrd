// SPDX-FileCopyrightText: 2026 Raze500
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Omu.Shared.Firearms.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Components;

namespace Content.Omu.Server.Firearms.EntitySystems;

/// <summary>
///     applies a physics knockback impulse to the shooter when firing a gun
///     with FirearmKickbackComponent. the impulse direction is opposite to
///     the direction the gun was aimed, simulating recoil.
/// </summary>
public sealed class FirearmKickbackSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FirearmKickbackComponent, GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(Entity<FirearmKickbackComponent> ent, ref GunShotEvent args)
    {
        var shooter = args.User;

        if (!TryComp<PhysicsComponent>(shooter, out var physics))
            return;

        if (!TryComp<TransformComponent>(shooter, out var xform))
            return;

        // get the direction the shooter is facing (which is the aim direction)
        // and push them in the opposite direction
        var aimDirection = xform.WorldRotation.ToVec();
        var kickbackDirection = -aimDirection;

        _physics.ApplyLinearImpulse(shooter, kickbackDirection * ent.Comp.KickbackStrength, body: physics);
    }
}

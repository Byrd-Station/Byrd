// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Omu.Shared.Enraged;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Content.Shared.Mind.Components;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Player;

namespace Content.Omu.Server.Enraged;

/// <summary>
/// Handles melee attack sounds and lunge animation for enraged entities.
/// Resends animations to the attacking player to bypass client-side prediction.
/// Without this the client will not show the lunge animation as the server is making the attacks.
/// </summary>
public sealed class EnragedBypassPrediction : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private const float LungeBuffer = 0.2f;
    private const float MinLungeLength = 0.1f;
    private const float DamagePitchVariation = 0.05f;

    public void OnMeleeHit(Entity<MeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        if (!args.IsHit || !IsEnraged(args.User))
            return;

        if (!TryComp<ActorComponent>(args.User, out var actor) ||
            !TryComp(args.User, out TransformComponent? userXform))
            return;

        var lungeOffset = ComputeLungeOffset(weapon.Comp, args.Coords, userXform);
        var (animation, spriteRotation) = GetMeleeAnimation(weapon.Comp, args);

        var filter = Filter.SinglePlayer(actor.PlayerSession);
        RaiseNetworkEvent(
            new MeleeLungeEvent(
                GetNetEntity(args.User),
                GetNetEntity(weapon.Owner),
                weapon.Comp.Angle,
                lungeOffset,
                animation,
                spriteRotation,
                weapon.Comp.FlipAnimation),
            filter);

        SendWeaponAudio(filter, weapon, args);
    }

    private bool IsEnraged(EntityUid user)
    {
        if (!TryComp<StatusEffectContainerComponent>(user, out var statusEffects))
            return false;

        var effects = statusEffects.ActiveStatusEffects?.ContainedEntities;
        if (effects == null)
            return false;

        foreach (var effect in effects)
        {
            if (HasComp<EnragedStatusEffectComponent>(effect))
                return true;
        }

        return false;
    }

    private Vector2 ComputeLungeOffset(
        MeleeWeaponComponent weapon,
        EntityCoordinates hitCoords,
        TransformComponent userXform)
    {
        var mapCoords = _transform.ToMapCoordinates(hitCoords);
        var localPos = Vector2.Transform(mapCoords.Position, _transform.GetInvWorldMatrix(userXform));

        var visualLength = MathF.Max(weapon.Range - LungeBuffer, weapon.Range);
        if (localPos.LengthSquared() <= 0f)
            localPos = new Vector2(MathF.Max(visualLength, MinLungeLength), 0f);

        localPos = userXform.LocalRotation.RotateVec(localPos);
        if (localPos.Length() > visualLength)
            localPos = localPos.Normalized() * visualLength;

        return localPos;
    }

    private static (string? Animation, Angle Rotation) GetMeleeAnimation(
        MeleeWeaponComponent weapon,
        MeleeHitEvent args)
    {
        if (args.Direction != null)
            return (weapon.WideAnimation, weapon.WideAnimationRotation);

        var animation = args.HitEntities.Count == 0
            ? weapon.MissAnimation
            : weapon.Animation;

        return (animation, weapon.AnimationRotation);
    }

    private void SendWeaponAudio(
        Filter filter,
        Entity<MeleeWeaponComponent> weapon,
        MeleeHitEvent args)
    {
        _audio.PlayEntity(weapon.Comp.SwingSound, filter, weapon.Owner, false);

        if (args.HitEntities.Count == 0)
            return;

        var hitSound = args.HitSoundOverride ?? weapon.Comp.HitSound ?? (SoundSpecifier)weapon.Comp.NoDamageSound;
        _audio.PlayStatic(hitSound, filter, args.Coords, false,
            AudioParams.Default.WithVariation(DamagePitchVariation));
    }
}

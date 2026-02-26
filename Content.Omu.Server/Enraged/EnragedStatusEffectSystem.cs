// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Omu.Shared.Enraged;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Robust.Shared.Map;
using Content.Server.NPC.Systems;
using Content.Shared.Mind.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.SSDIndicator;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Omu.Server.Enraged;

public sealed partial class EnragedStatusEffectSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;


    /// Visual buffer subtracted from weapon range so the lunge animation
    /// doesn't appear to overreach the actual hit distance.
    private const float LungeBuffer = 0.2f;

    /// Minimum lunge length used when the computed offset is zero.
    private const float MinLungeLength = 0.1f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EnragedStatusEffectComponent, StatusEffectAppliedEvent>(OnStatusApplied);
        SubscribeLocalEvent<EnragedStatusEffectComponent, StatusEffectRemovedEvent>(OnStatusRemoved);
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
    }

    // ──────────────────────────────────────────────
    //  Melee lunge re-broadcast
    // ──────────────────────────────────────────────

    private void OnMeleeHit(Entity<MeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        if (!IsEnraged(args.User))
            return;

        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        if (!TryComp(args.User, out TransformComponent? userXform))
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

    /// Checks whether an entity currently has an active enraged status effect.
    private bool IsEnraged(EntityUid user)
    {
        if (!TryComp<StatusEffectContainerComponent>(user, out var statusEffects))
            return false;

        var contained = statusEffects.ActiveStatusEffects?.ContainedEntities;
        if (contained == null)
            return false;

        foreach (var effect in contained)
        {
            if (HasComp<EnragedStatusEffectComponent>(effect))
                return true;
        }

        return false;
    }

    /// Converts world-space hit coordinates into a clamped local-space offset
    /// suitable for the lunge animation.
    private Vector2 ComputeLungeOffset(
        MeleeWeaponComponent weapon,
        EntityCoordinates hitCoords,
        TransformComponent userXform)
    {
        var mapCoords = _transform.ToMapCoordinates(hitCoords);
        var invMatrix = _transform.GetInvWorldMatrix(userXform);
        var localPos = Vector2.Transform(mapCoords.Position, invMatrix);

        var visualLength = weapon.Range - LungeBuffer;
        if (visualLength <= 0f)
            visualLength = weapon.Range;

        if (localPos.LengthSquared() <= 0f)
            localPos = new Vector2(MathF.Max(visualLength, MinLungeLength), 0f);

        localPos = userXform.LocalRotation.RotateVec(localPos);

        if (localPos.Length() > visualLength)
            localPos = localPos.Normalized() * visualLength;

        return localPos;
    }

    /// Selects the correct animation and sprite rotation for a light or heavy
    /// melee swing, accounting for hits and misses.
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

    // ──────────────────────────────────────────────
    //  Weapon audio re-broadcast
    // ──────────────────────────────────────────────

    /// Pitch variation applied to hit sounds, matching <see cref="MeleeSoundSystem"/>.
    private const float DamagePitchVariation = 0.05f;

    /// Sends the weapon's swing and hit sounds to a specific player filter.
    /// Because HTN-driven attacks bypass client prediction, the enraged player
    /// would otherwise never hear them.
    private void SendWeaponAudio(
        Filter filter,
        Entity<MeleeWeaponComponent> weapon,
        MeleeHitEvent args)
    {
        // Swing sound — always plays on the weapon entity.
        _audio.PlayEntity(
            weapon.Comp.SwingSound,
            filter,
            weapon.Owner,
            false);

        // Hit sound — only when we actually struck something.
        if (args.HitEntities.Count == 0)
            return;

        var hitSound = args.HitSoundOverride
                       ?? weapon.Comp.HitSound
                       ?? (SoundSpecifier) weapon.Comp.NoDamageSound;

        _audio.PlayStatic(
            hitSound,
            filter,
            args.Coords,
            false,
            AudioParams.Default.WithVariation(DamagePitchVariation));
    }

    // ──────────────────────────────────────────────
    //  Status-effect lifecycle
    // ──────────────────────────────────────────────

    private void OnStatusApplied(Entity<EnragedStatusEffectComponent> ent, ref StatusEffectAppliedEvent args)
    {
        var target = args.Target;

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(target):target} became enraged.");

        RemoveSsdIndicator(ent, target);
        ApplyHostileFaction(ent, target);
        ApplyHostileHtn(ent, target);
    }

    private void OnStatusRemoved(Entity<EnragedStatusEffectComponent> ent, ref StatusEffectRemovedEvent args)
    {
        var target = args.Target;

        RestoreSsdIndicator(ent, target);
        RestoreFaction(ent, target);
        RestoreHtn(ent, target);
    }

    // ──────────────────────────────────────────────
    //  SSD indicator helpers
    // ──────────────────────────────────────────────

    private void RemoveSsdIndicator(Entity<EnragedStatusEffectComponent> ent, EntityUid target)
    {
        if (!TryComp<SSDIndicatorComponent>(target, out _))
            return;

        RemComp<SSDIndicatorComponent>(target);
        ent.Comp.RemovedSsdIndicator = true;
    }

    private void RestoreSsdIndicator(Entity<EnragedStatusEffectComponent> ent, EntityUid target)
    {
        if (!ent.Comp.RemovedSsdIndicator || HasComp<SSDIndicatorComponent>(target))
            return;

        var ssd = EnsureComp<SSDIndicatorComponent>(target);
        ssd.IsSSD = false;
        Dirty(target, ssd);
    }

    // ──────────────────────────────────────────────
    //  NPC faction helpers
    // ──────────────────────────────────────────────

    private void ApplyHostileFaction(Entity<EnragedStatusEffectComponent> ent, EntityUid target)
    {
        if (!TryComp<NpcFactionMemberComponent>(target, out var npcFaction))
        {
            npcFaction = EnsureComp<NpcFactionMemberComponent>(target);
            ent.Comp.AddedFactionComponent = true;
        }

        ent.Comp.OldFactions.Clear();
        ent.Comp.OldFactions.UnionWith(npcFaction.Factions);
        _npcFaction.ClearFactions((target, npcFaction), false);
        _npcFaction.AddFaction((target, npcFaction), ent.Comp.HostileFaction);
    }

    private void RestoreFaction(Entity<EnragedStatusEffectComponent> ent, EntityUid target)
    {
        if (!TryComp<NpcFactionMemberComponent>(target, out var npcFaction))
            return;

        _npcFaction.ClearFactions((target, npcFaction), false);

        if (ent.Comp.AddedFactionComponent)
        {
            RemComp<NpcFactionMemberComponent>(target);
            return;
        }

        foreach (var faction in ent.Comp.OldFactions)
        {
            _npcFaction.AddFaction((target, npcFaction), faction);
        }
    }

    // ──────────────────────────────────────────────
    //  HTN AI helpers
    // ──────────────────────────────────────────────

    private void ApplyHostileHtn(Entity<EnragedStatusEffectComponent> ent, EntityUid target)
    {
        if (!TryComp<HTNComponent>(target, out var htn))
        {
            htn = EnsureComp<HTNComponent>(target);
            ent.Comp.AddedHtnComponent = true;
        }
        else
        {
            ent.Comp.OldRootTask = htn.RootTask.Task;
        }

        htn.RootTask = new HTNCompoundTask { Task = ent.Comp.HostileRootTask };
        htn.Blackboard.SetValue(NPCBlackboard.Owner, target);

        _npc.WakeNPC(target, htn);
        _htn.Replan(htn);
    }

    private void RestoreHtn(Entity<EnragedStatusEffectComponent> ent, EntityUid target)
    {
        if (!TryComp<HTNComponent>(target, out var htn))
            return;

        if (ent.Comp.AddedHtnComponent)
        {
            RemComp<HTNComponent>(target);
            return;
        }

        if (!string.IsNullOrEmpty(ent.Comp.OldRootTask))
        {
            htn.RootTask = new HTNCompoundTask { Task = ent.Comp.OldRootTask };
            _htn.Replan(htn);
        }
    }
}

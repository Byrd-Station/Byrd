// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Common.MartialArts;
using Content.Goobstation.Shared.MartialArts.Components;
using Content.Server.NPC.Components;
using Content.Shared.CombatMode;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.NPC;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Omu.Server.MartialArts;

/// <summary>
/// Drives NPC martial arts combat each tick.
/// Picks combos, waits on cooldowns, and fires melee/disarm/grab attacks.
/// </summary>
public sealed class NpcMartialArtsCombatSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<NpcMartialArtsCombatComponent, ActiveNPCComponent>();

        while (query.MoveNext(out var uid, out var martial, out _))
        {
            if (!TryComp<CombatModeComponent>(uid, out var combatMode) || !combatMode.IsInCombatMode)
            {
                RemCompDeferred<NpcMartialArtsCombatComponent>(uid);
                continue;
            }

            ProcessCombat(uid, martial, curTime, frameTime);
        }
    }

    private void ProcessCombat(
        EntityUid uid,
        NpcMartialArtsCombatComponent martial,
        TimeSpan curTime,
        float frameTime)
    {
        ReleaseNonTarget(uid, martial.Target);

        if (!TryComp<MeleeWeaponComponent>(uid, out var fists))
        {
            martial.Status = CombatStatus.NoWeapon;
            return;
        }

        if (!ValidateTarget(uid, martial, out var distance))
            return;

        if (distance > fists.Range)
        {
            martial.Status = CombatStatus.TargetOutOfRange;
            return;
        }

        martial.Status = CombatStatus.Normal;

        if (fists.NextAttack > curTime)
            return;

        if (!TryComp<CanPerformComboComponent>(uid, out var combo))
            return;

        if (martial.ActiveCombo == null && !TryPickCombo(martial, combo))
            return;

        if (martial.StepCooldown > 0f)
        {
            martial.StepCooldown -= frameTime;
            return;
        }

        ExecuteNextStep(uid, fists, martial);
    }

    private bool ValidateTarget(
        EntityUid uid,
        NpcMartialArtsCombatComponent martial,
        out float distance)
    {
        distance = 0f;

        if (!Exists(martial.Target) || Deleted(martial.Target))
        {
            martial.Status = CombatStatus.TargetUnreachable;
            return false;
        }

        var xform = Transform(uid);
        var targetXform = Transform(martial.Target);

        if (!xform.Coordinates.TryDistance(EntityManager, targetXform.Coordinates, out distance))
        {
            martial.Status = CombatStatus.TargetUnreachable;
            return false;
        }

        return true;
    }

    private bool TryPickCombo(NpcMartialArtsCombatComponent martial, CanPerformComboComponent combo)
    {
        if (combo.AllowedCombos.Count == 0)
            return false;

        var candidates = new List<ComboPrototype>();
        foreach (var c in combo.AllowedCombos)
        {
            if (!c.PerformOnSelf)
                candidates.Add(c);
        }

        if (candidates.Count == 0)
            return false;

        martial.ActiveCombo = _random.Pick(candidates);
        martial.StepIndex = 0;
        martial.StepCooldown = 0f;
        combo.LastAttacks.Clear();
        return true;
    }

    private void ExecuteNextStep(EntityUid uid, MeleeWeaponComponent fists, NpcMartialArtsCombatComponent martial)
    {
        var attackType = martial.ActiveCombo!.AttackTypes[martial.StepIndex];

        if (!ExecuteAttack(uid, fists, martial.Target, attackType))
        {
            ReleasePull(uid, martial.Target);
            ResetCombo(martial);
            return;
        }

        martial.StepIndex++;
        martial.StepCooldown = martial.TimeBetweenSteps;

        if (martial.StepIndex >= martial.ActiveCombo.AttackTypes.Count)
        {
            ReleasePull(uid, martial.Target);
            ResetCombo(martial);
        }
    }

    private bool ExecuteAttack(EntityUid uid, MeleeWeaponComponent fists, EntityUid target, ComboAttackType type)
    {
        return type switch
        {
            ComboAttackType.Harm or ComboAttackType.HarmLight
                => _melee.AttemptLightAttack(uid, uid, fists, target),
            ComboAttackType.Disarm
                => _melee.AttemptDisarmAttack(uid, uid, fists, target),
            ComboAttackType.Grab
                => TryGrab(uid, target),
            _ => false,
        };
    }

    private bool TryGrab(EntityUid uid, EntityUid target)
    {
        if (!TryComp<PullerComponent>(uid, out var puller))
            return _pulling.TryStartPull(uid, target);

        if (puller.Pulling == target)
            return _pulling.TryGrab(target, uid, ignoreCombatMode: true);

        if (puller.Pulling is { } other && TryComp<PullableComponent>(other, out var pullable))
            _pulling.TryStopPull(other, pullable, uid, true);

        return _pulling.TryStartPull(uid, target);
    }

    private void ReleasePull(EntityUid uid, EntityUid target)
    {
        if (TryComp<PullerComponent>(uid, out var puller) &&
            puller.Pulling == target &&
            TryComp<PullableComponent>(target, out var pullable))
        {
            _pulling.TryStopPull(target, pullable, uid, true);
        }
    }

    private void ReleaseNonTarget(EntityUid uid, EntityUid target)
    {
        if (!TryComp<PullerComponent>(uid, out var puller) || puller.Pulling is not { } pulled)
            return;

        if (pulled == target)
            return;

        if (TryComp<PullableComponent>(pulled, out var pullable))
            _pulling.TryStopPull(pulled, pullable, uid, true);
    }

    private static void ResetCombo(NpcMartialArtsCombatComponent martial)
    {
        martial.ActiveCombo = null;
        martial.StepIndex = 0;
        martial.StepCooldown = 0f;
    }
}

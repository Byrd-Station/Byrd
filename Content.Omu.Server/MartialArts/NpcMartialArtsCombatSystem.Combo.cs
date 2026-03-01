// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Common.MartialArts;
using Content.Goobstation.Shared.MartialArts.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Standing;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Random;

namespace Content.Omu.Server.MartialArts;

/// <summary>
/// Combo selection, step execution, and attack dispatch for NPC martial art.
/// </summary>
public sealed partial class NpcMartialArtsCombatSystem
{
    // Combo selection

    /// <summary>
    /// Randomly selects a combo appropriate for the NPC's current posture.
    /// Prone NPCs only consider self-targeting prone-capable combos (KickUp).
    /// </summary>
    private bool TryPickCombo(EntityUid uid, NpcMartialArtsCombatComponent martial, CanPerformComboComponent combo)
    {
        if (combo.AllowedCombos.Count == 0)
            return false;

        var isProne = TryComp<StandingStateComponent>(uid, out var standing) && !standing.Standing;
        var candidates = FilterCombos(combo.AllowedCombos, isProne);

        if (candidates.Count == 0)
            return false;

        martial.ActiveCombo = _random.Pick(candidates);
        martial.StepIndex = 0;
        martial.StepCooldown = 0f;
        martial.LastStepTime = _timing.CurTime;
        combo.LastAttacks.Clear();
        return true;
    }

    private static List<ComboPrototype> FilterCombos(IReadOnlyList<ComboPrototype> combos, bool isProne)
    {
        var result = new List<ComboPrototype>();

        foreach (var c in combos)
        {
            var eligible = isProne
                ? c.PerformOnSelf && c.CanDoWhileProne
                : !c.PerformOnSelf;

            if (eligible)
                result.Add(c);
        }

        return result;
    }

    // Step execution

    /// <summary>
    /// Advances to the next attack in the active combo sequence.
    /// Resets the combo on failure or after the final step completes.
    /// </summary>
    private void ExecuteNextStep(
        EntityUid uid,
        MeleeWeaponComponent fists,
        NpcMartialArtsCombatComponent martial,
        EntityUid attackTarget)
    {
        var attackType = martial.ActiveCombo!.AttackTypes[martial.StepIndex];

        if (!DispatchAttack(uid, fists, attackTarget, attackType))
        {
            FinishCombo(uid, martial);
            return;
        }

        martial.LastStepTime = _timing.CurTime;
        martial.StepIndex++;
        martial.StepCooldown = martial.TimeBetweenSteps;

        if (martial.StepIndex >= martial.ActiveCombo.AttackTypes.Count)
            FinishCombo(uid, martial);
    }

    private void FinishCombo(EntityUid uid, NpcMartialArtsCombatComponent martial)
    {
        ReleasePull(uid, martial.Target);
        martial.ActiveCombo = null;
        martial.StepIndex = 0;
        martial.StepCooldown = 0f;
        martial.FillerAttacksRemaining = _random.Next(martial.FillerAttacksMin, martial.FillerAttacksMax + 1);
    }

    // Attack dispatch

    private bool DispatchAttack(EntityUid uid, MeleeWeaponComponent fists, EntityUid target, ComboAttackType type)
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

    //  Pull helpers

    private void ReleasePull(EntityUid uid, EntityUid target)
    {
        if (TryComp<PullerComponent>(uid, out var puller)
            && puller.Pulling == target
            && TryComp<PullableComponent>(target, out var pullable))
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
}

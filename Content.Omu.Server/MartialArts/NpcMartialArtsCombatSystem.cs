// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Shared.MartialArts.Components;
using Content.Server.NPC.Components;
using Content.Shared.CombatMode;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.NPC;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Omu.Server.MartialArts;

/// <summary>
/// Drives NPC martial arts combat each tick..
/// </summary>
public sealed partial class NpcMartialArtsCombatSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

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

        if (martial.ActiveCombo != null && curTime - martial.LastStepTime > martial.StepTimeout)
        {
            FinishCombo(uid, martial);
            return;
        }

        if (!TryComp<MeleeWeaponComponent>(uid, out var fists))
        {
            martial.Status = CombatStatus.NoWeapon;
            return;
        }

        if (fists.NextAttack > curTime)
            return;

        if (!TryComp<CanPerformComboComponent>(uid, out var combo))
            return;

        if (martial.ActiveCombo == null && martial.FillerAttacksRemaining > 0)
        {
            if (!ValidateTarget(uid, martial, out var fillerDist) || fillerDist > fists.Range)
                return;

            _melee.AttemptLightAttack(uid, uid, fists, martial.Target);
            martial.FillerAttacksRemaining--;
            return;
        }

        if (martial.ActiveCombo == null && !TryPickCombo(uid, martial, combo))
            return;

        if (martial.StepCooldown > 0f)
        {
            martial.StepCooldown -= frameTime;
            return;
        }

        if (martial.ActiveCombo!.PerformOnSelf)
        {
            ExecuteNextStep(uid, fists, martial, uid);
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
        ExecuteNextStep(uid, fists, martial, martial.Target);
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
}

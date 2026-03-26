// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Omu.Shared.Resomi.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Movement.Events;

namespace Content.Omu.Shared.Resomi.EntitySystems;

/// <summary>
/// Shared side of the Resomi sprint/impulse system.
/// Applies speed modifiers based on sprint and exhaustion state.
/// </summary>
public abstract class SharedResomiSprintSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<ResomiImpulseComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
    }

    private void OnRefreshMovement(EntityUid uid, ResomiImpulseComponent comp, RefreshMovementSpeedModifiersEvent ev)
    {
        if (comp.IsExhausted || comp.IsExhaustedPending)
        {
            ev.ModifySpeed(comp.ExhaustionSpeedMultiplier, comp.ExhaustionSpeedMultiplier);
        }
        else if (comp.IsSprinting)
        {
            ev.ModifySpeed(comp.SprintSpeedMultiplier, comp.SprintSpeedMultiplier);
        }
    }
}

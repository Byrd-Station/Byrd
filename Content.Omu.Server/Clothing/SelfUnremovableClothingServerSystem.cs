// SPDX-FileCopyrightText: 2025 OmuStation Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Electrocution;
using Content.Shared.Clothing.Components;

namespace Content.Omu.Server.Clothing;

/// <summary>
///     Server-side companion to <see cref="SelfUnremovableClothingSystem"/>.
///     Handles the electric shock that fires when someone without the required
///     access tries to strip an item with <see cref="SelfUnremovableClothingComponent"/>.
///
///     This lives in Content.Omu.Server because ElectrocutionSystem is server-only -
///     the shock cannot be applied from shared code.
///
///     We subscribe to <see cref="SelfUnremovableClothingUnauthorizedStripEvent"/> rather
///     than BeingUnequippedAttemptEvent. The shared system already subscribes to that
///     event for the same component, and SS14 forbids two systems from subscribing to
///     the same (component, event) pair - it crashes with "Duplicate Subscriptions".
///     The shared system raises our custom event after it blocks and throttles the strip,
///     so we never need to worry about cooldowns or access checks here.
/// </summary>
public sealed class SelfUnremovableClothingServerSystem : EntitySystem
{
    [Dependency] private readonly ElectrocutionSystem _electrocution = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SelfUnremovableClothingComponent, SelfUnremovableClothingUnauthorizedStripEvent>(OnUnauthorizedStrip);
    }

    private void OnUnauthorizedStrip(Entity<SelfUnremovableClothingComponent> ent, ref SelfUnremovableClothingUnauthorizedStripEvent args)
    {
        // This item isn't configured to shock - skip.
        if (ent.Comp.UnauthorizedStripShockDamage <= 0)
            return;

        // Apply the electric shock. The electrocution system handles damage, stun,
        // jitter, sound, and sparks automatically. We pass the item as the source
        // so logs and effects point to the right place.
        //
        // No cooldown needed here - the shared system already throttles the custom
        // event to once every 2 seconds, so we'll never be called more than that.
        _electrocution.TryDoElectrocution(
            args.Stripper,
            ent.Owner,
            ent.Comp.UnauthorizedStripShockDamage,
            TimeSpan.FromSeconds(ent.Comp.UnauthorizedStripShockTime),
            refresh: true);
    }
}

// SPDX-FileCopyrightText: 2025 OmuStation Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Electrocution;
using Content.Server.Kitchen.Components;
using Content.Shared.Access.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.DoAfter;
using Content.Shared.Electrocution;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Omu.Server.Clothing;

/// <summary>
///     Server-side companion to <see cref="SelfUnremovableClothingSystem"/>.
///
///     Handles two things that can only run server-side because they need ElectrocutionSystem:
///
///     1. Electric shock when someone without access tries to strip the item the normal way
///        (triggered by <see cref="SelfUnremovableClothingUnauthorizedStripEvent"/>).
///
///     2. Cut-off and tear-off verbs that let anyone bypass the lock given enough time:
///        - Third parties need a sharp tool (shiv, knife, etc.) and 10 seconds.
///        - The wearer can tear it off themselves in 15 seconds, no tool required.
///        - Either way, if they aren't wearing insulated gloves they get a small shock.
///
///     We use <see cref="AlternativeVerb"/> here instead of <see cref="InteractionVerb"/>
///     because the shared system already subscribes (SelfUnremovableClothingComponent,
///     GetVerbsEvent&lt;InteractionVerb&gt;) and SS14 forbids two systems from registering
///     the same (component, event) pair. A different verb type = a different event type,
///     so there's no conflict.
/// </summary>
public sealed class SelfUnremovableClothingServerSystem : EntitySystem
{
    [Dependency] private readonly ElectrocutionSystem _electrocution = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SelfUnremovableClothingComponent, SelfUnremovableClothingUnauthorizedStripEvent>(OnUnauthorizedStrip);
        SubscribeLocalEvent<SelfUnremovableClothingComponent, GetVerbsEvent<AlternativeVerb>>(OnGetCutoffVerbs);
        SubscribeLocalEvent<SelfUnremovableClothingComponent, SelfUnremovableClothingCutoffDoAfterEvent>(OnCutoffDoAfter);
    }

    // ── Unauthorized strip shock ────────────────────────────────────────────

    private void OnUnauthorizedStrip(Entity<SelfUnremovableClothingComponent> ent, ref SelfUnremovableClothingUnauthorizedStripEvent args)
    {
        if (ent.Comp.UnauthorizedStripShockDamage <= 0)
            return;

        // The shared system already throttles this to once every 2 seconds,
        // so we don't need our own cooldown here.
        _electrocution.TryDoElectrocution(
            args.Stripper,
            ent.Owner,
            ent.Comp.UnauthorizedStripShockDamage,
            TimeSpan.FromSeconds(ent.Comp.UnauthorizedStripShockTime),
            refresh: true);
    }

    // ── Cut-off / tear-off verbs ────────────────────────────────────────────

    private void OnGetCutoffVerbs(Entity<SelfUnremovableClothingComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract)
            return;

        // Only relevant while the item is locked.
        if (!TryComp<AccessReaderComponent>(ent, out var reader) || !reader.Enabled)
            return;

        // The item must actually be worn by someone right now.
        if (!_inventory.TryGetContainingEntity(ent.Owner, out var wearer))
            return;

        // Both the wearer and third parties need a sharp tool in hand (shiv, knife, etc.).
        // The difference is just the time: 15s for the wearer, 10s for everyone else.
        // We read the active hand directly - args.Using is only set when dragging an item
        // onto the target, not when simply right-clicking while holding something.
        var heldItem = _hands.GetActiveItem(args.User);
        if (heldItem == null || !TryComp<SharpComponent>(heldItem, out _))
            return;

        var user = args.User;
        var isSelf = user == wearer;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("electropack-verb-cut-off"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/eject.svg.192dpi.png")),
            Act = () => StartCutoffDoAfter(user, wearer.Value, ent, isSelf),
            Priority = 1
        });
    }

    private void StartCutoffDoAfter(EntityUid user, EntityUid wearer, Entity<SelfUnremovableClothingComponent> item, bool isSelf)
    {
        var delay = isSelf
            ? TimeSpan.FromSeconds(item.Comp.SelfCutoffDelay)
            : TimeSpan.FromSeconds(item.Comp.OtherCutoffDelay);

        var doAfterArgs = new DoAfterArgs(EntityManager,
            user,
            delay,
            new SelfUnremovableClothingCutoffDoAfterEvent(),
            eventTarget: item.Owner,
            target: wearer)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = !isSelf,
            DistanceThreshold = 2f,
            MovementThreshold = 0.5f
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    // ── DoAfter completion ──────────────────────────────────────────────────

    private void OnCutoffDoAfter(Entity<SelfUnremovableClothingComponent> ent, ref SelfUnremovableClothingCutoffDoAfterEvent args)
    {
        if (args.Cancelled || args.Target is not { } wearer)
            return;

        // Find which slot the item is in and force-remove it.
        // We iterate instead of hardcoding "back" so this works for any clothing.
        var removed = false;
        var slotEnum = _inventory.GetSlotEnumerator(wearer);
        while (slotEnum.MoveNext(out var container))
        {
            if (container.ContainedEntity != ent.Owner)
                continue;

            _inventory.TryUnequip(wearer, wearer, container.ID, force: true, predicted: false);
            removed = true;
            break;
        }

        if (!removed)
            return;

        // Small shock as a consequence of cutting live electronics with bare hands.
        // Skipped entirely if the actor is wearing fully insulated gloves.
        if (ent.Comp.CutoffShockDamage <= 0)
            return;

        var isInsulated = _inventory.TryGetSlotEntity(args.User, "gloves", out var gloves)
            && TryComp<InsulatedComponent>(gloves.Value, out var insulation)
            && insulation.Coefficient == 0f;

        if (!isInsulated)
        {
            _electrocution.TryDoElectrocution(
                args.User,
                ent.Owner,
                ent.Comp.CutoffShockDamage,
                TimeSpan.FromSeconds(ent.Comp.CutoffShockTime),
                refresh: true);
        }
    }
}

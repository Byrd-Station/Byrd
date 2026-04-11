// SPDX-FileCopyrightText: 2024 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 to4no_fix <156101927+chavonadelal@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared.Clothing.EntitySystems;

/// <summary>
///     Prevents the wearer from removing their own clothing.
///     If the item also has an <see cref="AccessReaderComponent"/>, it gains an ID lock:
///     authorized personnel (Warden, HoS, Captain, etc.) can right-click the item OR
///     the person wearing it to lock or unlock it. While locked, only those same
///     authorized roles can strip it off the wearer.
///
///     How the lock works:
///     - Unlocked (default): anyone can strip it from the wearer, but the wearer still
///       cannot remove it themselves.
///     - Locked: only authorized personnel can strip it. Everyone else gets blocked.
///     - The wearer can NEVER remove it themselves, locked or not.
///
///     A few non-obvious things worth knowing if you touch this code:
///
///     1. Self-removal is cancelled on both client AND server. This is intentional -
///        if we only cancelled on the server, the client would briefly show the item
///        in the player's hand before snapping it back (prediction artifact).
///
///     2. Everything else (access checks, verb execution) is server-only. If the client
///        tried to toggle the lock locally, the server would immediately overwrite it on
///        the next state sync, causing the action to fire dozens of times in a loop.
///
///     3. The strip UI sends a strip event every tick while the button is held (~60/s).
///        We cache the "denied" result for 2 seconds so we don't run a full access check
///        on every single tick - that was causing "MainLoop: Cannot keep up" on the server.
///
///     4. The verb uses AreAccessTagsAllowed() instead of IsAllowed() to check who can
///        see it. IsAllowed() returns true for everyone when the lock is disabled (unlocked
///        state), so using it would make the verb appear enabled for all players.
///
///     5. The verb Act re-reads the AccessReaderComponent at execution time instead of
///        using the one captured when the verb was built. Network sync can swap out the
///        component object between those two moments, leaving us with a stale reference.
///        There is also a guard to skip the action if the state already changed, which
///        prevents a double-toggle if the engine somehow fires Act more than once.
/// </summary>
public sealed class SelfUnremovableClothingSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    /// <summary>
    ///     Tracks when we last denied a strip attempt per entity.
    ///     The strip UI fires the unequip event every tick (~60/s) while the button is held,
    ///     so without this we'd run an access check and show a popup hundreds of times a second.
    ///     When someone is denied, we record the time here and skip them for 2 seconds.
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _noAccessPopupCooldowns = new();

    private static readonly TimeSpan PopupCooldown = TimeSpan.FromSeconds(2);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SelfUnremovableClothingComponent, BeingUnequippedAttemptEvent>(OnUnequip);
        SubscribeLocalEvent<SelfUnremovableClothingComponent, ExaminedEvent>(OnUnequipMarkup);

        // Show the lock/unlock verb when right-clicking the electropack item itself.
        SubscribeLocalEvent<SelfUnremovableClothingComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbsOnItem);

        // Also show the verb when right-clicking the person wearing it.
        // We subscribe on InventoryComponent so we can iterate their worn items.
        SubscribeLocalEvent<InventoryComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbsOnWearer);
    }

    private void OnUnequip(Entity<SelfUnremovableClothingComponent> selfUnremovableClothing, ref BeingUnequippedAttemptEvent args)
    {
        // The inventory system can relay this event multiple times for the same attempt.
        // If it was already cancelled upstream, nothing left to do.
        if (args.Cancelled)
            return;

        if (TryComp<ClothingComponent>(selfUnremovableClothing, out var clothing) && (clothing.Slots & args.SlotFlags) == SlotFlags.NONE)
            return;

        // The wearer can never remove this item themselves, no matter the lock state.
        // We cancel on both client and server here - if we only cancelled on the server,
        // the client would show the item briefly in the player's hand before correcting itself.
        if (args.UnEquipTarget == args.Unequipee)
        {
            args.Cancel();
            return;
        }

        // From here on we're dealing with someone else trying to strip the item.
        // Only the server decides whether that's allowed - skip on the client.
        if (_netMan.IsClient)
            return;

        // If this person was already denied recently, block them again without
        // re-running the full access check. The strip UI fires this event every
        // tick while held, so we cache the result for 2 seconds to avoid lag.
        var now = _timing.CurTime;
        if (_noAccessPopupCooldowns.TryGetValue(args.Unequipee, out var lastBlocked)
            && now - lastBlocked < PopupCooldown)
        {
            args.Cancel();
            return;
        }

        // No AccessReader means no lock - anyone can strip it.
        if (!TryComp<AccessReaderComponent>(selfUnremovableClothing, out var reader))
            return;

        // IsAllowed returns true for everyone when the reader is disabled (unlocked),
        // and checks ID card access when it's enabled (locked).
        if (!_accessReader.IsAllowed(args.Unequipee, selfUnremovableClothing, reader))
        {
            args.Cancel();
            _noAccessPopupCooldowns[args.Unequipee] = now;
            _popup.PopupEntity(Loc.GetString("comp-self-unremovable-clothing-no-access"), args.Unequipee, args.Unequipee);

            // Let server systems react - e.g. apply an electric shock. We raise a custom
            // event here instead of letting them also subscribe to BeingUnequippedAttemptEvent,
            // because SS14 forbids two systems from subscribing to the same (component, event) pair.
            RaiseLocalEvent(selfUnremovableClothing.Owner, new SelfUnremovableClothingUnauthorizedStripEvent { Stripper = args.Unequipee });
        }
    }

    /// <summary>
    ///     Adds the lock/unlock verb when right-clicking the electropack item directly.
    /// </summary>
    private void OnGetVerbsOnItem(Entity<SelfUnremovableClothingComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        // GetVerbsEvent is broadcast to every entity with this component in the world,
        // not just the one being right-clicked. Skip any that aren't the actual target.
        if (ent.Owner != args.Target)
            return;

        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!TryComp<AccessReaderComponent>(ent, out var reader))
            return;

        TryAddLockVerb(ent, reader, args.User, args.Verbs);
    }

    /// <summary>
    ///     Adds the lock/unlock verb when right-clicking the person wearing the electropack.
    ///     Iterates their inventory to find any SelfUnremovableClothing items with AccessReader.
    /// </summary>
    private void OnGetVerbsOnWearer(Entity<InventoryComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (ent.Owner != args.Target)
            return;

        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;

        // Go through every equipped slot and look for items with the lock mechanic.
        var slotEnum = _inventory.GetSlotEnumerator(ent.Owner);
        while (slotEnum.MoveNext(out var slot))
        {
            var itemUid = slot.ContainedEntity;
            if (itemUid == null)
                continue;

            if (!TryComp<SelfUnremovableClothingComponent>(itemUid, out _))
                continue;

            if (!TryComp<AccessReaderComponent>(itemUid, out var reader))
                continue;

            TryAddLockVerb(itemUid.Value, reader, user, args.Verbs);
        }
    }

    /// <summary>
    ///     Builds and adds the lock/unlock verb. Called by both verb handlers so the
    ///     logic isn't duplicated.
    /// </summary>
    private void TryAddLockVerb(EntityUid item, AccessReaderComponent reader, EntityUid user, SortedSet<InteractionVerb> verbs)
    {
        var locked = reader.Enabled;

        // We need to know if the user has the right access to show the verb as enabled.
        // We can't use IsAllowed() here because when the lock is off (Enabled = false),
        // IsAllowed() returns true for everyone - meaning unauthorized players would see
        // the verb as clickable. AreAccessTagsAllowed() checks the access list directly.
        var accessItems = _accessReader.FindPotentialAccessItems(user);
        var accessTags = _accessReader.FindAccessTags(user, accessItems);
        _accessReader.FindStationRecordKeys(user, out var stationKeys, accessItems);
        var hasAccess = _accessReader.AreAccessTagsAllowed(accessTags, reader)
                     || _accessReader.AreStationRecordKeysAllowed(stationKeys, reader);

        verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString(locked ? "electropack-verb-unlock" : "electropack-verb-lock"),
            Icon = new SpriteSpecifier.Texture(new ResPath(locked
                ? "/Textures/Interface/VerbIcons/unlock.svg.192dpi.png"
                : "/Textures/Interface/VerbIcons/lock.svg.192dpi.png")),
            // Disabled = true makes the verb appear grayed out. The engine shows Message
            // as a hover tooltip and never calls Act, so no extra code needed for that.
            Disabled = !hasAccess,
            Message = hasAccess ? null : Loc.GetString("comp-self-unremovable-clothing-no-access"),
            Act = () =>
            {
                // This system runs on both client and server. If the client tried to toggle
                // the lock locally, the server would overwrite it on the next state sync,
                // which causes the action to loop dozens of times. Server-only fixes that.
                if (_netMan.IsClient)
                    return;

                // Re-read the component fresh instead of using the one captured above.
                // Network sync can replace the component object between when the verb was
                // built and when the player actually clicks it, leaving us a stale reference.
                if (!TryComp<AccessReaderComponent>(item, out var currentReader))
                    return;

                // If the state already changed since the verb was built, skip the action.
                // This prevents a double-toggle if the engine somehow fires Act twice.
                if (currentReader.Enabled != locked)
                    return;

                _accessReader.SetActive((item, currentReader), !locked);
                _popup.PopupEntity(
                    Loc.GetString(!locked ? "electropack-locked" : "electropack-unlocked"),
                    item,
                    user);
            }
        });
    }

    private void OnUnequipMarkup(Entity<SelfUnremovableClothingComponent> selfUnremovableClothing, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("comp-self-unremovable-clothing"));

        // If the item has a lock, also tell the examiner whether it's currently locked.
        if (TryComp<AccessReaderComponent>(selfUnremovableClothing, out var reader))
        {
            args.PushMarkup(Loc.GetString(reader.Enabled
                ? "comp-self-unremovable-clothing-locked"
                : "comp-self-unremovable-clothing-unlocked"));
        }
    }
}

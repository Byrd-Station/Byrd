// Portions taken from Monolith (https://github.com/monolith-station/monolith), credit tonotom1.
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Electronics; // Omustation
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.UserInterface; // Omustation
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Shared.SmartFridge;

public sealed class SmartFridgeSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!; // Omustation

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SmartFridgeComponent, ComponentStartup>(OnStartup); // Omustation
        SubscribeLocalEvent<SmartFridgeComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<SmartFridgeComponent, EntRemovedFromContainerMessage>(OnItemRemoved);

        SubscribeLocalEvent<SmartFridgeComponent, GetDumpableVerbEvent>(OnGetDumpableVerb);
        SubscribeLocalEvent<SmartFridgeComponent, DumpEvent>(OnDump);

        SubscribeLocalEvent<SmartFridgeComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt); // Omustation
        SubscribeLocalEvent<SmartFridgeComponent, OnAccessOverriderAccessUpdatedEvent>(OnAccessOverriderUpdated); // Omustation
        SubscribeLocalEvent<SmartFridgeComponent, EntInsertedIntoContainerMessage>(OnBoardInserted); // Omustation

        Subs.BuiEvents<SmartFridgeComponent>(SmartFridgeUiKey.Key,
            sub =>
            {
                sub.Event<SmartFridgeDispenseItemMessage>(OnDispenseItem);
                sub.Event<SmartFridgeRemoveEntryMessage>(OnRemoveEntry); // Monolith
            });
    }

    // Start of Omustation
    /// <summary>
    /// Applies YAML-defined access configuration to the fridge's AccessReader on startup.
    /// If no access is defined, the fridge remains open to all.
    /// </summary>
    private void OnStartup(Entity<SmartFridgeComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.Access == null || ent.Comp.Access.Count == 0)
            return;

        ent.Comp.RequireAccess = true;

        if (!TryComp<AccessReaderComponent>(ent, out var reader))
            return;

        _accessReader.SetAccesses((ent.Owner, reader), ent.Comp.Access);
    }
    // End of Omustation

    private bool DoInsert(Entity<SmartFridgeComponent> ent, EntityUid user, IEnumerable<EntityUid> usedItems, bool playSound)
    {
        if (!_container.TryGetContainer(ent, ent.Comp.Container, out var container))
            return false;

        if (!Allowed(ent, user))
            return true;

        bool anyInserted = false;
        foreach (var used in usedItems)
        {
            if (!_whitelist.CheckBoth(used, ent.Comp.Blacklist, ent.Comp.Whitelist))
                continue;
            anyInserted = true;

            _container.Insert(used, container);
            var key = new SmartFridgeEntry(Identity.Name(used, EntityManager));
            if (!ent.Comp.Entries.Contains(key))
                ent.Comp.Entries.Add(key);

            ent.Comp.ContainedEntries.TryAdd(key, new());
            var entries = ent.Comp.ContainedEntries[key];
            if (!entries.Contains(GetNetEntity(used)))
                entries.Add(GetNetEntity(used));

            Dirty(ent);
        }

        if (anyInserted && playSound)
        {
            _audio.PlayPredicted(ent.Comp.InsertSound, ent, user);
        }

        return anyInserted;
    }

    private void OnInteractUsing(Entity<SmartFridgeComponent> ent, ref InteractUsingEvent args)
    {
        if (!_hands.CanDrop(args.User, args.Used))
            return;

        args.Handled = DoInsert(ent, args.User, [args.Used], true);
    }

    private void OnItemRemoved(Entity<SmartFridgeComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        var key = new SmartFridgeEntry(Identity.Name(args.Entity, EntityManager));

        if (ent.Comp.ContainedEntries.TryGetValue(key, out var contained))
        {
            contained.Remove(GetNetEntity(args.Entity));
        }

        Dirty(ent);
    }

    private bool Allowed(Entity<SmartFridgeComponent> machine, EntityUid user)
    {
        if (!machine.Comp.RequireAccess) // Omustation
            return true;

        if (_accessReader.IsAllowed(user, machine))
            return true;

        _popup.PopupPredicted(Loc.GetString("smart-fridge-component-try-eject-access-denied"), machine, user);
        _audio.PlayPredicted(machine.Comp.SoundDeny, machine, user);
        return false;
    }

    // Start of Omustation
    /// <summary>
    /// Cancels the UI open attempt if the user does not have the required access.
    /// </summary>
    private void OnOpenAttempt(Entity<SmartFridgeComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!Allowed(ent, args.User))
            args.Cancel();
    }

    /// <summary>
    /// Syncs <see cref="SmartFridgeComponent.RequireAccess"/> with the AccessReader when
    /// an access overrider updates the fridge's access lists.
    /// </summary>
    private void OnAccessOverriderUpdated(Entity<SmartFridgeComponent> ent, ref OnAccessOverriderAccessUpdatedEvent args)
    {
        if (!TryComp<AccessReaderComponent>(ent, out var reader))
            return;

        ent.Comp.RequireAccess = reader.AccessLists.Count > 0;
        Dirty(ent);
    }

    /// <summary>
    /// When a configured SmartFridge machine board is inserted into the machine_board container,
    /// copies its access lists to the fridge's AccessReader and enables access enforcement.
    /// This allows access to be pre-configured on the board before installation.
    /// </summary>
    private void OnBoardInserted(Entity<SmartFridgeComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != "machine_board")
            return;

        if (!TryComp<DoorElectronicsComponent>(args.Entity, out _))
            return;

        if (!TryComp<AccessReaderComponent>(args.Entity, out var boardReader) || boardReader.AccessLists.Count == 0)
            return;

        var fridgeReader = EnsureComp<AccessReaderComponent>(ent);
        _accessReader.SetAccesses((ent.Owner, fridgeReader), boardReader.AccessLists);
        ent.Comp.RequireAccess = true;
        Dirty(ent);
    }
    // End of Omustation

    private void OnDispenseItem(Entity<SmartFridgeComponent> ent, ref SmartFridgeDispenseItemMessage args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (!Allowed(ent, args.Actor))
            return;

        if (!ent.Comp.ContainedEntries.TryGetValue(args.Entry, out var contained))
        {
            _audio.PlayPredicted(ent.Comp.SoundDeny, ent, args.Actor);
            _popup.PopupPredicted(Loc.GetString("smart-fridge-component-try-eject-unknown-entry"), ent, args.Actor);
            return;
        }

        foreach (var item in contained)
        {
            if (!_container.TryRemoveFromContainer(GetEntity(item)))
                continue;

            _audio.PlayPredicted(ent.Comp.SoundVend, ent, args.Actor);
            contained.Remove(item);
            Dirty(ent);
            return;
        }

        _audio.PlayPredicted(ent.Comp.SoundDeny, ent, args.Actor);
        _popup.PopupPredicted(Loc.GetString("smart-fridge-component-try-eject-out-of-stock"), ent, args.Actor);
    }

    // Monolith start
    private void OnRemoveEntry(Entity<SmartFridgeComponent> ent, ref SmartFridgeRemoveEntryMessage args)
    {
        if (!Allowed(ent, args.Actor))
            return;

        if (!ent.Comp.ContainedEntries.TryGetValue(args.Entry, out var contained)
            || contained.Count > 0
            || !ent.Comp.Entries.Contains(args.Entry))
            return;

        ent.Comp.Entries.Remove(args.Entry);
        ent.Comp.ContainedEntries.Remove(args.Entry);
        Dirty(ent);
    }
    // Monolith end

    private void OnGetDumpableVerb(Entity<SmartFridgeComponent> ent, ref GetDumpableVerbEvent args)
    {
        if (_accessReader.IsAllowed(args.User, ent))
        {
            args.Verb = Loc.GetString("dump-smartfridge-verb-name", ("unit", ent));
        }
    }

    private void OnDump(Entity<SmartFridgeComponent> ent, ref DumpEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        args.PlaySound = true;

        DoInsert(ent, args.User, args.DumpQueue, false);
    }
}

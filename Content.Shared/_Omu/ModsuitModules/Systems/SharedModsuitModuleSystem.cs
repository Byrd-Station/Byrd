using Content.Shared.Containers.ItemSlots;
using Content.Shared._Omu.ModsuitModules.Components;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Shared._Omu.ModsuitModules.Systems;

public abstract class SharedModsuitModuleSystem : EntitySystem
{
    [Dependency] protected readonly IGameTiming Timing = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly ISerializationManager _serManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModsuitModuleSlotComponent, ItemSlotInsertAttemptEvent>(OnModuleInserted);
        SubscribeLocalEvent<ModsuitModuleSlotComponent, ItemSlotEjectAttemptEvent>(OnModuleRemoved);
        SubscribeLocalEvent<ModsuitModuleSlotComponent, ContainerIsInsertingAttemptEvent>(OnModuleInsertAttempt);
    }
    private void AddComponents(EntityUid modsuit,
        ComponentRegistry reg)
    {
        foreach (var (key, comp) in reg)
        {
            var compType = comp.Component.GetType();
            if (HasComp(modsuit, compType))
                continue;

            var newComp = (Component) _serManager.CreateCopy(comp.Component, notNullableOverride: true);
            newComp.Owner = modsuit;
            EntityManager.AddComponent(modsuit, newComp, true);
            if (newComp.NetSyncEnabled)
            {
                Dirty(modsuit, newComp);
            }
        }
    }

    private void RemoveComponents(EntityUid modsuit,
        ComponentRegistry reg)
    {
        foreach (var (key, comp) in reg)
        {
            RemComp(modsuit, comp.Component.GetType());
        }
    }
    private void OnModuleInsertAttempt(EntityUid uid, ModsuitModuleSlotComponent component, ContainerIsInsertingAttemptEvent args)
    {
        if (!component.Initialized)
        {
            args.Cancel();
            return;
        }

        if (args.Container.ID != component.ModuleSlotId)
            return;

        if (!HasComp<ModsuitModuleComponent>(args.EntityUid))
        {
            args.Cancel();
        }
    }

    private void OnModuleInserted(EntityUid uid, ModsuitModuleSlotComponent component, ItemSlotInsertAttemptEvent args)
    {
        if (!component.Initialized)
        {
            args.Cancelled = true;
            return;
        }

        if (args.Slot.ID != component.ModuleSlotId)
            return;

        //RaiseLocalEvent(uid, new ModsuitModuleChangedEvent(false), false);

        TryComp<ModsuitModuleComponent>(args.Item, out var comp);
        if (comp != null && comp.OnAdd != null)
        {
            AddComponents(uid, comp.OnAdd);
        }

    }

    protected virtual void OnModuleRemoved(EntityUid uid, ModsuitModuleSlotComponent component, ItemSlotEjectAttemptEvent args)
    {
        if (args.Slot.ID != component.ModuleSlotId)
            return;

        //RaiseLocalEvent(uid, new ModsuitModuleChangedEvent(true), false);

        TryComp<ModsuitModuleComponent>(args.Item, out var comp);
        if (comp != null && comp.OnAdd != null)
        {
            RemoveComponents(uid, comp.OnAdd);
        }

    }
}

using Content.Shared._Omu.Clothing.Components;
using Content.Shared._Omu.Traits;
using Content.Shared.Inventory.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Omu.Clothing.EntitySystems;

public sealed class CyberneticMantleSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ItemToggleSystem _itemToggleSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CyberneticMantleComponent, BeingEquippedAttemptEvent>(OnBeingEquipped, after: [typeof(ClothingTakesUpExtraSlotsSystem)]);
        SubscribeLocalEvent<CyberneticMantleComponent, GotUnequippedEvent>(OnUnequipped);
    }

    /// <summary>
    ///     A list of things to do while the mantle is equpped.
    /// </summary>
    private void OnBeingEquipped(Entity<CyberneticMantleComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        // if the compnent can be toggled, turn it on when worn by a Beast.
        if (TryComp<ItemToggleComponent>(ent, out var itemToggle))
            if (TryComp<CyberneticBeastComponent>(args.Equipee, out var _))
                _itemToggleSystem.TrySetActive(new Entity<ItemToggleComponent?>(ent.Owner, itemToggle), true, args.Equipee, true); // throws an exception if predicted.

    }

    private void OnUnequipped(Entity<CyberneticMantleComponent> ent, ref GotUnequippedEvent args)
    {
        // if the compnent can be toggled, turn it off when unequipped.
        if (TryComp<ItemToggleComponent>(ent, out var itemToggle))
            _itemToggleSystem.TrySetActive(new Entity<ItemToggleComponent?>(ent.Owner, itemToggle), false, args.Equipee, false);
    }

}
using Content.Shared._Omu.Clothing.Components;
using Content.Shared._Omu.Traits;
using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Omu.Clothing.EntitySystems;

public sealed class CyberneticMantleSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ItemToggleSystem _itemToggleSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

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

        // make sure that the person equipping the mantle is a cybernetic beast
        if (TryComp<CyberneticBeastComponent>(args.EquipTarget, out var _))
        {
            // if the compnent can be toggled, turn it on when worn
            if (TryComp<ItemToggleComponent>(ent, out var itemToggle))
                _itemToggleSystem.TrySetActive(new Entity<ItemToggleComponent?>(ent.Owner, itemToggle), true, args.EquipTarget, true);

            // get the appearance of the beast
            if (TryComp<HumanoidAppearanceComponent>(args.EquipTarget, out var humanoidAppearance))
            {
                // set the eye colour of the mantle to the eye colour of the beast
                if (TryComp<ClothingComponent>(ent, out var clothingComp))
                    if (clothingComp.ClothingVisuals.TryGetValue("head", out var layerData))
                        if (layerData.TryGetValue(1, out var eyesLayer))
                            eyesLayer.Color = humanoidAppearance.EyeColor;

            }
        }

    }

    private void OnUnequipped(Entity<CyberneticMantleComponent> ent, ref GotUnequippedEvent args)
    {
        // if the compnent can be toggled, turn it off when unequipped.
        if (TryComp<ItemToggleComponent>(ent, out var itemToggle))
            if (_gameTiming.ApplyingState) // removing a component while resetting predicted entities will throw an exception, so don't do that.
                _itemToggleSystem.TrySetActive(new Entity<ItemToggleComponent?>(ent.Owner, itemToggle), false, args.Equipee, false);
    }

}
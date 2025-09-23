using System.Linq;
using Content.Shared._Omu.Clothing.Components;
using Content.Shared._Omu.Traits;
using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Omu.Clothing.EntitySystems;

public sealed class CyberneticMantleSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ItemToggleSystem _itemToggleSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedPointLightSystem _lightSystem = default!;
    [Dependency] private readonly INetManager _netManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CyberneticMantleComponent, BeingEquippedAttemptEvent>(OnBeingEquipped, after: [typeof(ClothingTakesUpExtraSlotsSystem)]);
        SubscribeLocalEvent<CyberneticMantleComponent, GotUnequippedEvent>(OnUnequipped);
    }

    /// <summary>
    ///     A list of things to do when the mantle is equpped. Enables the mantle's ItemToggleComponent.
    /// </summary>
    private void OnBeingEquipped(Entity<CyberneticMantleComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        // if the client is resetting predicted entities, trying to modify the components on the mantle will result in an exception. So don't do it.
        if (_gameTiming.ApplyingState && _netManager.IsClient)
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
                // clamp the beast's eye color to a reasonable brightness; no tiders with stealth mantles allowed
                var eyeColorHsv = Color.ToHsv(humanoidAppearance.EyeColor);
                eyeColorHsv.Z = Math.Clamp(eyeColorHsv.Z, ent.Comp.MinEyeColorLevel, 1.0f);
                var eyeColor = Color.FromHsv(eyeColorHsv);

                // set the eye colour of the mantle to the (clamped) eye colour of the beast
                SetEyeColor(ent, eyeColor);

                // if this visor emits light, make that light take on the clamped eye colour of the beast
                _lightSystem.SetColor(ent, eyeColor);
            }
        }

    }

    /// <summary>
    ///     A list of things to do when the mantle is unequipped. Disables the mantle's ItemToggleComponent.
    /// </summary>>
    private void OnUnequipped(Entity<CyberneticMantleComponent> ent, ref GotUnequippedEvent args)
    {
        // if the client is resetting predicted entities, trying to modify the components on the mantle will result in an exception. So don't do it.
        if (_gameTiming.ApplyingState && _netManager.IsClient)
            return;

        // if the compnent can be toggled, turn it off when unequipped.
        if (TryComp<ItemToggleComponent>(ent, out var itemToggle))
            _itemToggleSystem.TrySetActive(new Entity<ItemToggleComponent?>(ent.Owner, itemToggle), false, args.Equipee, true);
    }

    /// <summary>
    ///     Sets the colour of the eyes-layer of the mantle to the specified colour.
    /// </summary>
    private void SetEyeColor(Entity<CyberneticMantleComponent> ent, Color color)
    {
        if (TryComp<ClothingComponent>(ent, out var clothingComp)
            && clothingComp.ClothingVisuals.TryGetValue("head", out var layerData))
            layerData.Last().Color = color;
    }

}
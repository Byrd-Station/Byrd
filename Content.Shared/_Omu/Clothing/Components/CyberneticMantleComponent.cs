using Content.Shared._Omu.Clothing.EntitySystems;
using Content.Shared._Omu.Traits;
using Content.Shared.Inventory;
using Robust.Shared.GameStates;

namespace Content.Shared._Omu.Clothing.Components;

/// <summary>
///     This component indicates that an item is a Cybernetic Beast's mantle.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(CyberneticMantleSystem), typeof(CyberneticBeastSystem))]
public sealed partial class CyberneticMantleComponent : Component, IClothingSlots
{

    [DataField]
    public SlotFlags Slots { get; set; } = SlotFlags.WITHOUT_POCKET;
}

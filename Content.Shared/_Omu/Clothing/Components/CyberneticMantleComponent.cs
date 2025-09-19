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

    /// <summary>
    ///     The mantle will relay an event to these inventory slots when equpped.
    /// </summary>
    [DataField]
    public SlotFlags Slots { get; set; } = SlotFlags.HEAD;

    /// <summary>
    ///     The minimum color level of the eyes on the visor.
    ///     In effect, if a player has (for example) eyes with a red value of less than 1/2, and this value is set to 0.5f,
    ///     the red level on the visor will be increased to 0.5 in order to prevent pure black visors from being abused.
    /// </summary>
    [DataField]
    public float MinEyeColorLevel { get; set; } = 0.5f;
}

using Robust.Shared.GameStates;

namespace Content.Shared._Omu.Traits.TraitComponents;

// Ported from EE

/// <summary>
/// This is used for any trait that modifies climbing speed.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public abstract partial class ClimbDelayModifierComponent : Component
{
/// <summary>
///     What to multiply the climbing delay by.
/// </summary>
[DataField, AutoNetworkedField]
public float ClimbDelayMultiplier = 1f;
}

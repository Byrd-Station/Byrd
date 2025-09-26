using Content.Shared._Omu.Traits;
using Robust.Shared.GameStates;

namespace Content.Shared._Omu.Traits.Assorted;

/// <summary>
/// Used for the Photophobia trait.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedPhotophobiaSystem))]
public sealed partial class PhotophobiaComponent : Component
{

}
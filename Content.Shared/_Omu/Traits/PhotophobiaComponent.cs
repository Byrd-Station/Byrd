using Robust.Shared.GameStates;

namespace Content.Shared._Omu.Traits;

/// <summary>
///     Used for the Photophobia trait.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PhotophobiaComponent : Component
{
    /// <summary>
    ///     When the player has a flashlight toggled at them, how long should it flash them for?
    /// </summary>
    [DataField]
    public float FlashDuration = 2f;

    /// <summary>
    ///     When a player has a flashlight toggled at them, should it slow them down? By how much?
    ///     This should be a value between zero and one, where zero is maximum slowdown.
    /// </summary>
    [DataField]
    public float FlashSlowdown = 1f;
}
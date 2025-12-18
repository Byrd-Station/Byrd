using Robust.Shared.GameStates;

namespace Content.Omu.Server.HereticalTome.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class FascinationComponent : Component
{
    /// <summary>
    /// The current intensity of the fascination effect, where higher values indicate stronger effects.
    /// </summary>
    [DataField]
    public float FascinationInt = 0f;

    /// <summary>
    /// A localized description of the current fascination effect.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? FascinationDesc;
}

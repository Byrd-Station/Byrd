using Robust.Shared.GameStates;

namespace Content.Omu.Server.HereticalTome.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class FascinationComponent : Component
{
    /// <summary>
    /// The level of madness
    /// </summary>
    [DataField]
    public float FascinationInt = 0f;

    [DataField, AutoNetworkedField]
    public string? FascinationDesc;
}

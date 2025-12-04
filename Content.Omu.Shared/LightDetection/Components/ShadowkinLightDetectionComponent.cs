using Robust.Shared.GameStates;

namespace Content.Omu.Shared.ShadowkinLightDetection.Components;

/// <summary>
/// This is used for detecting if an entity is near a lighted area
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(false, true)]
public sealed partial class ShadowkinLightDetectionComponent : Component
{
    /// <summary>
    /// Current light level that entity gets from all light sources in radius
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public float CurrentLightLevel;

    /// <summary>
    /// Minimum light level for entity to be on light
    /// </summary>
    [DataField]
    public float OnLightLevel = 2f;

    public bool OnLight => CurrentLightLevel > OnLightLevel;
}

using Robust.Shared.GameStates;

namespace Content.Server.Procedural.Components;

[RegisterComponent]
public sealed partial class ScrapShipDoorTrapComponent : Component
{
    [DataField]
    public string ExplosionType = "Default";

    [DataField]
    public float TotalIntensity = 95f;

    [DataField]
    public float Slope = 3f;

    [DataField]
    public float MaxTileIntensity = 12f;

    [DataField]
    public bool CanCreateVacuum;

    [DataField]
    public bool Triggered;
}

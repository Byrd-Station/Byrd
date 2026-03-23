using Content.Server._Impstation.Thaven.Systems;
using Content.Shared.Atmos;

namespace Content.Server._Impstation.Thaven.Components;

[RegisterComponent]
[Access(typeof(ThavenBreatherSystem))]
public sealed partial class ThavenBreatherComponent : Component
{
    [DataField]
    public float MinPressure = Atmospherics.HazardLowPressure;

    [DataField]
    public float SaturationPerBreath = 5f;
}

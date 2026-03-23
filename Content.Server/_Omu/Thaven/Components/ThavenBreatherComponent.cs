using Content.Server._Omu.Thaven.Systems;
using Content.Shared.Atmos;

namespace Content.Server._Omu.Thaven.Components;

[RegisterComponent]
[Access(typeof(ThavenBreatherSystem))]
public sealed partial class ThavenBreatherComponent : Component
{
    [DataField]
    public float MinPressure = Atmospherics.HazardLowPressure;

    [DataField]
    public float SaturationPerBreath = 5f;
}


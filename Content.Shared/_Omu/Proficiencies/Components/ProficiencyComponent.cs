using Robust.Shared.Prototypes;
using Content.Shared._Omu.Proficiencies.Systems;

namespace Content.Shared._Omu.Proficiencies;

[RegisterComponent, Access(typeof(ProficiencySystem))]
public sealed partial class ProficiencyComponent : Component
{
    [DataField]
    public List<EntProtoId>? Items;

    [DataField]
    public float proficiencyMultiplier { get; set; } = 1f;

    [DataField]
    public float surgeryProficiency { get; set; } = 1f;

    //the lower the reloadspeed modifier the faster it goes
    [DataField]
    public float reloadSpeedProficiency { get; set; } = 1f;

    [DataField]
    public string? proficiencyID;
}

using Robust.Shared.Prototypes;
namespace Content.Omu.Shared.Proficiencies.Components;

[RegisterComponent]
public sealed partial class ProficiencyComponent : Component
{
    [DataField]
    public List<EntProtoId>? Items;

    [DataField]
    public float ProficiencyMultiplier { get; set; } = 1f;

    [DataField]
    public float SurgeryProficiency { get; set; } = 1f;

    //the lower the reloadspeed modifier the faster it goes
    [DataField]
    public float ReloadSpeedProficiency { get; set; } = 1f;

    [DataField]
    public string? ProficiencyID;
}

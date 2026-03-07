using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;



namespace Content.Omu.Shared.Proficiencies;

[Prototype("Proficiency")]
[DataDefinition]
public sealed partial class ProficiencyPrototype : IPrototype
{

    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<EntProtoId>? Items;

    [DataField]
    public float proficiencyMultiplier { get; private set; } = 1f;

    [DataField]
    public float surgeryProficiency { get; private set; } = 1f;

    //the lower the reloadspeed modifier the faster it goes
    [DataField]
    public float reloadSpeedProficiency { get; private set; } = 1f;
}

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
    public float ProficiencyMultiplier { get; private set; } = 1f;
   
    [DataField]
    public float SurgeryProficiency { get; private set; } = 1f;

    /// <summary>
    /// The lower the Reload speed modifier the faster it goes
    /// </summary>
    [DataField]
    public float ReloadSpeedProficiency { get; private set; } = 1f;
}

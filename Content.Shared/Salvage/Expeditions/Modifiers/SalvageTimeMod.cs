using Robust.Shared.Prototypes;

namespace Content.Shared.Salvage.Expeditions.Modifiers;

[Prototype("salvageTimeMod")]
public sealed class SalvageTimeMod : IPrototype, ISalvageMod
{
    [IdDataField] public string ID { get; } = default!;

    [DataField("desc")] public LocId Description { get; private set; } = default;

    /// <summary>
    /// Cost for difficulty modifiers.
    /// </summary>
    [DataField("cost")]
    public float Cost { get; private set; }

    [DataField("minDuration")]
    public int MinDuration = 630;

    [DataField("maxDuration")]
    public int MaxDuration = 570;
}

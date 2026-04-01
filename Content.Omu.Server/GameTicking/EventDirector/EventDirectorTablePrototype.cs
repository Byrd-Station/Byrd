using Robust.Shared.Prototypes;

namespace Content.Omu.Server.GameTicking.EventDirector;

/// <summary>
/// A single roll table used by the event director.
/// Each entry points at an existing gamerule prototype so the director owns timing/selection while the rule keeps its own gameplay logic.
/// </summary>
[Prototype("eventDirectorTable")]
public sealed partial class EventDirectorTablePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<EventDirectorTableEntry> Entries { get; private set; } = new();
}

/// <summary>
/// One weighted candidate inside a director table.
/// The filters here are intentionally simple for the first version so staff can understand why an entry was or was not eligible.
/// </summary>
// this type only lives on the server — no NetSerializable needed
[DataDefinition]
public sealed partial class EventDirectorTableEntry
{
    [DataField(required: true)]
    public string Id = default!;

    [DataField]
    public string? Name;

    [DataField(required: true)]
    public EntProtoId Rule = default!;

    [DataField]
    public float Weight = 1f;

    [DataField]
    public int MinimumPlayers = 0;

    [DataField]
    public int MaximumPlayers = int.MaxValue;

    [DataField]
    public int EarliestStartMinutes = 0;

    [DataField]
    public int MaximumOccurrences = -1;

    [DataField]
    public int RepeatDelayMinutes = 0;
}

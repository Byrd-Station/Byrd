using Content.Shared.Access.Components;
using Content.Shared.StationRecords;

namespace Content.Server._Omu.Saboteur.Conditions;

/// <summary>
/// Represents a crew member eligible for targeting by saboteur objectives.
/// </summary>
public readonly record struct CrewTarget(
    EntityUid Holder,
    EntityUid CardEntity,
    IdCardComponent IdCard,
    StationRecordKey Key);

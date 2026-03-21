using Content.Shared.Maps;
using Robust.Shared.Noise;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.DungeonGenerators;

/// <summary>
/// Generates a medium-sized ship hull shape on an empty grid.
/// Deprecated - use <see cref="ShipHullDunGen"/> instead.
/// </summary>
public sealed partial class MediumShipDunGen : IDunGenLayer
{
    [DataField]
    public int HalfLength = 12;

    [DataField]
    public int HalfWidth = 7;

    [DataField]
    public FastNoiseLite? EdgeNoise;

    [DataField]
    public float EdgeNoiseAmplitude = 1.0f;

    [DataField]
    public float BowTaper = 0.45f;

    [DataField]
    public float SternTaper = 0.2f;

    [DataField]
    public ProtoId<ContentTileDefinition> FloorTile = "FloorSteel";

    [DataField]
    public ProtoId<ContentTileDefinition> HullTile = "Plating";

    public ShipHullDunGen ToShipHull() => new()
    {
        HalfLength = HalfLength,
        HalfWidth = HalfWidth,
        EdgeNoise = EdgeNoise,
        EdgeNoiseAmplitude = EdgeNoiseAmplitude,
        BowTaper = BowTaper,
        SternTaper = SternTaper,
        FloorTile = FloorTile,
        HullTile = HullTile,
    };
}

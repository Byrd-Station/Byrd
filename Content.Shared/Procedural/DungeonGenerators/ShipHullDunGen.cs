using Content.Shared.Maps;
using Robust.Shared.Noise;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Procedural.DungeonGenerators;

/// <summary>
/// Hull profile family. Controls the fundamental silhouette shape before taper/noise.
/// </summary>
[Serializable, NetSerializable]
public enum ShipHullProfile : byte
{
    /// <summary>Standard tapered ellipse (original behaviour).</summary>
    Ellipse,
    /// <summary>Rectangular hull with rounded corners - boxy freighter look.</summary>
    Box,
    /// <summary>Diamond / rhombus shape - aggressive pointed silhouette.</summary>
    Diamond,
    /// <summary>Asymmetric hull with a wider port or starboard side.</summary>
    Asymmetric,
}

/// <summary>
/// Generates a ship hull shape on an empty grid using configurable profile families,
/// taper, asymmetry, and noise-based edge variation. Produces a Dungeon with RoomTiles
/// (interior) and RoomExteriorTiles (hull boundary) that downstream layers can use for
/// walls, wiring, and systems placement.
/// </summary>
public sealed partial class ShipHullDunGen : IDunGenLayer
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

    // --- New uniqueness parameters ---

    /// <summary>
    /// Hull profile family controlling the base silhouette shape.
    /// </summary>
    [DataField]
    public ShipHullProfile Profile = ShipHullProfile.Ellipse;

    /// <summary>
    /// Asymmetry bias: positive shifts the wider side to starboard (+X),
    /// negative to port (-X). 0 = symmetric. Range roughly -0.5 to 0.5.
    /// Only meaningful for Asymmetric profile but applies a subtle skew to all profiles.
    /// </summary>
    [DataField]
    public float Asymmetry;

    /// <summary>
    /// Corner radius for Box profile, in tiles. Higher = more rounded.
    /// Ignored for other profiles.
    /// </summary>
    [DataField]
    public int CornerRadius = 2;

    /// <summary>
    /// Width of a protruding engine block appended to the stern.
    /// 0 = no engine block. Adds structural variety to the aft section.
    /// </summary>
    [DataField]
    public int EngineBlockWidth;

    /// <summary>
    /// Length (in Y tiles) of the engine block protrusion.
    /// </summary>
    [DataField]
    public int EngineBlockLength;

    /// <summary>
    /// Optional side pod width (symmetric, one on each side). 0 = none.
    /// Creates bulge-like extensions on the port/starboard hull.
    /// </summary>
    [DataField]
    public int SidePodWidth;

    /// <summary>
    /// Length of side pods along the Y axis, centred on the midship.
    /// </summary>
    [DataField]
    public int SidePodLength;

    /// <summary>
    /// Secondary floor tile used for accent areas (engine block, pods).
    /// Falls back to FloorTile if not set.
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition>? AccentFloorTile;
}

using Content.Shared.Maps;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Procedural.DungeonLayers;

/// <summary>
/// Layout family controlling the interior compartmentalisation strategy.
/// </summary>
[Serializable, NetSerializable]
public enum ShipInteriorLayout : byte
{
    /// <summary>No interior partitioning - open hull (legacy behaviour).</summary>
    Open,
    /// <summary>Horizontal lane partitions dividing bow / mid / stern into rooms.</summary>
    Lanes,
    /// <summary>Central corridor with rooms on each side.</summary>
    Corridor,
    /// <summary>Ringed layout: outer corridor loop with a central room cluster.</summary>
    Ring,
}

/// <summary>
/// Procedurally partitions a ship hull interior into compartments (rooms connected by
/// doors) after <see cref="Content.Shared.Procedural.PostGeneration.BoundaryWallDunGen"/>
/// places the outer walls.  Runs before <see cref="ShipSystemsDunGen"/> so system placement
/// can target specific rooms.
/// </summary>
public sealed partial class ShipInteriorDunGen : IDunGenLayer
{
    /// <summary>
    /// Layout family to use. Determines partition geometry.
    /// </summary>
    [DataField]
    public ShipInteriorLayout Layout = ShipInteriorLayout.Lanes;

    /// <summary>
    /// Wall entity used for interior partition walls.
    /// </summary>
    [DataField]
    public EntProtoId PartitionWall = "WallShuttle";

    /// <summary>
    /// Airlock / door placed in partition doorways.
    /// </summary>
    [DataField]
    public EntProtoId PartitionDoor = "AirlockGlass";

    /// <summary>
    /// Minimum number of rooms to generate. Clamped to what the hull can fit.
    /// </summary>
    [DataField]
    public int MinRooms = 2;

    /// <summary>
    /// Maximum number of rooms to generate.
    /// </summary>
    [DataField]
    public int MaxRooms = 5;

    /// <summary>
    /// Width (in tiles) of corridors carved for Corridor and Ring layouts.
    /// </summary>
    [DataField]
    public int CorridorWidth = 1;

    /// <summary>
    /// Floor tile used in corridors (visual distinction from rooms).
    /// Null = keep existing floor tile.
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition>? CorridorFloorTile;

    /// <summary>
    /// Minimum tiles a room must contain or it's merged back into the corridor.
    /// </summary>
    [DataField]
    public int MinRoomSize = 4;
}

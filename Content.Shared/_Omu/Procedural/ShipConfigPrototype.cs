using Content.Shared.EntityTable;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonGenerators;
using Content.Shared.Procedural.DungeonLayers;
using Content.Shared.Procedural.PostGeneration;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._Omu.Procedural;

/// <summary>
/// Entry in <see cref="ShipConfigPrototype.Tables"/> that maps to an <see cref="EntityTableDunGen"/> layer.
/// </summary>
[DataDefinition]
public sealed partial class ShipEntityTable
{
    [DataField(required: true)]
    public ProtoId<EntityTablePrototype> Id;

    [DataField]
    public int Min = 1;

    [DataField]
    public int Max = 1;
}

/// <summary>
/// Compact, inheritable prototype that defines a procedural ship.
/// Flattens hull, boundary, interior, systems, anchors, and entity-table parameters
/// into direct fields with sensible defaults so YAML configs stay short.
/// Call <see cref="ToDungeonConfig"/> to produce the <see cref="DungeonConfig"/> the
/// existing dungeon pipeline expects.
/// </summary>
[Prototype("shipConfig")]
public sealed partial class ShipConfigPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<ShipConfigPrototype>))]
    public string[]? Parents { get; private set; }

    [AbstractDataField]
    public bool Abstract { get; private set; }

    // ── Hull ────────────────────────────────────────────────────────────

    [DataField] public int HalfLength = 12;
    [DataField] public int HalfWidth = 7;
    [DataField] public ShipHullProfile Profile = ShipHullProfile.Ellipse;
    [DataField] public float BowTaper = 0.45f;
    [DataField] public float SternTaper = 0.2f;
    [DataField] public float EdgeNoiseAmplitude = 1.0f;
    [DataField] public ProtoId<ContentTileDefinition> FloorTile = "FloorSteel";
    [DataField] public ProtoId<ContentTileDefinition> HullTile = "Plating";
    [DataField] public ProtoId<ContentTileDefinition>? AccentFloorTile;

    // Profile-specific
    [DataField] public float Asymmetry;
    [DataField] public int CornerRadius = 2;
    [DataField] public int EngineBlockWidth;
    [DataField] public int EngineBlockLength;
    [DataField] public int SidePodWidth;
    [DataField] public int SidePodLength;

    // ── Boundary ────────────────────────────────────────────────────────

    [DataField] public EntProtoId Wall = "WallShuttle";
    [DataField] public EntProtoId? BowWall = "ShuttleWindow";

    // ── Interior (optional -- null means open hull) ──────────────────────

    [DataField] public ShipInteriorLayout? InteriorLayout;
    [DataField] public EntProtoId? PartitionWall;   // defaults to Wall at runtime
    [DataField] public EntProtoId PartitionDoor = "AirlockGlass";
    [DataField] public int MinRooms = 2;
    [DataField] public int MaxRooms = 5;
    [DataField] public int CorridorWidth = 1;
    [DataField] public ProtoId<ContentTileDefinition>? CorridorFloorTile;

    // ── Systems ─────────────────────────────────────────────────────────

    [DataField] public int ThrustersPerDirection = 4;
    [DataField] public int VentCount = 4;
    [DataField] public int ScrubberCount = 4;
    [DataField] public bool EnableBulkheads;
    [DataField] public EntProtoId? Generator;           // null → ShipSystemsDunGen default (PACMAN)
    [DataField] public EntProtoId? GeneratorShielding;
    [DataField] public EntProtoId? GeneratorFuel;

    // ── Anchors ─────────────────────────────────────────────────────────

    [DataField] public List<EntProtoId> BowAnchors = new();
    [DataField] public List<EntProtoId> PortBowAnchors = new();
    [DataField] public List<EntProtoId> StarboardBowAnchors = new();
    [DataField] public List<EntProtoId> MidAnchors = new();
    [DataField] public List<EntProtoId> PortSternAnchors = new();
    [DataField] public List<EntProtoId> StarboardSternAnchors = new();
    [DataField] public List<EntProtoId> SternAnchors = new();

    // ── Entity tables ───────────────────────────────────────────────────

    [DataField] public List<ShipEntityTable> Tables = new();

    // ── Scrap overlay (optional) ────────────────────────────────────────

    [DataField] public ScrapShipDunGen? Scrap;

    // ── Conversion ──────────────────────────────────────────────────────

    public DungeonConfig ToDungeonConfig()
    {
        var config = new DungeonConfig();

        // 1 -- Hull
        config.Layers.Add(new ShipHullDunGen
        {
            HalfLength = HalfLength,
            HalfWidth = HalfWidth,
            Profile = Profile,
            BowTaper = BowTaper,
            SternTaper = SternTaper,
            EdgeNoiseAmplitude = EdgeNoiseAmplitude,
            FloorTile = FloorTile,
            HullTile = HullTile,
            AccentFloorTile = AccentFloorTile,
            Asymmetry = Asymmetry,
            CornerRadius = CornerRadius,
            EngineBlockWidth = EngineBlockWidth,
            EngineBlockLength = EngineBlockLength,
            SidePodWidth = SidePodWidth,
            SidePodLength = SidePodLength,
        });

        // 2 -- Boundary walls
        config.Layers.Add(new BoundaryWallDunGen
        {
            Wall = Wall,
            Tile = FloorTile,
            BowWall = BowWall,
        });

        // 3 -- Interior partitions (optional)
        if (InteriorLayout is { } layout)
        {
            config.Layers.Add(new ShipInteriorDunGen
            {
                Layout = layout,
                PartitionWall = PartitionWall ?? Wall,
                PartitionDoor = PartitionDoor,
                MinRooms = MinRooms,
                MaxRooms = MaxRooms,
                CorridorWidth = CorridorWidth,
                CorridorFloorTile = CorridorFloorTile,
            });
        }

        // 4 -- Ship systems (thrusters, power, atmo, etc.)
        var systems = new ShipSystemsDunGen
        {
            ThrustersPerDirection = ThrustersPerDirection,
            VentCount = VentCount,
            ScrubberCount = ScrubberCount,
            EnableBulkheads = EnableBulkheads,
        };

        if (Generator is { } gen)
            systems.Generator = gen;

        systems.GeneratorShielding = GeneratorShielding;
        systems.GeneratorFuel = GeneratorFuel;
        config.Layers.Add(systems);

        // 5 -- Department anchors
        config.Layers.Add(new DepartmentAnchorsDunGen
        {
            BowAnchors = BowAnchors,
            PortBowAnchors = PortBowAnchors,
            StarboardBowAnchors = StarboardBowAnchors,
            MidAnchors = MidAnchors,
            PortSternAnchors = PortSternAnchors,
            StarboardSternAnchors = StarboardSternAnchors,
            SternAnchors = SternAnchors,
        });

        // 6 -- Random entity tables
        foreach (var table in Tables)
        {
            config.Layers.Add(new EntityTableDunGen
            {
                Table = new NestedSelector { TableId = table.Id },
                MinCount = table.Min,
                MaxCount = table.Max,
            });
        }

        // 7 -- Scrap overlay (optional)
        if (Scrap != null)
            config.Layers.Add(Scrap);

        return config;
    }
}

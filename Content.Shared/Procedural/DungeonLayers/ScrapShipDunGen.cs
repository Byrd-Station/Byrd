using Content.Shared.Maps;
using Robust.Shared.Noise;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.DungeonLayers;

/// <summary>
/// A dungeon generation layer that "scraps" an existing ship grid by procedurally
/// degrading tiles, creating hull breaches, removing walls, and deleting entities.
/// Operates on all tiles of the grid (not just dungeon-generated tiles).
/// </summary>
public sealed partial class ScrapShipDunGen : IDunGenLayer
{
    /// <summary>
    /// Noise function used to determine which areas are affected.
    /// Areas where noise exceeds thresholds get scrapped.
    /// </summary>
    [DataField(required: true)]
    public FastNoiseLite Noise = default!;

    /// <summary>
    /// Noise threshold above which floor tiles are replaced with damaged variants.
    /// Lower values = more damage. Set to 1.0 to disable.
    /// </summary>
    [DataField]
    public float TileDamageThreshold = -0.2f;

    /// <summary>
    /// Tile to replace damaged floor tiles with.
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition> DamagedTile = "PlatingDamaged";

    /// <summary>
    /// Noise threshold above which tiles are completely removed (hull breach).
    /// Should be higher than TileDamageThreshold. Set to 1.0 to disable.
    /// </summary>
    [DataField]
    public float HullBreachThreshold = 0.6f;

    /// <summary>
    /// When a hull breach occurs, replace with this tile instead of space.
    /// If null, the tile is deleted entirely (becomes space).
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition>? BreachTile;

    /// <summary>
    /// Noise threshold above which wall entities are removed.
    /// Set to 1.0 to disable.
    /// </summary>
    [DataField]
    public float WallRemovalThreshold = 0.5f;

    /// <summary>
    /// If set, replace removed walls with this entity (e.g. Girder).
    /// </summary>
    [DataField]
    public EntProtoId? WallReplacement;

    /// <summary>
    /// Probability that a non-wall anchored entity on a damaged tile is removed.
    /// </summary>
    [DataField]
    public float EntityRemovalChance = 0.6f;

    /// <summary>
    /// Noise threshold above which non-wall entities get removal rolls.
    /// Set to 1.0 to disable.
    /// </summary>
    [DataField]
    public float EntityRemovalThreshold = -0.1f;

    /// <summary>
    /// Tiles that should not be degraded (e.g., already-space tiles).
    /// </summary>
    [DataField]
    public HashSet<ProtoId<ContentTileDefinition>>? ProtectedTiles;

    [DataField]
    public float BloodChance = 0.18f;

    [DataField]
    public float RustChance = 0.22f;

    [DataField]
    public float DirtChance = 0.30f;

    [DataField]
    public float BurnMarkChance = 0.20f;

    [DataField]
    public float TrashChance = 0.16f;

    [DataField]
    public float EmptyMagazineChance = 0.12f;

    [DataField]
    public float SalvageLootChance = 0.10f;

    [DataField]
    public float LandMineChance = 0.05f;

    [DataField]
    public float SmugglerSatchelChance = 0.03f;

    [DataField]
    public float ScatteredToolChance = 0.08f;

    [DataField]
    public float CobwebChance = 0.10f;

    [DataField]
    public float ScrapDebrisChance = 0.14f;

    [DataField]
    public float GraffitiChance = 0.08f;

    [DataField]
    public float PuddleChance = 0.06f;

    [DataField]
    public int MinCorpseCount = 2;

    [DataField]
    public int MaxCorpseCount = 8;

    [DataField]
    public float LightBreakChance = 0.50f;

    [DataField]
    public float LightFlickerChance = 0.25f;

    [DataField]
    public float LightDisableChance = 0.20f;

    [DataField]
    public float HiddenStashChance = 0.55f;

    /// <summary>
    /// Replaced by MinTurretCount/MaxTurretCount -- kept for YAML compat.
    /// </summary>
    [DataField]
    public float HostileTurretChance = 0.6f;

    [DataField]
    public int MinTurretCount = 1;

    [DataField]
    public int MaxTurretCount = 3;

    [DataField]
    public int MinStashCount = 1;

    [DataField]
    public int MaxStashCount = 3;

    /// <summary>
    /// Total mob count for the single chosen faction.
    /// </summary>
    [DataField]
    public int MinMobCount = 8;

    [DataField]
    public int MaxMobCount = 18;

    [DataField]
    public float BossMobChance = 0.15f;

    [DataField]
    public int MinExplosionCount = 2;

    [DataField]
    public int MaxExplosionCount = 5;

    [DataField]
    public int MinImpactCount = 2;

    [DataField]
    public int MaxImpactCount = 6;

    [DataField]
    public int MinFireCount = 2;

    [DataField]
    public int MaxFireCount = 5;

    [DataField]
    public int MinFloorTrapCount = 2;

    [DataField]
    public int MaxFloorTrapCount = 8;

    [DataField]
    public int MinAnomalyCount = 0;

    [DataField]
    public int MaxAnomalyCount = 2;

    [DataField]
    public float KudzuChance = 0.06f;

    [DataField]
    public float InfestationChance = 0.40f;

    [DataField]
    public int MinInfestationCount = 8;

    [DataField]
    public int MaxInfestationCount = 20;

    [DataField]
    public string ExplosionType = "Default";

    [DataField]
    public float ExplosionTotalIntensity = 120f;

    [DataField]
    public float ExplosionSlope = 2.5f;

    [DataField]
    public float ExplosionMaxTileIntensity = 14f;

    [DataField]
    public float ImpactTotalIntensity = 160f;

    [DataField]
    public float ImpactSlope = 2.5f;

    [DataField]
    public float ImpactMaxTileIntensity = 16f;

    [DataField]
    public float FireTemperature = 2200f;

    [DataField]
    public float FireVolume = 600f;

    [DataField]
    public float SecretDoorTrapTotalIntensity = 95f;

    [DataField]
    public float SecretDoorTrapSlope = 3f;

    [DataField]
    public float SecretDoorTrapMaxTileIntensity = 12f;
}

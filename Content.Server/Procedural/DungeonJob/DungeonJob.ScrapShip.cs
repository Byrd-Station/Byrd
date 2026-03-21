using System.Linq;
using System.Threading.Tasks;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Light.Components;
using Content.Server.Light.EntitySystems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Procedural.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Doors;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonLayers;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    private static readonly ProtoId<TagPrototype> ScrapWallTag = "Wall";
    private static readonly ProtoId<TagPrototype> ScrapWindowTag = "Window";
    private static readonly string[] ScrapDecalsRust = ["Rust"];
    private static readonly string[] ScrapDecalsDirt = ["Dirt", "DirtLight", "DirtMedium", "DirtHeavy"];
    private static readonly string[] ScrapDecalsBurn = ["burnt1", "burnt2", "burnt3", "burnt4"];
    private static readonly EntProtoId[] ScrapClutterPrototypes =
    [
        "Girder",
        "CableApcStack1",
        "CableHVStack1",
        "CableMVStack1",
        "TrashBag",
        "LightTubeBroken",
        "LightBulbBroken",
        "ShardGlass",
        "ShardGlassReinforced",
        "PartRodMetal1",
        "Mousetrap",
        "MousetrapArmed",
        "ScrapSteel",
        "ScrapGlass",
        "TrashBananaPeel",
    ];
    private static readonly EntProtoId[] ScrapMagazinePrototypes =
    [
        "MagazinePistolEmpty",
        "MagazineRifleEmpty",
        "MagazineLightRifleEmpty",
        "MagazineShotgunEmpty",
        "SpeedLoaderMagnumEmpty",
        "SpeedLoaderRevolverLightRifleEmpty",
        "MagazinePistolSubMachineGunEmpty",
        "MagazineBoxPistolEmpty",
    ];
    private static readonly EntProtoId[] ScrapLootPrototypes =
    [
        "SalvageLootSpawner",
        "SalvageSpawnerScrapValuable",
        "SalvageSpawnerTreasureValuable",
        "RandomSatchelSpawner",
        "SalvageLootSpawner",
        "SalvageSpawnerScrapValuable",
    ];
    private static readonly EntProtoId[] ScrapSpaceMobPrototypes =
    [
        "MobCarpDungeon",
        "MobCarpDungeon",
        "MobSpiderSpaceSalvage",
        "MobCobraSpaceSalvage",
        "MobSharkSalvage",
        "MobBearSpaceSalvage",
        "MobKangarooSpaceSalvage",
        "MobTickSalvage",
        "MobGiantSpiderAngry",
    ];
    private static readonly EntProtoId[] ScrapFleshMobPrototypes =
    [
        "MobFleshJaredSalvage",
        "MobFleshGolemSalvage",
        "MobFleshClampSalvage",
        "MobFleshLoverSalvage",
        "MobAbomination",
    ];
    private static readonly EntProtoId[] ScrapXenoMobPrototypes =
    [
        "MobXenomorphDroneDungeon",
        "MobXenomorphDroneDungeon",
        "MobXenomorphHunterDungeon",
        "MobXenomorphSentinelDungeon",
        "MobXenomorphLarvaDungeon",
    ];
    private static readonly EntProtoId[] ScrapHivebotPrototypes =
    [
        "MobHivebot",
        "MobHivebot",
        "MobHivebotRanged",
        "MobHivebotStrong",
    ];
    private static readonly EntProtoId[] ScrapWraithPrototypes =
    [
        "MobSkeletonGoon",
        "MobSkeletonGoon",
        "MobSkeletonGoonLesser",
        "MobSkeletonCommander",
        "MobSkeletonPirate",
    ];

    // Boss mobs per faction -- same faction as the regular mobs.
    private static readonly EntProtoId[] ScrapSpaceBossPrototypes = ["MobCarpDragon", "MobSharkSalvage"];
    private static readonly EntProtoId[] ScrapFleshBossPrototypes = ["MobAbomination"];
    private static readonly EntProtoId[] ScrapXenoBossPrototypes = ["MobXenomorphPraetorianDungeon"];
    private static readonly EntProtoId[] ScrapHivebotBossPrototypes = ["MobHivebotStrong"];
    private static readonly EntProtoId[] ScrapWraithBossPrototypes = ["MobSkeletonCommander"];

    // Infestation dressing per faction.
    private static readonly EntProtoId ScrapSpaceInfestationDressing = "SpiderWeb";
    private static readonly EntProtoId ScrapFleshInfestationDressing = "FleshKudzu";
    private static readonly EntProtoId ScrapXenoInfestationDressing = "SpiderWeb";
    private static readonly EntProtoId ScrapHivebotInfestationDressing = "ScrapSteel";
    private static readonly EntProtoId ScrapWraithInfestationDressing = "Cobweb1";

    /// <summary>
    /// Index into this enum to get the mob pool, boss pool, and infestation dressing
    /// for a given faction. One faction is picked per ship.
    /// </summary>
    private enum ScrapFaction
    {
        SpaceFauna,
        Flesh,
        Xeno,
        Hivebot,
        Wraith,
    }

    private static readonly ScrapFaction[] AllScrapFactions = Enum.GetValues<ScrapFaction>();

    private static EntProtoId[] GetFactionMobs(ScrapFaction faction) => faction switch
    {
        ScrapFaction.SpaceFauna => ScrapSpaceMobPrototypes,
        ScrapFaction.Flesh => ScrapFleshMobPrototypes,
        ScrapFaction.Xeno => ScrapXenoMobPrototypes,
        ScrapFaction.Hivebot => ScrapHivebotPrototypes,
        ScrapFaction.Wraith => ScrapWraithPrototypes,
        _ => ScrapSpaceMobPrototypes,
    };

    private static EntProtoId[] GetFactionBosses(ScrapFaction faction) => faction switch
    {
        ScrapFaction.SpaceFauna => ScrapSpaceBossPrototypes,
        ScrapFaction.Flesh => ScrapFleshBossPrototypes,
        ScrapFaction.Xeno => ScrapXenoBossPrototypes,
        ScrapFaction.Hivebot => ScrapHivebotBossPrototypes,
        ScrapFaction.Wraith => ScrapWraithBossPrototypes,
        _ => ScrapSpaceBossPrototypes,
    };

    private static EntProtoId GetFactionInfestationDressing(ScrapFaction faction) => faction switch
    {
        ScrapFaction.SpaceFauna => ScrapSpaceInfestationDressing,
        ScrapFaction.Flesh => ScrapFleshInfestationDressing,
        ScrapFaction.Xeno => ScrapXenoInfestationDressing,
        ScrapFaction.Hivebot => ScrapHivebotInfestationDressing,
        ScrapFaction.Wraith => ScrapWraithInfestationDressing,
        _ => ScrapSpaceInfestationDressing,
    };
    private static readonly EntProtoId[] ScrapValuablePrototypes =
    [
        "BriefcaseSmugglerCash",
        "ClothingBackpackSatchelSmugglerFilled",
        "MaterialDiamond1",
        "IngotGold1",
        "SpaceCash10000",
        "SpaceCash10000",
        "MaterialDiamond1",
    ];
    private static readonly EntProtoId[] ScrapCorpsePrototypes =
    [
        "SalvageHumanCorpseSpawner",
        "SalvageHumanCorpseSpawner",
        "MobSkeletonPerson",
        "MobSkeletonPerson",
        "RandomEngineerCorpseSpawner",
        "RandomCargoCorpseSpawner",
        "RandomSecurityCorpseSpawner",
        "RandomEngineerCorpseSpawner",
    ];
    private static readonly EntProtoId[] ScrapToolPrototypes =
    [
        "Wrench",
        "Crowbar",
        "Screwdriver",
        "Wirecutter",
        "Multitool",
        "Welder",
        "FlashlightLantern",
    ];
    private static readonly EntProtoId[] ScrapDebrisPrototypes =
    [
        "ScrapSteel",
        "ScrapSteel",
        "ScrapGlass",
        "ScrapGlass",
        "ShardGlass",
        "ShardGlassReinforced",
        "PartRodMetal1",
        "PartRodMetal1",
        "ScrapCamera",
        "ScrapFireExtinguisher",
        "ScrapTube",
        "ScrapIntercom",
        "Girder",
    ];
    private static readonly EntProtoId[] ScrapCobwebPrototypes =
    [
        "SpiderWeb",
        "SpiderWeb",
        "Cobweb1",
        "Cobweb2",
    ];
    private static readonly EntProtoId[] ScrapTurretPrototypes =
    [
        "WeaponTurretXeno",
        "WeaponTurretHostile",
        "WeaponTurretAllHostile",
        "WeaponTurretSyndicate",
        "WeaponTurretSyndicateBroken",
    ];
    private static readonly EntProtoId[] ScrapPuddlePrototypes =
    [
        "PuddleSpaceLube",
        "PuddleSpaceLube",
        "PuddleBlood",
        "PuddleVomit",
    ];
    private static readonly EntProtoId[] ScrapAnomalyPrototypes =
    [
        "AnomalyBluespace",
        "AnomalyElectricity",
        "AnomalyFlesh",
        "AnomalyGravity",
        "AnomalyIce",
        "AnomalyPyroclastic",
        "AnomalyShadow",
        "AnomalyTech",
        "AnomalyFlora",
    ];
    private static readonly EntProtoId[] ScrapFloorTrapPrototypes =
    [
        "FloorTrapExplosion",
        "FloorTrapEMP",
        "LandMineExplosive",
        "LandMineExplosive",
        "Claymore",
    ];
    private static readonly EntProtoId[] ScrapKudzuPrototypes =
    [
        "Kudzu",
        "WeakKudzu",
        "FleshKudzu",
        "ShadowKudzuWeak",
    ];
    private static readonly string[] ScrapDecalsGraffiti =
    [
        "skull",
        "splatter",
        "danger",
        "firedanger",
        "electricdanger",
        "biohazard",
        "radiation",
        "splatter",
        "skull",
    ];

    /// <summary>
    /// <see cref="ScrapShipDunGen"/>
    /// </summary>
    private async Task PostGen(ScrapShipDunGen gen, Random random)
    {
        // Collect all non-space tiles on the grid.
        var allTiles = new List<(Vector2i Index, Tile Tile)>();
        var tileEnumerator = _maps.GetAllTilesEnumerator(_gridUid, _grid);

        while (tileEnumerator.MoveNext(out var tileRef))
        {
            var tile = tileRef.Value;
            if (tile.Tile.IsEmpty)
                continue;

            var tileDef = (ContentTileDefinition) _tileDefManager[tile.Tile.TypeId];

            if (gen.ProtectedTiles != null && gen.ProtectedTiles.Contains(tileDef.ID))
                continue;

            allTiles.Add((tile.GridIndices, tile.Tile));
        }

        var replacements = new List<(Vector2i Index, Tile Tile)>();
        var entitiesToDelete = new List<EntityUid>();
        var entitiesToSpawn = new List<(EntProtoId Proto, EntityCoordinates Coords)>();
        var breachTiles = new HashSet<Vector2i>();
        var walkableTiles = new HashSet<Vector2i>();
        var damagedTiles = new HashSet<Vector2i>();
        var wallTiles = new HashSet<Vector2i>();

        foreach (var (index, tile) in allTiles)
        {
            var noiseVal = gen.Noise.GetNoise(index.X, index.Y);
            var anchoredEntities = new List<EntityUid>();
            var wallEntities = new List<EntityUid>();
            var nonWallEntities = new List<EntityUid>();

            var anchored = _maps.GetAnchoredEntitiesEnumerator(_gridUid, _grid, index);
            while (anchored.MoveNext(out var uid))
            {
                anchoredEntities.Add(uid.Value);

                if (_tags.HasTag(uid.Value, ScrapWallTag) || _tags.HasTag(uid.Value, ScrapWindowTag))
                {
                    wallEntities.Add(uid.Value);
                    continue;
                }

                nonWallEntities.Add(uid.Value);
            }

            // Hull breach: remove tile entirely.
            if (noiseVal >= gen.HullBreachThreshold)
            {
                breachTiles.Add(index);

                foreach (var uid in anchoredEntities)
                {
                    entitiesToDelete.Add(uid);
                }

                if (gen.BreachTile != null)
                {
                    var breachTileDef = _prototype.Index(gen.BreachTile.Value);
                    replacements.Add((index, new Tile(breachTileDef.TileId)));
                }
                else
                {
                    replacements.Add((index, Tile.Empty));
                }

                continue;
            }

            walkableTiles.Add(index);

            // Wall removal: delete walls and optionally replace with girders.
            if (noiseVal >= gen.WallRemovalThreshold)
            {
                foreach (var uid in wallEntities)
                {
                    entitiesToDelete.Add(uid);

                    if (gen.WallReplacement != null)
                    {
                        entitiesToSpawn.Add((gen.WallReplacement.Value, _maps.GridTileToLocal(_gridUid, _grid, index)));
                    }
                }
            }
            else if (wallEntities.Count > 0)
            {
                wallTiles.Add(index);
            }

            // Entity removal: randomly delete non-wall anchored entities.
            if (noiseVal >= gen.EntityRemovalThreshold)
            {
                foreach (var uid in nonWallEntities)
                {
                    if (random.Prob(gen.EntityRemovalChance))
                        entitiesToDelete.Add(uid);
                }
            }

            // Tile degradation: replace floor with damaged variant.
            if (noiseVal >= gen.TileDamageThreshold)
            {
                var damagedTileDef = _prototype.Index(gen.DamagedTile);
                replacements.Add((index, new Tile(damagedTileDef.TileId)));
                damagedTiles.Add(index);
            }

            await SuspendDungeon();

            if (!ValidateResume())
                return;
        }

        // Apply all tile changes.
        _maps.SetTiles(_gridUid, _grid, replacements);

        // Delete marked entities.
        foreach (var uid in entitiesToDelete)
        {
            if (!_entManager.Deleted(uid))
                _entManager.DeleteEntity(uid);
        }

        // Spawn replacement entities (e.g. girders where walls were).
        foreach (var (proto, coords) in entitiesToSpawn)
        {
            _entManager.SpawnEntity(proto, coords);
        }

        var spawnTiles = walkableTiles.Where(tile => !breachTiles.Contains(tile)).ToList();
        var damagedSpawnTiles = damagedTiles.Where(tile => !breachTiles.Contains(tile)).ToList();
        var spawnTileSet = spawnTiles.ToHashSet();

        ScatterScrapDetail(gen, damagedSpawnTiles, random);
        ScatterScrapProps(gen, spawnTiles, random);
        ScatterScrapDebris(gen, damagedSpawnTiles, random);
        ScatterScrapPuddles(gen, spawnTiles, random);
        ApplyScrapLightDamage(gen, random);
        SpawnScrapMobs(gen, spawnTiles, damagedSpawnTiles, random);
        SpawnScrapCorpses(gen, spawnTiles, random);
        SpawnScrapExplosions(gen, damagedSpawnTiles, random);
        SpawnScrapImpacts(gen, spawnTiles, random);
        SpawnScrapFires(gen, damagedSpawnTiles, random);
        SpawnScrapFloorTraps(gen, spawnTiles, random);
        SpawnScrapAnomalies(gen, spawnTiles, random);
        SpawnScrapKudzu(gen, damagedSpawnTiles, random);
        SpawnScrapTurrets(gen, spawnTiles, random);
        SpawnScrapSecretStashes(gen, wallTiles, spawnTileSet, random);
    }

    private void ScatterScrapDetail(ScrapShipDunGen gen, List<Vector2i> damagedTiles, Random random)
    {
        foreach (var tile in damagedTiles)
        {
            var coords = _maps.GridTileToLocal(_gridUid, _grid, tile);

            if (random.Prob(gen.BloodChance))
                SpawnScrapEntity(Pick(ScrapBloodPuddlePrototypes, random), coords);

            if (random.Prob(gen.RustChance))
                AddScrapDecal(Pick(ScrapDecalsRust, random), coords);

            if (random.Prob(gen.DirtChance))
                AddScrapDecal(Pick(ScrapDecalsDirt, random), coords);

            if (random.Prob(gen.BurnMarkChance))
                AddScrapDecal(Pick(ScrapDecalsBurn, random), coords);

            if (random.Prob(gen.GraffitiChance))
                AddScrapDecal(Pick(ScrapDecalsGraffiti, random), coords);
        }
    }

    private void ScatterScrapProps(ScrapShipDunGen gen, List<Vector2i> spawnTiles, Random random)
    {
        foreach (var tile in spawnTiles)
        {
            var coords = _maps.GridTileToLocal(_gridUid, _grid, tile);

            if (random.Prob(gen.TrashChance))
                SpawnScrapEntity(Pick(ScrapClutterPrototypes, random), coords);

            if (random.Prob(gen.EmptyMagazineChance))
                SpawnScrapEntity(Pick(ScrapMagazinePrototypes, random), coords);

            if (random.Prob(gen.SalvageLootChance))
                SpawnScrapEntity(Pick(ScrapLootPrototypes, random), coords);

            if (random.Prob(gen.LandMineChance) && IsMachineTileFree(tile))
                SpawnScrapEntity("LandMineExplosive", coords);

            if (random.Prob(gen.SmugglerSatchelChance))
                SpawnScrapEntity("ClothingBackpackSatchelSmugglerFilled", coords);

            if (random.Prob(gen.ScatteredToolChance))
                SpawnScrapEntity(Pick(ScrapToolPrototypes, random), coords);

            if (random.Prob(gen.CobwebChance))
                SpawnScrapEntity(Pick(ScrapCobwebPrototypes, random), coords);
        }
    }

    private void ScatterScrapDebris(ScrapShipDunGen gen, List<Vector2i> damagedTiles, Random random)
    {
        foreach (var tile in damagedTiles)
        {
            if (!random.Prob(gen.ScrapDebrisChance))
                continue;

            var coords = _maps.GridTileToLocal(_gridUid, _grid, tile);
            SpawnScrapEntity(Pick(ScrapDebrisPrototypes, random), coords);
        }
    }

    private void SpawnScrapCorpses(ScrapShipDunGen gen, List<Vector2i> spawnTiles, Random random)
    {
        var availableTiles = new List<Vector2i>(spawnTiles);
        var count = random.Next(gen.MinCorpseCount, gen.MaxCorpseCount + 1);

        for (var i = 0; i < count && availableTiles.Count > 0; i++)
        {
            if (!TryTakeSpawnTile(availableTiles, random, out var tile))
                break;

            var coords = _maps.GridTileToLocal(_gridUid, _grid, tile);
            SpawnScrapEntity(Pick(ScrapCorpsePrototypes, random), coords);
            // Blood near corpses.
            SpawnScrapEntity(Pick(ScrapBloodPuddlePrototypes, random), coords);
        }
    }

    private void ApplyScrapLightDamage(ScrapShipDunGen gen, Random random)
    {
        var lights = new HashSet<Entity<PoweredLightComponent>>();
        _lookup.GetChildEntities(_gridUid, lights);

        var lightSystem = _entManager.System<PoweredLightSystem>();

        foreach (var light in lights)
        {
            if (_entManager.Deleted(light.Owner))
                continue;

            if (random.Prob(gen.LightBreakChance))
            {
                lightSystem.TryDestroyBulb(light.Owner, light.Comp);
                continue;
            }

            if (random.Prob(gen.LightFlickerChance))
                lightSystem.ToggleBlinkingLight(light.Owner, light.Comp, true);

            if (random.Prob(gen.LightDisableChance))
                lightSystem.SetState(light.Owner, false, light.Comp);
        }
    }

    private void SpawnScrapMobs(ScrapShipDunGen gen, List<Vector2i> spawnTiles, List<Vector2i> damagedTiles, Random random)
    {
        // Pick ONE faction for this entire ship -- no cross-faction infighting.
        var faction = Pick(AllScrapFactions, random);
        var mobPool = GetFactionMobs(faction);
        var bossPool = GetFactionBosses(faction);

        var availableTiles = new List<Vector2i>(spawnTiles);
        var totalMobCount = random.Next(gen.MinMobCount, gen.MaxMobCount + 1);

        for (var i = 0; i < totalMobCount && availableTiles.Count > 0; i++)
        {
            if (!TryTakeSpawnTile(availableTiles, random, out var tile))
                break;

            SpawnScrapEntity(Pick(mobPool, random), _maps.GridTileToLocal(_gridUid, _grid, tile));
        }

        // Boss mob: rare but devastating -- same faction.
        if (random.Prob(gen.BossMobChance) && availableTiles.Count > 0)
        {
            if (TryTakeSpawnTile(availableTiles, random, out var bossTile))
            {
                SpawnScrapEntity(Pick(bossPool, random), _maps.GridTileToLocal(_gridUid, _grid, bossTile));
            }
        }

        // Infestation: clustered dressing + extra mobs from the same faction.
        if (random.Prob(gen.InfestationChance))
        {
            var infestDressing = GetFactionInfestationDressing(faction);
            var infestTiles = new List<Vector2i>(damagedTiles);
            var infestCount = random.Next(gen.MinInfestationCount, gen.MaxInfestationCount + 1);

            for (var i = 0; i < infestCount && infestTiles.Count > 0; i++)
            {
                if (!TryTakeSpawnTile(infestTiles, random, out var tile))
                    break;

                var coords = _maps.GridTileToLocal(_gridUid, _grid, tile);
                SpawnScrapEntity(infestDressing, coords);

                if (random.Prob(0.3f))
                    SpawnScrapEntity(Pick(mobPool, random), coords);
            }
        }
    }

    private void SpawnScrapExplosions(ScrapShipDunGen gen, List<Vector2i> damagedTiles, Random random)
    {
        if (damagedTiles.Count == 0)
            return;

        var availableTiles = new List<Vector2i>(damagedTiles);
        var explosionSystem = _entManager.System<ExplosionSystem>();
        var count = random.Next(gen.MinExplosionCount, gen.MaxExplosionCount + 1);

        for (var i = 0; i < count && availableTiles.Count > 0; i++)
        {
            if (!TryTakeTile(availableTiles, random, out var tile))
                break;

            explosionSystem.QueueExplosion(
                _transform.ToMapCoordinates(_maps.GridTileToLocal(_gridUid, _grid, tile)),
                gen.ExplosionType,
                gen.ExplosionTotalIntensity,
                gen.ExplosionSlope,
                gen.ExplosionMaxTileIntensity,
                cause: null,
                canCreateVacuum: false);
        }
    }

    private void SpawnScrapImpacts(ScrapShipDunGen gen, List<Vector2i> spawnTiles, Random random)
    {
        if (spawnTiles.Count == 0)
            return;

        var availableTiles = new List<Vector2i>(spawnTiles);
        var explosionSystem = _entManager.System<ExplosionSystem>();
        var count = random.Next(gen.MinImpactCount, gen.MaxImpactCount + 1);

        for (var i = 0; i < count && availableTiles.Count > 0; i++)
        {
            if (!TryTakeTile(availableTiles, random, out var tile))
                break;

            var coords = _maps.GridTileToLocal(_gridUid, _grid, tile);
            explosionSystem.QueueExplosion(
                _transform.ToMapCoordinates(coords),
                gen.ExplosionType,
                gen.ImpactTotalIntensity,
                gen.ImpactSlope,
                gen.ImpactMaxTileIntensity,
                cause: null,
                canCreateVacuum: true);
            AddScrapDecal(Pick(ScrapDecalsBurn, random), coords);
            SpawnScrapEntity(Pick(ScrapLootPrototypes, random), coords);
        }
    }

    private void SpawnScrapFires(ScrapShipDunGen gen, List<Vector2i> damagedTiles, Random random)
    {
        if (damagedTiles.Count == 0)
            return;

        var atmosphere = _entManager.System<AtmosphereSystem>();
        var gridAtmosphere = (_gridUid, _entManager.GetComponentOrNull<GridAtmosphereComponent>(_gridUid));
        var availableTiles = new List<Vector2i>(damagedTiles);
        var count = random.Next(gen.MinFireCount, gen.MaxFireCount + 1);

        for (var i = 0; i < count && availableTiles.Count > 0; i++)
        {
            if (!TryTakeTile(availableTiles, random, out var tile))
                break;

            atmosphere.HotspotExpose(gridAtmosphere, tile, gen.FireTemperature, gen.FireVolume);
            AddScrapDecal(Pick(ScrapDecalsBurn, random), _maps.GridTileToLocal(_gridUid, _grid, tile));
        }
    }

    private void ScatterScrapPuddles(ScrapShipDunGen gen, List<Vector2i> spawnTiles, Random random)
    {
        foreach (var tile in spawnTiles)
        {
            if (!random.Prob(gen.PuddleChance))
                continue;

            var coords = _maps.GridTileToLocal(_gridUid, _grid, tile);
            SpawnScrapEntity(Pick(ScrapPuddlePrototypes, random), coords);
        }
    }

    private void SpawnScrapFloorTraps(ScrapShipDunGen gen, List<Vector2i> spawnTiles, Random random)
    {
        var availableTiles = new List<Vector2i>(spawnTiles);
        var count = random.Next(gen.MinFloorTrapCount, gen.MaxFloorTrapCount + 1);

        for (var i = 0; i < count && availableTiles.Count > 0; i++)
        {
            if (!TryTakeSpawnTile(availableTiles, random, out var tile))
                break;

            SpawnScrapEntity(Pick(ScrapFloorTrapPrototypes, random), _maps.GridTileToLocal(_gridUid, _grid, tile));
        }
    }

    private void SpawnScrapAnomalies(ScrapShipDunGen gen, List<Vector2i> spawnTiles, Random random)
    {
        var availableTiles = new List<Vector2i>(spawnTiles);
        var count = random.Next(gen.MinAnomalyCount, gen.MaxAnomalyCount + 1);

        for (var i = 0; i < count && availableTiles.Count > 0; i++)
        {
            if (!TryTakeSpawnTile(availableTiles, random, out var tile))
                break;

            SpawnScrapEntity(Pick(ScrapAnomalyPrototypes, random), _maps.GridTileToLocal(_gridUid, _grid, tile));
        }
    }

    private void SpawnScrapKudzu(ScrapShipDunGen gen, List<Vector2i> damagedTiles, Random random)
    {
        foreach (var tile in damagedTiles)
        {
            if (!random.Prob(gen.KudzuChance))
                continue;

            var coords = _maps.GridTileToLocal(_gridUid, _grid, tile);
            SpawnScrapEntity(Pick(ScrapKudzuPrototypes, random), coords);
        }
    }

    private void SpawnScrapTurrets(ScrapShipDunGen gen, List<Vector2i> spawnTiles, Random random)
    {
        var availableTiles = new List<Vector2i>(spawnTiles);
        var count = random.Next(gen.MinTurretCount, gen.MaxTurretCount + 1);

        for (var i = 0; i < count && availableTiles.Count > 0; i++)
        {
            if (!TryTakeSpawnTile(availableTiles, random, out var tile))
                break;

            if (!IsMachineTileFree(tile))
                continue;

            SpawnScrapEntity(Pick(ScrapTurretPrototypes, random), _maps.GridTileToLocal(_gridUid, _grid, tile));
        }
    }

    private void SpawnScrapSecretStashes(
        ScrapShipDunGen gen,
        HashSet<Vector2i> wallTiles,
        HashSet<Vector2i> spawnTiles,
        Random random)
    {
        var stashCount = random.Next(gen.MinStashCount, gen.MaxStashCount + 1);
        var availableWalls = wallTiles.ToList();

        for (var s = 0; s < stashCount; s++)
        {
            if (!random.Prob(gen.HiddenStashChance))
                continue;

            var placed = false;
            while (!placed && TryTakeTile(availableWalls, random, out var wallTile))
            {
                if (!TryFindWallEntity(wallTile, out var wallUid))
                    continue;

                if (!TryGetStashTile(wallTile, spawnTiles, out var stashTile))
                    continue;

                var wallCoords = _maps.GridTileToLocal(_gridUid, _grid, wallTile);
                _entManager.DeleteEntity(wallUid);

                var door = _entManager.SpawnEntity("SolidSecretDoor", wallCoords);
                var trap = _entManager.EnsureComponent<ScrapShipDoorTrapComponent>(door);
                trap.ExplosionType = gen.ExplosionType;
                trap.TotalIntensity = gen.SecretDoorTrapTotalIntensity;
                trap.Slope = gen.SecretDoorTrapSlope;
                trap.MaxTileIntensity = gen.SecretDoorTrapMaxTileIntensity;
                trap.CanCreateVacuum = false;

                var stashCoords = _maps.GridTileToLocal(_gridUid, _grid, stashTile);
                SpawnScrapEntity(Pick(ScrapValuablePrototypes, random), stashCoords);
                SpawnScrapEntity("C4", stashCoords);
                SpawnScrapEntity("ClothingBackpackSatchelSmugglerFilled", stashCoords);
                AddScrapDecal(Pick(ScrapDecalsDirt, random), stashCoords);
                placed = true;
            }
        }
    }

    private void SpawnScrapEntity(EntProtoId prototype, EntityCoordinates coords)
    {
        var uid = _entManager.SpawnEntity(prototype, coords);
        _entManager.RemoveComponent<GhostRoleComponent>(uid);
        _entManager.RemoveComponent<GhostTakeoverAvailableComponent>(uid);

        if (_entManager.HasComponent<DoorComponent>(uid))
            return;

        if (_entManager.HasComponent<HTNComponent>(uid))
            _entManager.System<NPCSystem>().SleepNPC(uid);
    }

    private void AddScrapDecal(string decalId, EntityCoordinates coords)
    {
        _decals.TryAddDecal(decalId, coords, out _, cleanable: true);
    }

    private bool TryFindWallEntity(Vector2i tile, out EntityUid wallUid)
    {
        var anchored = _maps.GetAnchoredEntitiesEnumerator(_gridUid, _grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (_tags.HasTag(uid.Value, ScrapWallTag) || _tags.HasTag(uid.Value, ScrapWindowTag))
            {
                wallUid = uid.Value;
                return true;
            }
        }

        wallUid = default;
        return false;
    }

    private static readonly EntProtoId[] ScrapBloodPuddlePrototypes = ["PuddleBloodSmall", "PuddleBlood"];

    private bool TryGetStashTile(Vector2i wallTile, HashSet<Vector2i> spawnTiles, out Vector2i stashTile)
    {
        foreach (var offset in new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
        {
            var candidate = wallTile + offset;
            if (spawnTiles.Contains(candidate))
            {
                stashTile = candidate;
                return true;
            }
        }

        stashTile = default;
        return false;
    }

    private bool IsMachineTileFree(Vector2i tile)
    {
        return _anchorable.TileFree(
            _grid,
            tile,
            (int) CollisionGroup.MachineLayer,
            (int) CollisionGroup.MachineLayer);
    }

    private static T Pick<T>(IReadOnlyList<T> values, Random random)
    {
        return values[random.Next(values.Count)];
    }

    private bool TryTakeSpawnTile(List<Vector2i> availableTiles, Random random, out Vector2i tile)
    {
        while (TryTakeTile(availableTiles, random, out tile))
        {
            if (IsMachineTileFree(tile))
                return true;
        }

        tile = default;
        return false;
    }

    private static bool TryTakeTile(List<Vector2i> availableTiles, Random random, out Vector2i tile)
    {
        if (availableTiles.Count == 0)
        {
            tile = default;
            return false;
        }

        var index = random.Next(availableTiles.Count);
        tile = availableTiles[index];
        availableTiles[index] = availableTiles[^1];
        availableTiles.RemoveAt(availableTiles.Count - 1);
        return true;
    }
}

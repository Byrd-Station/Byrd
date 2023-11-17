// SPDX-FileCopyrightText: 2023 Visne <39844191+Visne@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 deltanedas <39013340+deltanedas@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 deltanedas <@deltanedas:kde.org>
// SPDX-FileCopyrightText: 2024 0x6273 <0x40@keemail.me>
// SPDX-FileCopyrightText: 2024 ElectroJr <leonsfriedrich@gmail.com>
// SPDX-FileCopyrightText: 2024 Kara <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2024 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 MilenVolf <63782763+MilenVolf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Piras314 <p1r4s@proton.me>
// SPDX-FileCopyrightText: 2024 SlamBamActionman <83650252+SlamBamActionman@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Vasilis <vasilis@pikachu.systems>
// SPDX-FileCopyrightText: 2024 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 metalgearsloth <comedian_vs_clown@hotmail.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Robust.Shared.CPUJob.JobQueues;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Parallax;
using Content.Server.Procedural;
using Content.Server.Salvage.Expeditions;
using Content.Shared.Atmos;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Dataset;
using Content.Shared.Gravity;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Shared.Procedural;
using Content.Shared.Procedural.Loot;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Salvage.Expeditions.Modifiers;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Content.Server.Shuttles.Components;

namespace Content.Server.Salvage;

public sealed class SpawnSalvageMissionJob : Job<bool>
{
    private readonly IEntityManager _entManager;
    private readonly IGameTiming _timing;
    private readonly IPrototypeManager _prototypeManager;
    private readonly AnchorableSystem _anchorable;
    private readonly BiomeSystem _biome;
    private readonly DungeonSystem _dungeon;
    private readonly MetaDataSystem _metaData;
    private readonly SharedMapSystem _map;
    private readonly ShuttleSystem _shuttle;
    private readonly StationSystem _stationSystem;
    private readonly SalvageSystem _salvage;

    public readonly EntityUid Station;
    public readonly EntityUid? CoordinatesDisk;
    private readonly SalvageMissionParams _missionParams;

    public SpawnSalvageMissionJob(
        double maxTime,
        IEntityManager entManager,
        IGameTiming timing,
        ILogManager logManager,
        IMapManager mapManager,
        IPrototypeManager protoManager,
        AnchorableSystem anchorable,
        BiomeSystem biome,
        DungeonSystem dungeon,
        MetaDataSystem metaData,
        SharedMapSystem map,
        SalvageSystem salvage,
        EntityUid station,
        EntityUid? coordinatesDisk,
        SalvageMissionParams missionParams,
        CancellationToken cancellation = default) : base(maxTime, cancellation)
    {
        _entManager = entManager;
        _timing = timing;
        _prototypeManager = protoManager;
        _anchorable = anchorable;
        _biome = biome;
        _dungeon = dungeon;
        _metaData = metaData;
        _map = map;
        _salvage = salvage;
        Station = station;
        CoordinatesDisk = coordinatesDisk;
        _missionParams = missionParams;
    }

    protected override async Task<bool> Process()
    {
        _sawmill.Debug("salvage", $"Spawning salvage mission with seed {_missionParams.Seed}");
        var mapUid = _map.CreateMap(out var mapId, runMapInit: false);
        var config = _missionParams.MissionType;
        var mapUid = _mapManager.GetMapEntityId(mapId);
        _mapManager.AddUninitializedMap(mapId);
        MetaDataComponent? metadata = null;
        var grid = _entManager.EnsureComponent<MapGridComponent>(mapUid);
        var random = new Random(_missionParams.Seed);
        var destComp = _entManager.AddComponent<FTLDestinationComponent>(mapUid);
        destComp.BeaconsOnly = true;
        destComp.RequireCoordinateDisk = true;
        destComp.Enabled = true;
        _metaData.SetEntityName(
            mapUid,
            _entManager.System<SharedSalvageSystem>().GetFTLName(_prototypeManager.Index(SalvageSystem.PlanetNames), _missionParams.Seed));
        _entManager.AddComponent<FTLBeaconComponent>(mapUid);

        // Saving the mission mapUid to a CD is made optional, in case one is somehow made in a process without a CD entity
        if (CoordinatesDisk.HasValue)
        {
            var cd = _entManager.EnsureComponent<ShuttleDestinationCoordinatesComponent>(CoordinatesDisk.Value);
            cd.Destination = mapUid;
            _entManager.Dirty(CoordinatesDisk.Value, cd);
        }

        // Setup mission configs
        // As we go through the config the rating will deplete so we'll go for most important to least important.

        var mission = _entManager.System<SharedSalvageSystem>()
            .GetMission(_missionParams.MissionType, _missionParams.Difficulty, _missionParams.Seed);

        var missionBiome = _prototypeManager.Index<SalvageBiomeMod>(mission.Biome);
        BiomeComponent? biome = null;

        if (missionBiome.BiomePrototype != null)
        {
            biome = _entManager.AddComponent<BiomeComponent>(mapUid);
            var biomeSystem = _entManager.System<BiomeSystem>();
            biomeSystem.SetTemplate(mapUid, biome, _prototypeManager.Index<BiomeTemplatePrototype>(missionBiome.BiomePrototype));
            biomeSystem.SetSeed(mapUid, biome, mission.Seed);
            _entManager.Dirty(mapUid, biome);

            // Gravity
            var gravity = _entManager.EnsureComponent<GravityComponent>(mapUid);
            gravity.Enabled = true;
            _entManager.Dirty(mapUid, gravity, metadata);

            // Atmos
            var air = _prototypeManager.Index<SalvageAirMod>(mission.Air);
            // copy into a new array since the yml deserialization discards the fixed length
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            air.Gases.CopyTo(moles, 0);
            var atmos = _entManager.EnsureComponent<MapAtmosphereComponent>(mapUid);
            _entManager.System<AtmosphereSystem>().SetMapSpace(mapUid, air.Space, atmos);
            _entManager.System<AtmosphereSystem>().SetMapGasMixture(mapUid, new GasMixture(moles, mission.Temperature), atmos);

            if (mission.Color != null)
            {
                var lighting = _entManager.EnsureComponent<MapLightComponent>(mapUid);
                lighting.AmbientLightColor = mission.Color.Value;
                _entManager.Dirty(lighting);
            }
        }

        _map.InitializeMap(mapId);
        _map.SetPaused(mapUid, true);

        // Setup expedition
        var expedition = _entManager.AddComponent<SalvageExpeditionComponent>(mapUid);
        expedition.Station = Station;
        expedition.EndTime = _timing.CurTime + mission.Duration;
        expedition.MissionParams = _missionParams;
        expedition.Difficulty = _missionParams.Difficulty;
        expedition.Rewards = mission.Rewards;

        var landingPadRadius = 24;
        var minDungeonOffset = landingPadRadius + 4;

        // We'll use the dungeon rotation as the spawn angle
        var dungeonRotation = _dungeon.GetDungeonRotation(_missionParams.Seed);

        var maxDungeonOffset = minDungeonOffset + 12;
        var dungeonOffsetDistance = minDungeonOffset + (maxDungeonOffset - minDungeonOffset) * random.NextFloat();
        var dungeonOffset = new Vector2(0f, dungeonOffsetDistance);
        dungeonOffset = dungeonRotation.RotateVec(dungeonOffset);
        var dungeonMod = _prototypeManager.Index<SalvageDungeonModPrototype>(mission.Dungeon);
        var dungeonConfig = _prototypeManager.Index(dungeonMod.Proto);
        var dungeons = await WaitAsyncTask(_dungeon.GenerateDungeonAsync(dungeonConfig, mapUid, grid, (Vector2i)dungeonOffset,
            _missionParams.Seed));

        var dungeon = dungeons.First();

        // Aborty
        if (dungeon.Rooms.Count == 0)
        {
            var maxDungeonOffset = minDungeonOffset + 12;
            var dungeonOffsetDistance = minDungeonOffset + (maxDungeonOffset - minDungeonOffset) * random.NextFloat();
            var dungeonOffset = new Vector2(0f, dungeonOffsetDistance);
            dungeonOffset = dungeonRotation.RotateVec(dungeonOffset);
            var dungeonMod = _prototypeManager.Index<SalvageDungeonMod>(mission.Dungeon);
            var dungeonConfig = _prototypeManager.Index<DungeonConfigPrototype>(dungeonMod.Proto);
            dungeon =
                await WaitAsyncTask(_dungeon.GenerateDungeonAsync(dungeonConfig, mapUid, grid, (Vector2i) dungeonOffset,
                    _missionParams.Seed));

            // Aborty
            if (dungeon.Rooms.Count == 0)
            {
                return false;
            }

            expedition.DungeonLocation = dungeonOffset;
        }

        List<Vector2i> reservedTiles = new();

        foreach (var tile in _map.GetTilesIntersecting(mapUid, grid, new Circle(Vector2.Zero, landingPadRadius), false))
        {
            if (!_biome.TryGetBiomeTile(mapUid, grid, tile.GridIndices, out _))
                continue;

            reservedTiles.Add(tile.GridIndices);
        }

        // Mission setup
        switch (config)
        {
            case SalvageMissionType.Mining:
                await SetupMining(mission, mapUid);
                break;
            case SalvageMissionType.Destruction:
                await SetupStructure(mission, dungeon, mapUid, grid, random);
                break;
            case SalvageMissionType.Elimination:
                await SetupElimination(mission, dungeon, mapUid, grid, random);
                break;
            default:
                throw new NotImplementedException();
        }

        // Handle loot
        // We'll always add this loot if possible
        foreach (var lootProto in _prototypeManager.EnumeratePrototypes<SalvageLootPrototype>())

        {
            if (!lootProto.Guaranteed)
                continue;

            try
            {
                await SpawnDungeonLoot(lootProto, mapUid);
            }
            catch (Exception e)
            {
                _sawmill.Error($"Failed to spawn guaranteed loot {lootProto.ID}: {e}");
            }
        }

        // Handle boss loot (when relevant).

        // Handle mob loot.

        // Handle remaining loot

        /*
         * MOB SPAWNS
         */

        var mobBudget = difficultyProto.MobBudget;
        var faction = _prototypeManager.Index<SalvageFactionPrototype>(mission.Faction);
        var randomSystem = _entManager.System<RandomSystem>();

        foreach (var entry in faction.MobGroups)
        {
            budgetEntries.Add(entry);
        }

        var probSum = budgetEntries.Sum(x => x.Prob);

        while (mobBudget > 0f)
        {
            var entry = randomSystem.GetBudgetEntry(ref mobBudget, ref probSum, budgetEntries, random);
            if (entry == null)
                break;

            try
            {
                await SpawnRandomEntry((mapUid, grid), entry, dungeon, random);
            }
            catch (Exception e)
            {
                _sawmill.Error($"Failed to spawn mobs for {entry.Proto}: {e}");
            }
        }

        var allLoot = _prototypeManager.Index(SharedSalvageSystem.ExpeditionsLootProto);
        var lootBudget = difficultyProto.LootBudget;

        foreach (var rule in allLoot.LootRules)
        {
            switch (rule)
            {
                case RandomSpawnsLoot randomLoot:
                    budgetEntries.Clear();

                    foreach (var entry in randomLoot.Entries)
                    {
                        budgetEntries.Add(entry);
                    }

                    probSum = budgetEntries.Sum(x => x.Prob);

                    while (lootBudget > 0f)
                    {
                        var entry = randomSystem.GetBudgetEntry(ref lootBudget, ref probSum, budgetEntries, random);
                        if (entry == null)
                            break;

                        _sawmill.Debug($"Spawning dungeon loot {entry.Proto}");
                        await SpawnRandomEntry((mapUid, grid), entry, dungeon, random);
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        return true;
    }

    private async Task SpawnRandomEntry(Entity<MapGridComponent> grid, IBudgetEntry entry, Dungeon dungeon, Random random)
    {
        await SuspendIfOutOfTime();

        var availableRooms = new ValueList<DungeonRoom>(dungeon.Rooms);
        var availableTiles = new List<Vector2i>();

        while (availableRooms.Count > 0)
        {
            availableTiles.Clear();
            var roomIndex = random.Next(availableRooms.Count);
            var room = availableRooms.RemoveSwap(roomIndex);
            availableTiles.AddRange(room.Tiles);

            while (availableTiles.Count > 0)
            {
                var tile = availableTiles.RemoveSwap(random.Next(availableTiles.Count));

                if (!_anchorable.TileFree(grid, tile, (int)CollisionGroup.MachineLayer,
                        (int)CollisionGroup.MachineLayer))
                {
                    continue;
                }

                var uid = _entManager.SpawnAtPosition(entry.Proto, _map.GridTileToLocal(grid, grid, tile));
                _entManager.RemoveComponent<GhostRoleComponent>(uid);
                _entManager.RemoveComponent<GhostTakeoverAvailableComponent>(uid);
                return;
            }
        }

        // oh noooooooooooo
    }

    private async Task SpawnDungeonLoot(SalvageLootPrototype loot, EntityUid gridUid)
    {
        for (var i = 0; i < loot.LootRules.Count; i++)
        {
            var rule = loot.LootRules[i];

            switch (rule)
            {
                case BiomeMarkerLoot biomeLoot:
                    {
                        if (_entManager.TryGetComponent<BiomeComponent>(gridUid, out var biome))
                        {
                            _biome.AddMarkerLayer(gridUid, biome, biomeLoot.Prototype);
                        }
                    }
                    break;
                case BiomeTemplateLoot biomeLoot:
                    {
                        if (_entManager.TryGetComponent<BiomeComponent>(gridUid, out var biome))
                        {
                            _biome.AddTemplate(gridUid, biome, "Loot", _prototypeManager.Index<BiomeTemplatePrototype>(biomeLoot.Prototype), i);
                        }
                    }
                    break;
            }
        }
    }

    #region Mission Specific

    private async Task SetupMining(
        SalvageMission mission,
        EntityUid gridUid)
    {
        var faction = _prototypeManager.Index<SalvageFactionPrototype>(mission.Faction);

        if (_entManager.TryGetComponent<BiomeComponent>(gridUid, out var biome))
        {
            // TODO: Better
            for (var i = 0; i < _salvage.GetDifficulty(mission.Difficulty); i++)
            {
                _biome.AddMarkerLayer(biome, faction.Configs["Mining"]);
            }
        }
    }

    private async Task SetupStructure(
        SalvageMission mission,
        Dungeon dungeon,
        EntityUid gridUid,
        MapGridComponent grid,
        Random random)
    {
        var structureComp = _entManager.EnsureComponent<SalvageStructureExpeditionComponent>(gridUid);
        var availableRooms = dungeon.Rooms.ToList();
        var faction = _prototypeManager.Index<SalvageFactionPrototype>(mission.Faction);
        await SpawnMobsRandomRooms(mission, dungeon, faction, grid, random);

        var structureCount = _salvage.GetStructureCount(mission.Difficulty);
        var shaggy = faction.Configs["DefenseStructure"];
        var validSpawns = new List<Vector2i>();

        // Spawn the objectives
        for (var i = 0; i < structureCount; i++)
        {
            var structureRoom = availableRooms[random.Next(availableRooms.Count)];
            validSpawns.Clear();
            validSpawns.AddRange(structureRoom.Tiles);
            random.Shuffle(validSpawns);

            while (validSpawns.Count > 0)
            {
                var spawnTile = validSpawns[^1];
                validSpawns.RemoveAt(validSpawns.Count - 1);

                if (!_anchorable.TileFree(grid, spawnTile, (int) CollisionGroup.MachineLayer,
                        (int) CollisionGroup.MachineLayer))
                {
                    continue;
                }

                var spawnPosition = grid.GridTileToLocal(spawnTile);
                var uid = _entManager.SpawnEntity(shaggy, spawnPosition);
                _entManager.AddComponent<SalvageStructureComponent>(uid);
                structureComp.Structures.Add(uid);
                break;
            }
        }
    }

    private async Task SetupElimination(
        SalvageMission mission,
        Dungeon dungeon,
        EntityUid gridUid,
        MapGridComponent grid,
        Random random)
    {
        // spawn megafauna in a random place
        var roomIndex = random.Next(dungeon.Rooms.Count);
        var room = dungeon.Rooms[roomIndex];
        var tile = room.Tiles.ElementAt(random.Next(room.Tiles.Count));
        var position = grid.GridTileToLocal(tile);

        var faction = _prototypeManager.Index<SalvageFactionPrototype>(mission.Faction);
        var prototype = faction.Configs["Megafauna"];
        var uid = _entManager.SpawnEntity(prototype, position);
        // not removing ghost role since its 1 megafauna, expect that you won't be able to cheese it.
        var eliminationComp = _entManager.EnsureComponent<SalvageEliminationExpeditionComponent>(gridUid);
        eliminationComp.Megafauna.Add(uid);

        // spawn less mobs than usual since there's megafauna to deal with too
        await SpawnMobsRandomRooms(mission, dungeon, faction, grid, random, 0.5f);
    }

    private async Task SpawnMobsRandomRooms(SalvageMission mission, Dungeon dungeon, SalvageFactionPrototype faction, MapGridComponent grid, Random random, float scale = 1f)
    {
        // scale affects how many groups are spawned, not the size of the groups themselves
        var groupSpawns = _salvage.GetSpawnCount(mission.Difficulty) * scale;
        var groupSum = faction.MobGroups.Sum(o => o.Prob);
        var validSpawns = new List<Vector2i>();

        for (var i = 0; i < groupSpawns; i++)
        {
            var roll = random.NextFloat() * groupSum;
            var value = 0f;

            foreach (var group in faction.MobGroups)
            {
                value += group.Prob;

                if (value < roll)
                    continue;

                var mobGroupIndex = random.Next(faction.MobGroups.Count);
                var mobGroup = faction.MobGroups[mobGroupIndex];

                var spawnRoomIndex = random.Next(dungeon.Rooms.Count);
                var spawnRoom = dungeon.Rooms[spawnRoomIndex];
                validSpawns.Clear();
                validSpawns.AddRange(spawnRoom.Tiles);
                random.Shuffle(validSpawns);

                foreach (var entry in EntitySpawnCollection.GetSpawns(mobGroup.Entries, random))
                {
                    while (validSpawns.Count > 0)
                    {
                        var spawnTile = validSpawns[^1];
                        validSpawns.RemoveAt(validSpawns.Count - 1);

                        if (!_anchorable.TileFree(grid, spawnTile, (int) CollisionGroup.MachineLayer,
                                (int) CollisionGroup.MachineLayer))
                        {
                            continue;
                        }

                        var spawnPosition = grid.GridTileToLocal(spawnTile);

                        var uid = _entManager.CreateEntityUninitialized(entry, spawnPosition);
                        _entManager.RemoveComponent<GhostTakeoverAvailableComponent>(uid);
                        _entManager.RemoveComponent<GhostRoleComponent>(uid);
                        _entManager.InitializeAndStartEntity(uid);

                        break;
                    }
                }

                await SuspendIfOutOfTime();
                break;
            }
        }
    }

    #endregion
}

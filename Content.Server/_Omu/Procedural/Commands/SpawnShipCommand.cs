using System.Numerics;
using Content.Server.Administration;
using Content.Server.Procedural;
using Content.Server.Station.Systems;
using Content.Shared._Omu.Procedural;
using Content.Shared.Administration;
using Content.Shared.Procedural;
using Content.Shared.Station.Components;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Omu.Procedural.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed class SpawnShipCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public string Command => "spawnship";
    public string Description => "Spawns a procedurally generated ship at your location.";
    public string Help => "Usage: spawnship <configId> [seed]\n  configId: any dungeonConfig prototype (e.g. SmallShipFreighter, MediumShipSkirmisher)\n  seed: optional integer seed";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var transform = _entManager.System<SharedTransformSystem>();
        var maps = _entManager.System<SharedMapSystem>();
        var dungeon = _entManager.System<DungeonSystem>();
        var station = _entManager.System<StationSystem>();

        if (args.Length < 1)
        {
            shell.WriteError("Config ID required. " + Help);
            return;
        }

        var configId = args[0];

        DungeonConfig config;
        if (_prototype.TryIndex<ShipConfigPrototype>(configId, out var shipConfig))
        {
            config = shipConfig.ToDungeonConfig();
        }
        else if (_prototype.TryIndex<DungeonConfigPrototype>(configId, out var dungeonConfig))
        {
            config = dungeonConfig;
        }
        else
        {
            shell.WriteError($"Unknown ship/dungeon config: {configId}");
            return;
        }

        int seed;
        if (args.Length >= 2)
        {
            if (!int.TryParse(args[1], out seed))
            {
                shell.WriteError("Invalid seed value.");
                return;
            }
        }
        else
        {
            seed = new Random().Next();
        }

        // Find a valid station grid across all stations.
        var stations = station.GetStations();
        if (stations.Count == 0)
        {
            shell.WriteError("No stations found.");
            return;
        }

        MapId targetMapId = MapId.Nullspace;
        Vector2 spawnPosition = Vector2.Zero;
        var foundStationGrid = false;
        const float distanceFromStation = 100f;

        foreach (var stationUid in stations)
        {
            if (!_entManager.TryGetComponent<StationDataComponent>(stationUid, out var stationData))
                continue;

            foreach (var stationGridUid in stationData.Grids)
            {
                if (!_entManager.TryGetComponent<TransformComponent>(stationGridUid, out var stationXform))
                    continue;

                var stationCoords = transform.GetMapCoordinates(stationGridUid, xform: stationXform);
                if (stationCoords.MapId == MapId.Nullspace || !maps.MapExists(stationCoords.MapId))
                    continue;

                targetMapId = stationCoords.MapId;
                spawnPosition = stationCoords.Position + new Vector2(distanceFromStation, 0);
                foundStationGrid = true;
                break;
            }

            if (foundStationGrid)
                break;
        }

        if (!foundStationGrid)
        {
            shell.WriteError("Unable to find a valid station.");
            return;
        }

        var targetCoordinates = new EntityCoordinates(maps.GetMap(targetMapId), spawnPosition);

        var tempMapUid = maps.CreateMap(out var tempMap);
        var gridUid = _entManager.CreateEntityUninitialized(null, new EntityCoordinates(tempMapUid, 0f, 0f));
        var grid = _entManager.AddComponent<MapGridComponent>(gridUid);
        _entManager.InitializeAndStartEntity(gridUid, tempMap);

        shell.WriteLine($"Spawning ship (config: {configId}, seed: {seed}) near ATS...");
        dungeon.GenerateDungeon(config, gridUid, grid, Vector2i.Zero, seed, targetCoordinates);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var hints = new List<string>();
            foreach (var p in _prototype.EnumeratePrototypes<ShipConfigPrototype>())
                hints.Add(p.ID);
            foreach (var p in _prototype.EnumeratePrototypes<DungeonConfigPrototype>())
                hints.Add(p.ID);
            return CompletionResult.FromHintOptions(hints, "Ship config prototype");
        }

        if (args.Length == 2)
        {
            return CompletionResult.FromHint("Optional seed (integer)");
        }

        return CompletionResult.Empty;
    }
}

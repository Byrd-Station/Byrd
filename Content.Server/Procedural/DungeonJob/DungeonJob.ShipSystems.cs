using System.Threading.Tasks;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonLayers;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// <see cref="ShipSystemsDunGen"/>
    /// </summary>
    private async Task PostGen(ShipSystemsDunGen gen, Dungeon dungeon, HashSet<Vector2i> reservedTiles, Random random)
    {
        if (dungeon.Rooms.Count == 0)
            return;

        var zones = new ShipZones(dungeon);
        var freeTiles = CollectFreeTiles(dungeon, reservedTiles, zones.Center.X);
        if (freeTiles.Count == 0)
            return;

        var usedTiles = new HashSet<Vector2i>();

        await PlaceThrusters(gen, dungeon, reservedTiles, usedTiles, random);
        if (gen.EnableBulkheads)
            PlaceBulkheads(gen, dungeon, usedTiles);

        await SuspendDungeon();
        if (!ValidateResume())
            return;

        // Cockpit at the bow, generator at the stern.
        var cockpitTile    = PlaceCockpit(gen, dungeon, freeTiles, usedTiles, zones.Center);
        var sternTile      = PlaceGenerator(gen, dungeon, freeTiles, usedTiles, zones.Center);

        // Power infrastructure near the stern / generator area.
        var substationTile = PlaceOnWall(gen.Substation, sternTile ?? zones.Stern, zones.Center, dungeon, usedTiles);

        // SMES near the substation / generator area.
        var smesTile = FindAdjacentFree(substationTile ?? sternTile ?? zones.Stern, freeTiles, usedTiles, dungeon);
        if (smesTile != null)
        {
            usedTiles.Add(smesTile.Value);
            _entManager.SpawnEntity(gen.Smes, _maps.GridTileToLocal(_gridUid, _grid, smesTile.Value));
            // Cable terminal on the same tile connects SMES to the HV network.
            _entManager.SpawnEntity(gen.CableTerminal, _maps.GridTileToLocal(_gridUid, _grid, smesTile.Value));
        }

        // Power cell recharger on the port side for crew accessibility.
        var rechargerTile = FindAdjacentFree(zones.MidPort, freeTiles, usedTiles, dungeon);
        if (rechargerTile != null)
        {
            usedTiles.Add(rechargerTile.Value);
            _entManager.SpawnEntity(gen.Recharger, _maps.GridTileToLocal(_gridUid, _grid, rechargerTile.Value));
        }

        await SuspendDungeon();
        if (!ValidateResume())
            return;

        PlaceEngineeringEquipment(gen, dungeon, freeTiles, usedTiles, zones.Center, sternTile, out var gyroTile, out var gravTile);
        var mixerTile = gen.EnableAtmosphere
            ? PlaceAtmosphereGasSupply(gen, dungeon, freeTiles, usedTiles, zones.Center, sternTile)
            : null;

        await SuspendDungeon();
        if (!ValidateResume())
            return;

        var ventTiles     = PlaceDistributed(gen.VentPump,     gen.VentCount,     freeTiles, usedTiles, dungeon, random, zones.Center);
        var scrubberTiles = PlaceDistributed(gen.VentScrubber, gen.ScrubberCount, freeTiles, usedTiles, dungeon, random, zones.Center);
        PlaceTwoStageAirlock(gen, dungeon, reservedTiles, usedTiles);
        PlaceHardRuleItems(gen, dungeon, freeTiles, usedTiles, zones, sternTile);

        await SuspendDungeon();
        if (!ValidateResume())
            return;

        PlaceDecals(gen, dungeon, sternTile);
        PlaceCabling(gen, dungeon, zones, cockpitTile, gyroTile, gravTile, ventTiles, scrubberTiles, sternTile, substationTile, smesTile, rechargerTile, usedTiles);
        PlaceAtmPipes(gen, dungeon, zones.Center, mixerTile, ventTiles, scrubberTiles, random);
    }
}

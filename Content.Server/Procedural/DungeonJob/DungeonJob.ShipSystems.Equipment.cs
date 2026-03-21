using System.Linq;
using System.Numerics;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonLayers;
using Robust.Shared.Map;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// Places the shuttle console and pilot seat at the bow (north, highest Y).
    /// Returns the console tile, or null if no suitable tile was found.
    /// </summary>
    private Vector2i? PlaceCockpit(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        List<Vector2i> freeTiles,
        HashSet<Vector2i> usedTiles,
        Vector2i center)
    {
        var cockpitTile = FindBestTile(freeTiles, usedTiles, t => t.Y * 10 - Math.Abs(t.X - center.X));
        if (cockpitTile == null)
            return null;

        usedTiles.Add(cockpitTile.Value);
        var coords = _maps.GridTileToLocal(_gridUid, _grid, cockpitTile.Value);
        var consoleEnt = _entManager.SpawnEntity(gen.ShuttleConsole, coords);
        RotateTowardCenter(consoleEnt, cockpitTile.Value, center);

        // Place pilot seat one tile behind the console (south of the bow).
        var seatTile = new Vector2i(cockpitTile.Value.X, cockpitTile.Value.Y - 1);
        if (dungeon.RoomTiles.Contains(seatTile) && !usedTiles.Contains(seatTile))
        {
            usedTiles.Add(seatTile);
            var seatEnt = _entManager.SpawnEntity(gen.PilotSeat, _maps.GridTileToLocal(_gridUid, _grid, seatTile));
            RotateTowardCenter(seatEnt, seatTile, center);
        }

        return cockpitTile;
    }

    /// <summary>
    /// Places the power generator (AME or simple) at the stern (south, lowest Y).
    /// Returns the generator/controller tile, or null if no suitable location was found.
    /// </summary>
    private Vector2i? PlaceGenerator(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        List<Vector2i> freeTiles,
        HashSet<Vector2i> usedTiles,
        Vector2i center)
    {
        if (gen.GeneratorShielding != null)
        {
            // AME: find a 3×3 area for shielding, then place the controller adjacent to it.
            var candidates = freeTiles.Except(usedTiles)
                .OrderBy(t => -t.Y * 10 - Math.Abs(t.X - (int)center.X))
                .ToList();

            var freeTileSet = new HashSet<Vector2i>(freeTiles);
            foreach (var coreCenter in candidates)
            {
                // Check that all 9 tiles of the 3×3 are free room tiles AND that the full
                // 5×5 border around them is interior.
                var allFit = true;
                for (var dx = -2; dx <= 2 && allFit; dx++)
                    for (var dy = -2; dy <= 2 && allFit; dy++)
                    {
                        var t = new Vector2i(coreCenter.X + dx, coreCenter.Y + dy);
                        if (!dungeon.RoomTiles.Contains(t))
                            allFit = false;
                    }
                // Also confirm all 9 core tiles are specifically free (anchorable, not reserved).
                for (var dx = -1; dx <= 1 && allFit; dx++)
                    for (var dy = -1; dy <= 1 && allFit; dy++)
                    {
                        var t = new Vector2i(coreCenter.X + dx, coreCenter.Y + dy);
                        if (!freeTileSet.Contains(t) || usedTiles.Contains(t))
                            allFit = false;
                    }

                if (!allFit)
                    continue;

                // Find an adjacent tile for the controller.
                Vector2i? controllerTile = null;
                foreach (var edge in new[]
                {
                    new Vector2i(coreCenter.X, coreCenter.Y - 2), // south
                    new Vector2i(coreCenter.X, coreCenter.Y + 2), // north
                    new Vector2i(coreCenter.X - 2, coreCenter.Y), // west
                    new Vector2i(coreCenter.X + 2, coreCenter.Y), // east
                })
                {
                    if (dungeon.RoomTiles.Contains(edge) && !usedTiles.Contains(edge))
                    {
                        controllerTile = edge;
                        break;
                    }
                }

                if (controllerTile == null)
                    continue;

                // Place 3×3 shielding core.
                for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var shieldTile = new Vector2i(coreCenter.X + dx, coreCenter.Y + dy);
                        usedTiles.Add(shieldTile);
                        _entManager.SpawnEntity(gen.GeneratorShielding, _maps.GridTileToLocal(_gridUid, _grid, shieldTile));
                    }

                // Place controller adjacent to the shielding.
                usedTiles.Add(controllerTile.Value);
                var genEnt = _entManager.SpawnEntity(gen.Generator, _maps.GridTileToLocal(_gridUid, _grid, controllerTile.Value));
                RotateTowardCenter(genEnt, controllerTile.Value, center);

                if (gen.GeneratorFuel != null)
                    _entManager.SpawnEntity(gen.GeneratorFuel, _maps.GridTileToLocal(_gridUid, _grid, controllerTile.Value));

                return controllerTile;
            }

            return null;
        }

        // Simple generator: pick the southernmost central tile.
        var sternTile = FindBestTile(freeTiles, usedTiles, t => -t.Y * 10 - Math.Abs(t.X - center.X));
        if (sternTile == null)
            return null;

        usedTiles.Add(sternTile.Value);
        var simpleGenEnt = _entManager.SpawnEntity(gen.Generator, _maps.GridTileToLocal(_gridUid, _grid, sternTile.Value));
        RotateTowardCenter(simpleGenEnt, sternTile.Value, center);
        return sternTile;
    }

    /// <summary>
    /// Places the gyroscope and gravity generator near the stern (engineering area).
    /// </summary>
    private void PlaceEngineeringEquipment(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        List<Vector2i> freeTiles,
        HashSet<Vector2i> usedTiles,
        Vector2i center,
        Vector2i? sternTile,
        out Vector2i? gyroTile,
        out Vector2i? gravTile)
    {
        gyroTile = FindAdjacentFree(sternTile ?? center, freeTiles, usedTiles, dungeon);
        if (gyroTile != null)
        {
            usedTiles.Add(gyroTile.Value);
            var gyroEnt = _entManager.SpawnEntity(gen.Gyroscope, _maps.GridTileToLocal(_gridUid, _grid, gyroTile.Value));
            RotateTowardCenter(gyroEnt, gyroTile.Value, center);
        }

        gravTile = FindAdjacentFree(gyroTile ?? sternTile ?? center, freeTiles, usedTiles, dungeon);
        if (gravTile != null)
        {
            usedTiles.Add(gravTile.Value);
            var gravEnt = _entManager.SpawnEntity(gen.GravityGenerator, _maps.GridTileToLocal(_gridUid, _grid, gravTile.Value));
            RotateTowardCenter(gravEnt, gravTile.Value, center);
        }
    }

    /// <summary>
    /// Places O2/N2 canisters, gas ports, and a gas mixer near the stern.
    /// Returns the mixer tile, or null if atmosphere placement is impossible.
    /// </summary>
    private Vector2i? PlaceAtmosphereGasSupply(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        List<Vector2i> freeTiles,
        HashSet<Vector2i> usedTiles,
        Vector2i center,
        Vector2i? sternTile)
    {
        var atmosBase = sternTile ?? center;

        var o2Tile = FindAdjacentFree(atmosBase, freeTiles, usedTiles, dungeon);
        if (o2Tile != null)
        {
            usedTiles.Add(o2Tile.Value);
            var o2Ent = _entManager.SpawnEntity(gen.OxygenCanister, _maps.GridTileToLocal(_gridUid, _grid, o2Tile.Value));
            RotateTowardCenter(o2Ent, o2Tile.Value, center);
            var gasPort1 = _entManager.SpawnEntity(gen.GasPort, _maps.GridTileToLocal(_gridUid, _grid, o2Tile.Value));
            RotateTowardCenter(gasPort1, o2Tile.Value, center);
        }

        var n2Tile = FindAdjacentFree(o2Tile ?? atmosBase, freeTiles, usedTiles, dungeon);
        if (n2Tile != null)
        {
            usedTiles.Add(n2Tile.Value);
            var n2Ent = _entManager.SpawnEntity(gen.NitrogenCanister, _maps.GridTileToLocal(_gridUid, _grid, n2Tile.Value));
            RotateTowardCenter(n2Ent, n2Tile.Value, center);
            var gasPort2 = _entManager.SpawnEntity(gen.GasPort, _maps.GridTileToLocal(_gridUid, _grid, n2Tile.Value));
            RotateTowardCenter(gasPort2, n2Tile.Value, center);
        }

        var mixerTile = FindAdjacentFree(n2Tile ?? o2Tile ?? atmosBase, freeTiles, usedTiles, dungeon);
        if (mixerTile != null)
        {
            usedTiles.Add(mixerTile.Value);
            var mixerEnt = _entManager.SpawnEntity(gen.GasMixer, _maps.GridTileToLocal(_gridUid, _grid, mixerTile.Value));
            RotateTowardCenter(mixerEnt, mixerTile.Value, center);
        }

        return mixerTile;
    }

    /// <summary>
    /// Places intercom, emergency closet, fire extinguisher,
    /// air alarm, fire alarm, and firelock near the airlock entrance.
    /// </summary>
    private void PlaceHardRuleItems(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        List<Vector2i> freeTiles,
        HashSet<Vector2i> usedTiles,
        ShipZones zones,
        Vector2i? sternTile)
    {
        // Intercom wall-mounted near the bow (bridge area).
        PlaceOnWall(gen.Intercom, zones.Bow, zones.Center, dungeon, usedTiles);

        // Emergency closet in mid-ship for accessibility.
        var emergTile = FindAdjacentFree(zones.MidStarboard, freeTiles, usedTiles, dungeon);
        if (emergTile != null)
        {
            usedTiles.Add(emergTile.Value);
            var emergEnt = _entManager.SpawnEntity(gen.EmergencyCloset, _maps.GridTileToLocal(_gridUid, _grid, emergTile.Value));
            RotateTowardCenter(emergEnt, emergTile.Value, zones.Center);
        }

        // Fire extinguisher near engineering area (stern).
        var extTile = FindAdjacentFree(sternTile ?? zones.Stern, freeTiles, usedTiles, dungeon);
        if (extTile != null)
        {
            usedTiles.Add(extTile.Value);
            var extEnt = _entManager.SpawnEntity(gen.FireExtinguisher, _maps.GridTileToLocal(_gridUid, _grid, extTile.Value));
            RotateTowardCenter(extEnt, extTile.Value, zones.Center);
        }

        // Air alarm on wall near bow, fire alarm near stern - spread across ship.
        PlaceOnWall(gen.AirAlarm, zones.Bow, zones.Center, dungeon, usedTiles);
        PlaceOnWall(gen.FireAlarm, sternTile ?? zones.Stern, zones.Center, dungeon, usedTiles);

        // Firelock near the airlock entrance for compartment sealing.
        if (dungeon.Entrances.Count > 0)
        {
            var entranceTile = dungeon.Entrances.First();
            var firelockTile = FindAdjacentFree(entranceTile, freeTiles, usedTiles, dungeon);
            if (firelockTile != null)
            {
                usedTiles.Add(firelockTile.Value);
                var firelockEnt = _entManager.SpawnEntity(gen.Firelock, _maps.GridTileToLocal(_gridUid, _grid, firelockTile.Value));
                RotateTowardCenter(firelockEnt, firelockTile.Value, zones.Center);
            }
        }
    }

    /// <summary>
    /// Places caution decals around engineering equipment and loading-area decals at airlocks.
    /// </summary>
    private void PlaceDecals(ShipSystemsDunGen gen, Dungeon dungeon, Vector2i? sternTile)
    {
        if (sternTile != null)
        {
            var offset = new Vector2(0.5f, 0.5f);
            var gridPos = _maps.GridTileToLocal(_gridUid, _grid, sternTile.Value).Offset(offset);
            _decals.TryAddDecal(gen.CautionDecal, gridPos, out _);
        }

        foreach (var entrance in dungeon.Entrances)
        {
            var offset = new Vector2(0.5f, 0.5f);
            var gridPos = _maps.GridTileToLocal(_gridUid, _grid, entrance).Offset(offset);
            _decals.TryAddDecal(gen.AirlockDecal, gridPos, out _);
        }
    }
}

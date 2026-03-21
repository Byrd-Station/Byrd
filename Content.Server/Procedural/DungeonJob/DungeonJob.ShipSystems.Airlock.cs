using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonLayers;
using Robust.Shared.Map;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    private void PlaceTwoStageAirlock(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        HashSet<Vector2i> reservedTiles,
        HashSet<Vector2i> usedTiles)
    {
        // 2-Stage Airlock: exterior airlock → buffer tile → interior airlock.
        // Try stern (south) first, then port (west).
        // (inward, outward) directions for each side.
        var sides = new (int dx, int dy)[] { (0, 1), (1, 0) }; // south inward=+Y; port inward=+X

        // Score: stern candidates by lowest Y, port candidates by lowest X.
        foreach (var (dx, dy) in sides)
        {
            // Build candidates on this face: exterior tiles whose inward neighbor is interior.
            var candidates = new List<Vector2i>();
            foreach (var ext in dungeon.RoomExteriorTiles)
            {
                if (usedTiles.Contains(ext) || reservedTiles.Contains(ext))
                    continue;

                var inward1 = new Vector2i(ext.X + dx, ext.Y + dy);
                var inward2 = new Vector2i(ext.X + dx * 2, ext.Y + dy * 2);
                var outward = new Vector2i(ext.X - dx, ext.Y - dy);

                var hasExterior = !dungeon.RoomTiles.Contains(outward) && !dungeon.RoomExteriorTiles.Contains(outward);
                var hasBuffer   = dungeon.RoomTiles.Contains(inward1) && !usedTiles.Contains(inward1);
                var hasInner    = dungeon.RoomTiles.Contains(inward2) && !usedTiles.Contains(inward2);

                if (hasExterior && hasBuffer && hasInner)
                    candidates.Add(ext);
            }

            if (candidates.Count == 0)
                continue;

            // Pick the tile furthest out on this face (lowest Y for stern, lowest X for port).
            candidates.Sort((a, b) => dy != 0 ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            var best = candidates[0];

            var buf   = new Vector2i(best.X + dx, best.Y + dy);
            var inner = new Vector2i(best.X + dx * 2, best.Y + dy * 2);

            usedTiles.Add(best);
            usedTiles.Add(buf);
            usedTiles.Add(inner);

            // Remove walls placed by BoundaryWallDunGen so docks don't overlap walls.
            DeleteWallsAt(best);
            DeleteWallsAt(buf);
            DeleteWallsAt(inner);

            _entManager.SpawnEntity(gen.Airlock, _maps.GridTileToLocal(_gridUid, _grid, best));
            dungeon.Entrances.Add(best);
            // buf stays empty - pressure equalization space.
            _entManager.SpawnEntity(gen.InteriorAirlock, _maps.GridTileToLocal(_gridUid, _grid, inner));

            // Place bulkhead walls flanking both sides of the airlock corridor.
            var perpX = dy; // perpendicular to the inward axis
            var perpY = dx;
            foreach (var airlockTile in new[] { best, buf, inner })
            {
                foreach (var sign in new[] { 1, -1 })
                {
                    var flank = new Vector2i(airlockTile.X + perpX * sign, airlockTile.Y + perpY * sign);
                    if (usedTiles.Contains(flank))
                        continue;
                    if (!dungeon.RoomTiles.Contains(flank) && !dungeon.RoomExteriorTiles.Contains(flank))
                        continue;

                    usedTiles.Add(flank);
                    DeleteWallsAt(flank);
                    _entManager.SpawnEntity(gen.BulkheadWall, _maps.GridTileToLocal(_gridUid, _grid, flank));
                }
            }

            return;
        }

        // Fallback: if no 2-stage arrangement fits, place single airlock.
        PlaceAirlockFallback(gen, dungeon, reservedTiles, usedTiles);
    }

    private void PlaceAirlockFallback(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        HashSet<Vector2i> reservedTiles,
        HashSet<Vector2i> usedTiles)
    {
        // Try stern (south) then port (west). Pick furthest tile on that face.
        var sides = new (int dx, int dy)[] { (0, 1), (1, 0) };

        foreach (var (dx, dy) in sides)
        {
            var candidates = new List<Vector2i>();
            foreach (var ext in dungeon.RoomExteriorTiles)
            {
                if (usedTiles.Contains(ext) || reservedTiles.Contains(ext))
                    continue;

                var inward  = new Vector2i(ext.X + dx, ext.Y + dy);
                var outward = new Vector2i(ext.X - dx, ext.Y - dy);

                var hasInterior = dungeon.RoomTiles.Contains(inward);
                var hasExterior = !dungeon.RoomTiles.Contains(outward) && !dungeon.RoomExteriorTiles.Contains(outward);

                if (hasInterior && hasExterior)
                    candidates.Add(ext);
            }

            if (candidates.Count == 0)
                continue;

            candidates.Sort((a, b) => dy != 0 ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            var best = candidates[0];

            usedTiles.Add(best);
            DeleteWallsAt(best);
            _entManager.SpawnEntity(gen.Airlock, _maps.GridTileToLocal(_gridUid, _grid, best));
            dungeon.Entrances.Add(best);
            return;
        }
    }
}

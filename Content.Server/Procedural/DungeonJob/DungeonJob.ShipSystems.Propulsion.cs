using System.Threading.Tasks;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonLayers;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    private async Task PlaceThrusters(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        HashSet<Vector2i> reservedTiles,
        HashSet<Vector2i> usedTiles,
        Random random)
    {
        // Group exterior tiles by cardinal direction relative to center.
        var center = dungeon.Rooms[0].Center;

        // Per-direction thruster counts: stern > sides > bow.
        // ThrustersPerDirection is a YAML convenience override.
        var sternCount     = gen.ThrustersPerDirection > 0 ? gen.ThrustersPerDirection : gen.ThrustersStern;
        var bowCount       = gen.ThrustersPerDirection > 0 ? gen.ThrustersPerDirection : gen.ThrustersBow;
        var portCount      = gen.ThrustersPerDirection > 0 ? gen.ThrustersPerDirection : gen.ThrustersPort;
        var starboardCount = gen.ThrustersPerDirection > 0 ? gen.ThrustersPerDirection : gen.ThrustersStarboard;

        var directionCounts = new (Direction Dir, int Count)[]
        {
            (Direction.North, bowCount),        // bow
            (Direction.South, sternCount),      // stern (main thrust)
            (Direction.East, starboardCount),   // starboard
            (Direction.West, portCount),        // port
        };

        foreach (var (dir, maxCount) in directionCounts)
        {
            var dirVec = dir.ToIntVec();
            var candidates = new List<Vector2i>();

            foreach (var exteriorTile in dungeon.RoomExteriorTiles)
            {
                // A tile is a thruster candidate if the tile one step inward is interior.
                var inward = exteriorTile - dirVec;
                if (!dungeon.RoomTiles.Contains(inward))
                    continue;

                // And the tile itself is not interior.
                if (dungeon.RoomTiles.Contains(exteriorTile))
                    continue;

                if (reservedTiles.Contains(exteriorTile) || usedTiles.Contains(exteriorTile))
                    continue;

                // Check the tile is on the correct edge.
                var relPos = exteriorTile - center.Floored();
                switch (dir)
                {
                    case Direction.North when relPos.Y > 0:
                    case Direction.South when relPos.Y < 0:
                    case Direction.East when relPos.X > 0:
                    case Direction.West when relPos.X < 0:
                        candidates.Add(exteriorTile);
                        break;
                }
            }

            random.Shuffle(candidates);
            var placed = 0;

            foreach (var wallTile in candidates)
            {
                if (placed >= maxCount)
                    break;

                // Place the thruster one tile further out from the hull wall.
                var thrusterTile = wallTile + dirVec;

                if (usedTiles.Contains(thrusterTile))
                    continue;

                // Skip if this tile is part of the hull (has a wall on it).
                if (dungeon.RoomExteriorTiles.Contains(thrusterTile))
                    continue;

                usedTiles.Add(thrusterTile);

                // Place a lattice tile so the thruster has a structural anchor in space.
                var latticeDef = (ContentTileDefinition) _tileDefManager[gen.LatticeTile];
                _maps.SetTile(_gridUid, _grid, thrusterTile, _tile.GetVariantTile(latticeDef, random));

                var ent = _entManager.SpawnEntity(gen.Thruster, _maps.GridTileToLocal(_gridUid, _grid, thrusterTile));
                // Rotate thruster to face away from the ship (180 degrees from the face direction).
                _transform.SetLocalRotation(ent, dir.ToAngle() + Math.PI);
                placed++;
            }
        }
    }

    private void PlaceBulkheads(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        HashSet<Vector2i> usedTiles)
    {
        if (dungeon.Rooms.Count == 0)
            return;

        var room = dungeon.Rooms[0];
        var center = room.Center.Floored();

        // Find Y-axis extents of the interior (bow=north=+Y, stern=south=−Y).
        var minY = int.MaxValue;
        var maxY = int.MinValue;
        foreach (var tile in dungeon.RoomTiles)
        {
            minY = Math.Min(minY, tile.Y);
            maxY = Math.Max(maxY, tile.Y);
        }

        var length = maxY - minY + 1;
        if (length < gen.MinBulkheadLength)
            return; // Ship too small to partition.

        // Compute bulkhead Y positions.
        var bowBulkheadY   = maxY - (int)(length * gen.BowBulkheadFraction);
        var sternBulkheadY = minY + (int)(length * gen.SternBulkheadFraction);

        // Don't let the bulkheads overlap or be too close.
        if (bowBulkheadY <= sternBulkheadY + gen.MinBulkheadSpacing)
            return;

        PlaceBulkheadLine(gen, dungeon, usedTiles, bowBulkheadY, center.X);
        PlaceBulkheadLine(gen, dungeon, usedTiles, sternBulkheadY, center.X);

        // Narrow the bow section to a proper bridge room width.
        if (gen.BridgeWidth > 0)
            NarrowBowToBridge(gen, dungeon, usedTiles, bowBulkheadY, center.X);

        // Replace bow hull walls with shuttle windows for cockpit viewports.
        if (gen.EnableCockpitWindows)
            ReplaceBowHullWithWindows(gen, dungeon, bowBulkheadY, gen.BridgeWidth > 0 ? gen.BridgeWidth : int.MaxValue, center.X);
    }

    /// <summary>
    /// Places a horizontal line of walls at the given Y coordinate across all interior tiles,
    /// leaving a doorway at the X position closest to centerX.
    /// </summary>
    private void PlaceBulkheadLine(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        HashSet<Vector2i> usedTiles,
        int bulkheadY,
        int centerX)
    {
        // Gather all interior tiles at this Y.
        var rowTiles = new List<Vector2i>();
        foreach (var tile in dungeon.RoomTiles)
        {
            if (tile.Y == bulkheadY)
                rowTiles.Add(tile);
        }

        if (rowTiles.Count == 0)
            return;

        // Sort by distance to center X so the doorway is placed at the most central tile.
        rowTiles.Sort((a, b) => Math.Abs(a.X - centerX).CompareTo(Math.Abs(b.X - centerX)));

        var doorTile = rowTiles[0];

        foreach (var tile in rowTiles)
        {
            if (usedTiles.Contains(tile))
                continue;

            usedTiles.Add(tile);

            if (tile == doorTile)
            {
                // Place airlock door at the center of the bulkhead.
                _entManager.SpawnEntity(gen.BulkheadDoor, _maps.GridTileToLocal(_gridUid, _grid, tile));
            }
            else
            {
                // Place wall segment.
                _entManager.SpawnEntity(gen.BulkheadWall, _maps.GridTileToLocal(_gridUid, _grid, tile));
            }
        }
    }

    /// <summary>
    /// Fills bow-section tiles (Y &gt; bowBulkheadY) that fall outside the bridge width with
    /// bulkhead walls, creating a narrow, enclosed bridge room of exactly BridgeWidth tiles.
    /// </summary>
    private void NarrowBowToBridge(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        HashSet<Vector2i> usedTiles,
        int bowBulkheadY,
        int centerX)
    {
        var half = gen.BridgeWidth / 2;

        foreach (var tile in dungeon.RoomTiles)
        {
            if (tile.Y <= bowBulkheadY)
                continue; // not in the bow section

            if (tile.X >= centerX - half && tile.X <= centerX + half)
                continue; // inside the bridge room

            if (usedTiles.Contains(tile))
                continue;

            usedTiles.Add(tile);
            _entManager.SpawnEntity(gen.BulkheadWall, _maps.GridTileToLocal(_gridUid, _grid, tile));
        }
    }

    /// <summary>
    /// Replaces hull wall entities on exterior tiles bordering the bow (cockpit) section
    /// with shuttle windows, giving the cockpit a viewport.
    /// Only tiles within <paramref name="bridgeWidth"/> of <paramref name="centerX"/> receive windows.
    /// </summary>
    private void ReplaceBowHullWithWindows(ShipSystemsDunGen gen, Dungeon dungeon, int bowBulkheadY, int bridgeWidth, int centerX)
    {
        var half = bridgeWidth / 2;
        var cardinalOffsets = new (int Dx, int Dy)[] { (0, 1), (0, -1), (1, 0), (-1, 0) };

        foreach (var extTile in dungeon.RoomExteriorTiles)
        {
            // Only put windows alongside the bridge room, not the filled wing areas.
            if (extTile.X < centerX - half - 1 || extTile.X > centerX + half + 1)
                continue;

            // Only process exterior tiles adjacent to at least one bow interior tile (Y > bowBulkheadY = north).
            var isAdjacentToBow = false;
            foreach (var (dx, dy) in cardinalOffsets)
            {
                var neighbor = new Vector2i(extTile.X + dx, extTile.Y + dy);
                if (dungeon.RoomTiles.Contains(neighbor) && neighbor.Y > bowBulkheadY)
                {
                    isAdjacentToBow = true;
                    break;
                }
            }

            if (!isAdjacentToBow)
                continue;

            // Collect wall entities first, then delete - avoids modifying the collection mid-enumeration.
            var toDelete = new List<EntityUid>();
            var anchored = _maps.GetAnchoredEntitiesEnumerator(_gridUid, _grid, extTile);
            while (anchored.MoveNext(out var uid))
            {
                if (_tags.HasTag(uid.Value, WallTag))
                    toDelete.Add(uid.Value);
            }
            foreach (var uid in toDelete)
                _entManager.DeleteEntity(uid);

            // Spawn shuttle window in place of the removed wall.
            _entManager.SpawnEntity(gen.CockpitWindow, _maps.GridTileToLocal(_gridUid, _grid, extTile));
        }
    }
}

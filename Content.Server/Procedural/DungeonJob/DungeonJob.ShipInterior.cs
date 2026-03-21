using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonLayers;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// <see cref="ShipInteriorDunGen"/>
    /// </summary>
    private async Task PostGen(ShipInteriorDunGen gen, Dungeon dungeon, HashSet<Vector2i> reservedTiles, Random random)
    {
        if (dungeon.Rooms.Count == 0)
            return;

        var room = dungeon.Rooms[0];
        var center = room.Center.Floored();

        // Determine ship Y-extents.
        var minY = int.MaxValue;
        var maxY = int.MinValue;
        var minX = int.MaxValue;
        var maxX = int.MinValue;
        foreach (var tile in dungeon.RoomTiles)
        {
            minY = Math.Min(minY, tile.Y);
            maxY = Math.Max(maxY, tile.Y);
            minX = Math.Min(minX, tile.X);
            maxX = Math.Max(maxX, tile.X);
        }

        var length = maxY - minY + 1;
        var width = maxX - minX + 1;

        // Need enough space for at least one partition.
        if (length < 5 || width < 3)
            return;

        var usedTiles = new HashSet<Vector2i>();

        switch (gen.Layout)
        {
            case ShipInteriorLayout.Lanes:
                PlaceLanePartitions(gen, dungeon, usedTiles, random, center, minY, maxY, minX, maxX);
                break;
            case ShipInteriorLayout.Corridor:
                PlaceCorridorPartitions(gen, dungeon, usedTiles, random, center, minY, maxY, minX, maxX);
                break;
            case ShipInteriorLayout.Ring:
                PlaceRingPartitions(gen, dungeon, usedTiles, random, center, minY, maxY, minX, maxX);
                break;
            case ShipInteriorLayout.Open:
            default:
                break; // No partitioning.
        }

        await SuspendDungeon();
    }

    /// <summary>
    /// Lanes layout: place horizontal walls at evenly spaced Y positions to create
    /// bow / mid sections / stern compartments. Each wall has a central doorway.
    /// </summary>
    private void PlaceLanePartitions(
        ShipInteriorDunGen gen,
        Dungeon dungeon,
        HashSet<Vector2i> usedTiles,
        Random random,
        Vector2i center,
        int minY, int maxY, int minX, int maxX)
    {
        var length = maxY - minY + 1;
        var roomCount = Math.Clamp(random.Next(gen.MinRooms, gen.MaxRooms + 1), 2, Math.Max(2, length / 3));

        // Compute partition Y positions (evenly spaced, excluding very edge).
        var partitions = new List<int>();
        var step = length / roomCount;
        for (var i = 1; i < roomCount; i++)
        {
            var partY = minY + i * step;
            // Jitter the position slightly for variety.
            partY += random.Next(-1, 2);
            partY = Math.Clamp(partY, minY + 2, maxY - 2);
            partitions.Add(partY);
        }

        // Deduplicate and sort.
        partitions = partitions.Distinct().OrderBy(y => y).ToList();

        foreach (var partY in partitions)
        {
            PlaceInteriorWallLine(gen, dungeon, usedTiles, partY, center.X, horizontal: true, minX, maxX);
        }
    }

    /// <summary>
    /// Corridor layout: vertical wall pair flanking a central corridor, plus
    /// horizontal partitions within the side rooms.
    /// </summary>
    private void PlaceCorridorPartitions(
        ShipInteriorDunGen gen,
        Dungeon dungeon,
        HashSet<Vector2i> usedTiles,
        Random random,
        Vector2i center,
        int minY, int maxY, int minX, int maxX)
    {
        var corridorHalf = gen.CorridorWidth / 2;

        // Place vertical partition walls on each side of the central corridor.
        var wallXPort = center.X - corridorHalf - 1;
        var wallXStarboard = center.X + corridorHalf + 1;

        PlaceInteriorWallLine(gen, dungeon, usedTiles, wallXPort, center.Y, horizontal: false, minY, maxY);
        PlaceInteriorWallLine(gen, dungeon, usedTiles, wallXStarboard, center.Y, horizontal: false, minY, maxY);

        // Optionally retile the corridor.
        if (gen.CorridorFloorTile != null)
        {
            var corridorDef = _prototype.Index(gen.CorridorFloorTile.Value);
            var tileChanges = new List<(Vector2i Index, Tile Tile)>();
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = center.X - corridorHalf; x <= center.X + corridorHalf; x++)
                {
                    var pos = new Vector2i(x, y);
                    if (dungeon.RoomTiles.Contains(pos) && !usedTiles.Contains(pos))
                        tileChanges.Add((pos, _tile.GetVariantTile(corridorDef, random)));
                }
            }
            _maps.SetTiles(_gridUid, _grid, tileChanges);
        }

        // Add horizontal partitions in the side rooms.
        var length = maxY - minY + 1;
        var sideRooms = Math.Clamp(random.Next(gen.MinRooms, gen.MaxRooms + 1) - 1, 1, Math.Max(1, length / 4));
        var step = length / (sideRooms + 1);
        for (var i = 1; i <= sideRooms; i++)
        {
            var partY = minY + i * step + random.Next(-1, 2);
            partY = Math.Clamp(partY, minY + 2, maxY - 2);

            // Port side partitions -- door at midpoint of the port room width.
            var portDoorX = (minX + wallXPort - 1) / 2;
            PlaceInteriorWallSegment(gen, dungeon, usedTiles, partY, minX, wallXPort - 1, portDoorX);
            // Starboard side partitions -- door at midpoint of the starboard room width.
            var starboardDoorX = (wallXStarboard + 1 + maxX) / 2;
            PlaceInteriorWallSegment(gen, dungeon, usedTiles, partY, wallXStarboard + 1, maxX, starboardDoorX);
        }
    }

    /// <summary>
    /// Ring layout: an outer corridor loop around a central room cluster.
    /// Creates two vertical and two horizontal partition lines forming a rectangle.
    /// </summary>
    private void PlaceRingPartitions(
        ShipInteriorDunGen gen,
        Dungeon dungeon,
        HashSet<Vector2i> usedTiles,
        Random random,
        Vector2i center,
        int minY, int maxY, int minX, int maxX)
    {
        var length = maxY - minY + 1;
        var width = maxX - minX + 1;

        // Ring inset: roughly 30% from each edge.
        var insetY = Math.Max(2, length / 4);
        var insetX = Math.Max(2, width / 4);

        var ringMinY = minY + insetY;
        var ringMaxY = maxY - insetY;
        var ringMinX = minX + insetX;
        var ringMaxX = maxX - insetX;

        if (ringMaxY <= ringMinY + 2 || ringMaxX <= ringMinX + 2)
            return; // Too small for a ring.

        // Horizontal walls at ringMinY and ringMaxY.
        PlaceInteriorWallSegment(gen, dungeon, usedTiles, ringMinY, ringMinX, ringMaxX, center.X);
        PlaceInteriorWallSegment(gen, dungeon, usedTiles, ringMaxY, ringMinX, ringMaxX, center.X);

        // Vertical walls at ringMinX and ringMaxX (between the horizontal walls).
        for (var y = ringMinY + 1; y < ringMaxY; y++)
        {
            foreach (var wallX in new[] { ringMinX, ringMaxX })
            {
                var pos = new Vector2i(wallX, y);
                if (!dungeon.RoomTiles.Contains(pos) || usedTiles.Contains(pos))
                    continue;
                usedTiles.Add(pos);
                _entManager.SpawnEntity(gen.PartitionWall, _maps.GridTileToLocal(_gridUid, _grid, pos));
            }
        }

        // Doorways in each wall side (centred).
        var midY = (ringMinY + ringMaxY) / 2;
        foreach (var wallX in new[] { ringMinX, ringMaxX })
        {
            var doorPos = new Vector2i(wallX, midY);
            DeleteWallsAt(doorPos);
            if (usedTiles.Contains(doorPos))
                usedTiles.Remove(doorPos);
            _entManager.SpawnEntity(gen.PartitionDoor, _maps.GridTileToLocal(_gridUid, _grid, doorPos));
            usedTiles.Add(doorPos);
        }

        // Optionally retile the corridor ring.
        if (gen.CorridorFloorTile != null)
        {
            var corridorDef = _prototype.Index(gen.CorridorFloorTile.Value);
            var tileChanges = new List<(Vector2i Index, Tile Tile)>();
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var pos = new Vector2i(x, y);
                    if (!dungeon.RoomTiles.Contains(pos) || usedTiles.Contains(pos))
                        continue;
                    // Outside the ring walls = corridor.
                    if (x < ringMinX || x > ringMaxX || y < ringMinY || y > ringMaxY)
                        tileChanges.Add((pos, _tile.GetVariantTile(corridorDef, random)));
                }
            }
            _maps.SetTiles(_gridUid, _grid, tileChanges);
        }
    }

    /// <summary>
    /// Places a full-width interior wall line at the given position with a central doorway.
    /// <paramref name="horizontal"/> = true means a Y=const line; false means X=const line.
    /// </summary>
    private void PlaceInteriorWallLine(
        ShipInteriorDunGen gen,
        Dungeon dungeon,
        HashSet<Vector2i> usedTiles,
        int fixedCoord,
        int doorCoord,
        bool horizontal,
        int rangeMin, int rangeMax)
    {
        var wallTiles = new List<Vector2i>();

        for (var v = rangeMin; v <= rangeMax; v++)
        {
            var pos = horizontal ? new Vector2i(v, fixedCoord) : new Vector2i(fixedCoord, v);
            if (!dungeon.RoomTiles.Contains(pos) || usedTiles.Contains(pos))
                continue;
            wallTiles.Add(pos);
        }

        if (wallTiles.Count == 0)
            return;

        // Pick the tile closest to doorCoord as the doorway.
        wallTiles.Sort((a, b) =>
        {
            var da = horizontal ? Math.Abs(a.X - doorCoord) : Math.Abs(a.Y - doorCoord);
            var db = horizontal ? Math.Abs(b.X - doorCoord) : Math.Abs(b.Y - doorCoord);
            return da.CompareTo(db);
        });

        var doorTile = wallTiles[0];

        foreach (var tile in wallTiles)
        {
            usedTiles.Add(tile);

            if (tile == doorTile)
            {
                _entManager.SpawnEntity(gen.PartitionDoor, _maps.GridTileToLocal(_gridUid, _grid, tile));
            }
            else
            {
                _entManager.SpawnEntity(gen.PartitionWall, _maps.GridTileToLocal(_gridUid, _grid, tile));
            }
        }
    }

    /// <summary>
    /// Places a horizontal wall segment at Y = <paramref name="wallY"/> from
    /// <paramref name="xMin"/> to <paramref name="xMax"/> with a door near <paramref name="doorX"/>.
    /// </summary>
    private void PlaceInteriorWallSegment(
        ShipInteriorDunGen gen,
        Dungeon dungeon,
        HashSet<Vector2i> usedTiles,
        int wallY,
        int xMin, int xMax,
        int doorX)
    {
        var wallTiles = new List<Vector2i>();
        for (var x = xMin; x <= xMax; x++)
        {
            var pos = new Vector2i(x, wallY);
            if (!dungeon.RoomTiles.Contains(pos) || usedTiles.Contains(pos))
                continue;
            wallTiles.Add(pos);
        }

        if (wallTiles.Count == 0)
            return;

        // Find door position closest to doorX.
        wallTiles.Sort((a, b) => Math.Abs(a.X - doorX).CompareTo(Math.Abs(b.X - doorX)));
        var doorTile = wallTiles[0];

        foreach (var tile in wallTiles)
        {
            usedTiles.Add(tile);
            if (tile == doorTile)
                _entManager.SpawnEntity(gen.PartitionDoor, _maps.GridTileToLocal(_gridUid, _grid, tile));
            else
                _entManager.SpawnEntity(gen.PartitionWall, _maps.GridTileToLocal(_gridUid, _grid, tile));
        }
    }
}

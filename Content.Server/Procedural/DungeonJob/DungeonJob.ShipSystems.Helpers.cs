using System.Numerics;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonLayers;
using Robust.Shared.Map;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// Reference points computed from the actual tile extents of the ship.
    /// Used by ShipSystems to distribute equipment across different areas
    /// instead of anchoring everything to the geometric center.
    /// </summary>
    private readonly struct ShipZones
    {
        public readonly Vector2i Center;
        public readonly Vector2i Bow;           // High Y, center X  (bridge)
        public readonly Vector2i Stern;         // Low Y, center X   (engineering)
        public readonly Vector2i MidPort;       // Mid Y, low X
        public readonly Vector2i MidStarboard;  // Mid Y, high X

        public ShipZones(Dungeon dungeon)
        {
            var minX = int.MaxValue;
            var maxX = int.MinValue;
            var minY = int.MaxValue;
            var maxY = int.MinValue;
            foreach (var tile in dungeon.RoomTiles)
            {
                minX = Math.Min(minX, tile.X);
                maxX = Math.Max(maxX, tile.X);
                minY = Math.Min(minY, tile.Y);
                maxY = Math.Max(maxY, tile.Y);
            }

            var midX = (minX + maxX) / 2;
            var midY = (minY + maxY) / 2;
            Center = new Vector2i(midX, midY);
            Bow = new Vector2i(midX, maxY - 1);
            Stern = new Vector2i(midX, minY + 1);
            MidPort = new Vector2i(minX + 1, midY);
            MidStarboard = new Vector2i(maxX - 1, midY);
        }
    }
    /// <summary>
    /// Collects free room tiles sorted by Y then centerline distance.
    /// Stern tiles come first, bow tiles last - <see cref="PlaceDistributed"/>
    /// uses this ordering to create even Y-zone bands across interior rooms.
    /// </summary>
    private List<Vector2i> CollectFreeTiles(Dungeon dungeon, HashSet<Vector2i> reservedTiles, int centerX)
    {
        var freeTiles = new List<Vector2i>();
        foreach (var tile in dungeon.RoomTiles)
        {
            if (reservedTiles.Contains(tile))
                continue;

            if (!_anchorable.TileFree(_grid, tile, DungeonSystem.CollisionLayer, DungeonSystem.CollisionMask))
                continue;

            freeTiles.Add(tile);
        }

        // Sort by Y (stern → bow), ties broken by distance from centerline.
        // This gives PlaceDistributed natural Y-zone bands spanning the ship.
        freeTiles.Sort((a, b) =>
        {
            var cmp = a.Y.CompareTo(b.Y);
            if (cmp != 0) return cmp;
            return Math.Abs(a.X - centerX).CompareTo(Math.Abs(b.X - centerX));
        });

        return freeTiles;
    }

    /// <summary>
    /// Find the best tile from freeTiles that isn't in usedTiles, scored by the given function.
    /// Higher score = better.
    /// </summary>
    private Vector2i? FindBestTile(List<Vector2i> freeTiles, HashSet<Vector2i> usedTiles, Func<Vector2i, float> scorer)
    {
        Vector2i? best = null;
        var bestScore = float.MinValue;

        foreach (var tile in freeTiles)
        {
            if (usedTiles.Contains(tile))
                continue;

            var score = scorer(tile);
            if (score > bestScore)
            {
                bestScore = score;
                best = tile;
            }
        }

        return best;
    }

    /// <summary>
    /// Find a free tile adjacent to the given position.
    /// </summary>
    private Vector2i? FindAdjacentFree(Vector2i origin, List<Vector2i> freeTiles, HashSet<Vector2i> usedTiles, Dungeon dungeon)
    {
        var freeTileSet = new HashSet<Vector2i>(freeTiles);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                // Cardinal only.
                if (dx != 0 && dy != 0)
                    continue;

                var neighbor = new Vector2i(origin.X + dx, origin.Y + dy);
                if (usedTiles.Contains(neighbor))
                    continue;

                if (!freeTileSet.Contains(neighbor))
                    continue;

                return neighbor;
            }
        }

        return null;
    }

    /// <summary>
    /// Place a wall-mounted entity on an interior tile adjacent to a wall, rotated to face the wall.
    /// Searches outward from the preferred position.
    /// </summary>
    private Vector2i? PlaceOnWall(
        string proto,
        Vector2i preferred,
        Vector2i center,
        Dungeon dungeon,
        HashSet<Vector2i> usedTiles)
    {
        // Cardinal directions to check for an adjacent wall.
        var cardinals = new (int Dx, int Dy, Direction Dir)[]
        {
            (0, 1, Direction.North),
            (0, -1, Direction.South),
            (1, 0, Direction.East),
            (-1, 0, Direction.West),
        };

        // Sort interior tiles by distance to preferred position.
        var candidates = new List<Vector2i>(dungeon.RoomTiles);
        candidates.Sort((a, b) =>
        {
            var da = (a - preferred).LengthSquared;
            var db = (b - preferred).LengthSquared;
            return da.CompareTo(db);
        });

        foreach (var tile in candidates)
        {
            if (usedTiles.Contains(tile))
                continue;

            foreach (var (dx, dy, dir) in cardinals)
            {
                var neighbor = new Vector2i(tile.X + dx, tile.Y + dy);

                // The neighbor must be a wall (exterior tile, not interior).
                if (dungeon.RoomTiles.Contains(neighbor))
                    continue;

                if (!dungeon.RoomExteriorTiles.Contains(neighbor))
                    continue;

                // Don't stack multiple wall-mounted entities on the same wall tile.
                if (usedTiles.Contains(neighbor))
                    continue;

                // Place on the wall tile itself, rotated to face inward (toward center).
                usedTiles.Add(tile);
                usedTiles.Add(neighbor);
                var ent = _entManager.SpawnEntity(proto, _maps.GridTileToLocal(_gridUid, _grid, neighbor));
                RotateTowardCenter(ent, neighbor, center);
                return neighbor;
            }
        }
        return null;
    }

    private List<Vector2i> PlaceDistributed(
        string proto,
        int count,
        List<Vector2i> freeTiles,
        HashSet<Vector2i> usedTiles,
        Dungeon dungeon,
        Random random,
        Vector2i center)
    {
        // Divide freeTiles into `count` equal bands and pick one tile per band.
        // freeTiles is sorted by Y (stern→bow) so bands span the ship length,
        // giving even spatial spread across interior rooms.
        var placed = new List<Vector2i>();
        if (freeTiles.Count == 0 || count <= 0)
            return placed;

        var step = Math.Max(1, freeTiles.Count / (count + 1));

        for (var band = 1; band <= count && placed.Count < count; band++)
        {
            var idealIdx = band * step;

            // Scan forward within the band (up to one full step) for a free tile.
            var end = Math.Min(freeTiles.Count, idealIdx + step);
            for (var i = idealIdx; i < end; i++)
            {
                var tile = freeTiles[i];
                if (usedTiles.Contains(tile) || !dungeon.RoomTiles.Contains(tile))
                    continue;

                usedTiles.Add(tile);
                var ent = _entManager.SpawnEntity(proto, _maps.GridTileToLocal(_gridUid, _grid, tile));
                RotateTowardCenter(ent, tile, center);
                placed.Add(tile);
                break;
            }
        }

        return placed;
    }

    /// <summary>
    /// Rotates an entity to face toward the ship center.
    /// </summary>
    private void RotateTowardCenter(EntityUid ent, Vector2i tile, Vector2i center)
    {
        var dx = center.X - tile.X;
        var dy = center.Y - tile.Y;
        if (dx == 0 && dy == 0)
            return;

        // Snap to the nearest cardinal direction (N/S/E/W).
        Angle angle;
        if (Math.Abs(dx) >= Math.Abs(dy))
            angle = dx > 0 ? Angle.Zero : new Angle(Math.PI);      // East or West
        else
            angle = dy > 0 ? new Angle(Math.PI / 2) : new Angle(-Math.PI / 2); // North or South

        _transform.SetLocalRotation(ent, angle);
    }

    /// <summary>
    /// Deletes all wall entities anchored at the given tile position.
    /// Used to clear walls placed by BoundaryWallDunGen before placing docks
    /// </summary>
    private void DeleteWallsAt(Vector2i tile)
    {
        var toDelete = new List<EntityUid>();
        var anchored = _maps.GetAnchoredEntitiesEnumerator(_gridUid, _grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (_tags.HasTag(uid.Value, WallTag))
                toDelete.Add(uid.Value);
        }
        foreach (var uid in toDelete)
            _entManager.DeleteEntity(uid);
    }

    /// <summary>
    /// Returns the four cardinal neighbours of a tile.
    /// </summary>
    private static IEnumerable<Vector2i> GetCardinalNeighbors(Vector2i tile)
    {
        yield return new Vector2i(tile.X,     tile.Y + 1);
        yield return new Vector2i(tile.X,     tile.Y - 1);
        yield return new Vector2i(tile.X + 1, tile.Y);
        yield return new Vector2i(tile.X - 1, tile.Y);
    }

    /// <summary>
    /// Returns the tile in <paramref name="roomTiles"/> with smallest Manhattan distance to
    /// <paramref name="target"/>. Used for endpoint snapping in BFS helpers.
    /// </summary>
    private static Vector2i NearestTile(Vector2i target, HashSet<Vector2i> roomTiles)
    {
        var best = default(Vector2i);
        var bestDist = int.MaxValue;
        foreach (var tile in roomTiles)
        {
            var dist = Math.Abs(tile.X - target.X) + Math.Abs(tile.Y - target.Y);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = tile;
            }
        }
        return best;
    }

    /// <summary>
    /// A* shortest path from <paramref name="from"/> to <paramref name="to"/> through tiles in
    /// <paramref name="roomTiles"/>. Endpoints outside the set are snapped to the nearest in-set tile.
    /// Returns null if unreachable.
    /// </summary>
    private static List<Vector2i>? FindPathBFS(Vector2i from, Vector2i to, HashSet<Vector2i> roomTiles)
    {
        if (roomTiles.Count == 0)
            return null;

        if (!roomTiles.Contains(from))
            from = NearestTile(from, roomTiles);
        if (!roomTiles.Contains(to))
            to = NearestTile(to, roomTiles);

        if (from == to)
            return new List<Vector2i> { from };

        var parent = new Dictionary<Vector2i, Vector2i> { [from] = from };
        var gScore = new Dictionary<Vector2i, int>       { [from] = 0 };
        var open   = new PriorityQueue<Vector2i, int>();
        open.Enqueue(from, ManhattanDistance(from, to));

        while (open.TryDequeue(out var current, out var priority))
        {
            var g = gScore[current];
            // Skip stale entries - a cheaper path to this tile was already processed.
            if (priority > g + ManhattanDistance(current, to))
                continue;

            if (current == to)
            {
                var path = new List<Vector2i>();
                var node = to;
                while (node != from)
                {
                    path.Add(node);
                    node = parent[node];
                }
                path.Add(from);
                path.Reverse();
                return path;
            }

            foreach (var nb in GetCardinalNeighbors(current))
            {
                if (!roomTiles.Contains(nb))
                    continue;
                var newG = g + 1;
                if (gScore.TryGetValue(nb, out var existingG) && newG >= existingG)
                    continue;
                parent[nb] = current;
                gScore[nb] = newG;
                open.Enqueue(nb, newG + ManhattanDistance(nb, to));
            }
        }

        return null;
    }

    private static int ManhattanDistance(Vector2i a, Vector2i b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    /// <summary>
    /// BFS spanning tree rooted at <paramref name="root"/> visiting every reachable tile in
    /// <paramref name="roomTiles"/> exactly once. Produces a DAG.
    /// </summary>
    private static HashSet<Vector2i> SpanningTreeBFS(Vector2i root, HashSet<Vector2i> roomTiles)
    {
        var visited = new HashSet<Vector2i>();
        if (roomTiles.Count == 0)
            return visited;

        if (!roomTiles.Contains(root))
            root = NearestTile(root, roomTiles);

        var queue = new Queue<Vector2i>();
        visited.Add(root);
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var nb in GetCardinalNeighbors(current))
            {
                if (!roomTiles.Contains(nb) || visited.Contains(nb))
                    continue;
                visited.Add(nb);
                queue.Enqueue(nb);
            }
        }

        return visited;
    }
}

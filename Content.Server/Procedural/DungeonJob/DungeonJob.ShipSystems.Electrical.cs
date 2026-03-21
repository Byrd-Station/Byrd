using System.Linq;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonLayers;
using Robust.Shared.Map;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// Lays HV, MV, and APC extension cables.
    /// HV: generator → SMES → substation.
    /// MV: substation → each APC.
    /// LV: each APC → its local devices (per-APC BFS tree = strict DAG, no loops).
    /// APCs are placed automatically -- one per Y-zone cluster of devices.
    /// </summary>
    private void PlaceCabling(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        ShipZones zones,
        Vector2i? cockpitTile,
        Vector2i? gyroTile,
        Vector2i? gravTile,
        List<Vector2i> ventTiles,
        List<Vector2i> scrubberTiles,
        Vector2i? sternTile,
        Vector2i? substationTile,
        Vector2i? smesTile,
        Vector2i? rechargerTile,
        HashSet<Vector2i> usedTiles)
    {
        // Cables can go under walls, so include both interior and exterior (wall) tiles.
        var cableWalkable = new HashSet<Vector2i>(dungeon.RoomTiles);
        cableWalkable.UnionWith(dungeon.RoomExteriorTiles);

        // Collect all powered devices.
        var allDevices = new List<Vector2i>();
        if (cockpitTile != null) allDevices.Add(cockpitTile.Value);
        if (gyroTile != null) allDevices.Add(gyroTile.Value);
        if (gravTile != null) allDevices.Add(gravTile.Value);
        if (rechargerTile != null) allDevices.Add(rechargerTile.Value);
        allDevices.AddRange(ventTiles);
        allDevices.AddRange(scrubberTiles);

        // --- HV cable: generator -> SMES -> substation ---
        var hvTiles = new HashSet<Vector2i>();
        if (sternTile != null && substationTile != null)
        {
            if (smesTile != null)
            {
                var hvPath1 = FindPathBFS(sternTile.Value, smesTile.Value, cableWalkable);
                if (hvPath1 != null) hvTiles.UnionWith(hvPath1);
                var hvPath2 = FindPathBFS(smesTile.Value, substationTile.Value, cableWalkable);
                if (hvPath2 != null) hvTiles.UnionWith(hvPath2);
            }
            else
            {
                var hvPath = FindPathBFS(sternTile.Value, substationTile.Value, cableWalkable);
                if (hvPath != null) hvTiles.UnionWith(hvPath);
            }
        }

        // --- Cluster devices into Y-zones and place one APC per cluster ---
        var apcTiles = new List<Vector2i>();
        var apcDeviceGroups = ClusterDevicesByY(allDevices, dungeon);

        foreach (var cluster in apcDeviceGroups)
        {
            if (cluster.Count == 0)
                continue;

            // Centroid of the cluster -- place APC on wall nearest to it.
            var cx = (int)cluster.Average(t => t.X);
            var cy = (int)cluster.Average(t => t.Y);
            var centroid = new Vector2i(cx, cy);

            var apcWall = PlaceOnWall(gen.Apc, centroid, zones.Center, dungeon, usedTiles);
            if (apcWall != null)
                apcTiles.Add(apcWall.Value);
        }

        // Fallback: if no APC was placed at all, force one near the substation.
        if (apcTiles.Count == 0)
        {
            var fallback = PlaceOnWall(gen.Apc, substationTile ?? sternTile ?? zones.Stern, zones.Center, dungeon, usedTiles);
            if (fallback != null)
                apcTiles.Add(fallback.Value);
        }

        // --- MV cable: substation → each APC (shortest path) ---
        var mvTiles = new HashSet<Vector2i>();
        if (substationTile != null)
        {
            foreach (var apc in apcTiles)
            {
                var mvPath = FindPathBFS(substationTile.Value, apc, cableWalkable);
                if (mvPath != null)
                    mvTiles.UnionWith(mvPath);
            }
        }

        // --- LV cable: per-APC BFS trees (strict DAG -- no loops) ---
        // Assign each device to the nearest APC, then build a BFS tree from
        // that APC visiting only its assigned devices.
        // A global visited set prevents different APC trees from sharing
        // intermediate tiles, which would merge the trees into a cyclic graph.
        var apcCableTiles = new HashSet<Vector2i>();
        if (apcTiles.Count > 0 && allDevices.Count > 0)
        {
            // Assign each device to nearest APC.
            var assignments = new Dictionary<Vector2i, List<Vector2i>>();
            foreach (var apc in apcTiles)
                assignments[apc] = new List<Vector2i>();

            foreach (var dev in allDevices)
            {
                var bestApc = apcTiles[0];
                var bestDist = ManhattanDistance(dev, bestApc);
                for (var i = 1; i < apcTiles.Count; i++)
                {
                    var d = ManhattanDistance(dev, apcTiles[i]);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestApc = apcTiles[i];
                    }
                }
                assignments[bestApc].Add(dev);
            }

            // Global visited set shared across all APC trees so their cable
            // paths never overlap (overlap = cycle in the LV network).
            var globalVisited = new HashSet<Vector2i>();

            // BFS tree from each APC to its devices.
            foreach (var (apcRoot, devices) in assignments)
            {
                if (devices.Count == 0)
                {
                    // APC still needs a cable on its own tile.
                    apcCableTiles.Add(apcRoot);
                    globalVisited.Add(apcRoot);
                    continue;
                }

                apcCableTiles.Add(apcRoot);
                globalVisited.Add(apcRoot);
                var devSet = new HashSet<Vector2i>(devices);
                var parent = new Dictionary<Vector2i, Vector2i> { [apcRoot] = apcRoot };
                var queue = new Queue<Vector2i>();
                queue.Enqueue(apcRoot);
                var found = new List<Vector2i>();

                while (queue.Count > 0 && devSet.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (devSet.Remove(current))
                        found.Add(current);

                    foreach (var nb in GetCardinalNeighbors(current))
                    {
                        // Block tiles already claimed by another APC tree.
                        if (!cableWalkable.Contains(nb) || parent.ContainsKey(nb) || globalVisited.Contains(nb))
                            continue;
                        parent[nb] = current;
                        queue.Enqueue(nb);
                    }
                }

                // Trace each found device back to the APC root and mark tiles globally.
                foreach (var dev in found)
                {
                    var node = dev;
                    while (node != apcRoot)
                    {
                        apcCableTiles.Add(node);
                        globalVisited.Add(node);
                        node = parent[node];
                    }
                }
            }
        }

        // --- Spawn cable entities ---
        foreach (var tile in hvTiles)
            _entManager.SpawnEntity(gen.CableHV, _maps.GridTileToLocal(_gridUid, _grid, tile));
        foreach (var tile in mvTiles)
            _entManager.SpawnEntity(gen.CableMV, _maps.GridTileToLocal(_gridUid, _grid, tile));
        foreach (var tile in apcCableTiles)
            _entManager.SpawnEntity(gen.CableApc, _maps.GridTileToLocal(_gridUid, _grid, tile));
    }

    /// <summary>
    /// Clusters device tiles into groups by Y-band (stern / mid / bow).
    /// Returns 1–3 clusters depending on how devices are distributed.
    /// </summary>
    private static List<List<Vector2i>> ClusterDevicesByY(List<Vector2i> devices, Dungeon dungeon)
    {
        if (devices.Count == 0)
            return new List<List<Vector2i>>();

        // Compute ship Y extents.
        var minY = int.MaxValue;
        var maxY = int.MinValue;
        foreach (var tile in dungeon.RoomTiles)
        {
            minY = Math.Min(minY, tile.Y);
            maxY = Math.Max(maxY, tile.Y);
        }

        var length = maxY - minY + 1;

        // For very small ships, one cluster is enough.
        if (length < 8 || devices.Count <= 3)
            return new List<List<Vector2i>> { new(devices) };

        // Split into thirds: stern / mid / bow.
        var thirdLen = length / 3;
        var sternMax = minY + thirdLen;
        var bowMin = maxY - thirdLen;

        var stern = new List<Vector2i>();
        var mid = new List<Vector2i>();
        var bow = new List<Vector2i>();

        foreach (var dev in devices)
        {
            if (dev.Y <= sternMax)
                stern.Add(dev);
            else if (dev.Y >= bowMin)
                bow.Add(dev);
            else
                mid.Add(dev);
        }

        var result = new List<List<Vector2i>>();
        if (stern.Count > 0) result.Add(stern);
        if (mid.Count > 0) result.Add(mid);
        if (bow.Count > 0) result.Add(bow);

        // If everything ended up in one cluster, just return it.
        if (result.Count == 0)
            result.Add(new List<Vector2i>(devices));

        return result;
    }
}

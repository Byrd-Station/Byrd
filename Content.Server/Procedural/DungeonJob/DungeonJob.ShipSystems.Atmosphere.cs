using System.Linq;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonLayers;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// Builds the supply and scrubber pipe networks and places an exterior exhaust vent.
    /// </summary>
    private void PlaceAtmPipes(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        Vector2i center,
        Vector2i? mixerTile,
        List<Vector2i> ventTiles,
        List<Vector2i> scrubberTiles,
        Random random)
    {
        var midX = center.X;
        var minY = int.MaxValue;
        var maxY = int.MinValue;
        foreach (var tile in dungeon.RoomTiles)
        {
            minY = Math.Min(minY, tile.Y);
            maxY = Math.Max(maxY, tile.Y);
        }

        // Supply network: spine at midX, branches from mixer and vents.
        var supplyTiles = new HashSet<Vector2i>();

        // Lay supply spine (only on interior tiles at midX).
        for (var y = minY; y <= maxY; y++)
        {
            var spinePos = new Vector2i(midX, y);
            if (dungeon.RoomTiles.Contains(spinePos))
                supplyTiles.Add(spinePos);
        }

        // Connect mixer to the supply spine.
        if (mixerTile != null)
        {
            var mixerPath = FindPathBFS(mixerTile.Value, NearestTile(new Vector2i(midX, mixerTile.Value.Y), supplyTiles.Count > 0 ? supplyTiles : dungeon.RoomTiles), dungeon.RoomTiles);
            if (mixerPath != null)
                supplyTiles.UnionWith(mixerPath);
        }

        // Branch each vent pump to the supply spine.
        foreach (var ventTile in ventTiles)
        {
            var target = NearestTile(new Vector2i(midX, ventTile.Y), supplyTiles.Count > 0 ? supplyTiles : dungeon.RoomTiles);
            var path = FindPathBFS(ventTile, target, dungeon.RoomTiles);
            if (path != null)
            {
                // Don't add the vent tile itself - the vent IS the pipe endpoint.
                foreach (var p in path)
                {
                    if (p != ventTile)
                        supplyTiles.Add(p);
                }
            }
        }

        // Scrubber network: spine offset from midX, branches from scrubbers.
        var scrubberSpineX = midX + gen.ScrubberSpineOffset;
        var scrubberPipeTiles = new HashSet<Vector2i>();
        var scrubberWalkable = new HashSet<Vector2i>(dungeon.RoomTiles);
        scrubberWalkable.ExceptWith(supplyTiles);
        foreach (var st in scrubberTiles)
            scrubberWalkable.Add(st);

        for (var y = minY; y <= maxY; y++)
        {
            var spinePos = new Vector2i(scrubberSpineX, y);
            if (scrubberWalkable.Contains(spinePos))
                scrubberPipeTiles.Add(spinePos);
        }

        foreach (var scrubberTile in scrubberTiles)
        {
            var target = NearestTile(new Vector2i(scrubberSpineX, scrubberTile.Y), scrubberPipeTiles.Count > 0 ? scrubberPipeTiles : scrubberWalkable);
            var path = FindPathBFS(scrubberTile, target, scrubberWalkable);
            if (path != null)
            {
                foreach (var p in path)
                {
                    if (!scrubberTiles.Contains(p))
                        scrubberPipeTiles.Add(p);
                }
            }
        }

        // Place supply pipes with correct fittings and rotations.
        PlacePipeNetwork(gen, supplyTiles, ventTiles, gen.PipeStraight, gen.PipeBend, gen.PipeTJunction, gen.PipeFourway);

        // Place scrubber pipes with correct fittings and rotations.
        PlacePipeNetwork(gen, scrubberPipeTiles, scrubberTiles.ToHashSet(), gen.ScrubberPipe, gen.PipeBend, gen.PipeTJunction, gen.PipeFourway);

        // Rotate vent pumps and scrubbers to face their nearest pipe neighbor.
        RotateDevicesTowardPipe(ventTiles, supplyTiles, center);
        RotateDevicesTowardPipe(scrubberTiles, scrubberPipeTiles, center);

        // Place passive vent on exterior lattice to exhaust the scrubber network.
        PlaceScrubberExhaustVent(gen, dungeon, scrubberPipeTiles, scrubberSpineX, minY, maxY, random);
    }

    /// <summary>
    /// Finds a hull wall tile adjacent to an interior tile on the scrubber spine column, punches a
    /// lattice tile outside it, and places a passive vent there to vent the scrubber network to space.
    /// </summary>
    private void PlaceScrubberExhaustVent(
        ShipSystemsDunGen gen,
        Dungeon dungeon,
        HashSet<Vector2i> scrubberPipeTiles,
        int scrubberSpineX,
        int minY,
        int maxY,
        Random random)
    {
        var tryDirs = new[] { 1, -1 };

        foreach (var stepX in tryDirs)
        {
            for (var y = minY; y <= maxY; y++)
            {
                var spinePos = new Vector2i(scrubberSpineX, y);
                if (!dungeon.RoomTiles.Contains(spinePos))
                    continue;

                // Walk outward until we leave interior tiles.
                var current = spinePos;
                var pathTiles = new List<Vector2i>();
                Vector2i? exitTile = null;

                while (true)
                {
                    var next = new Vector2i(current.X + stepX, current.Y);
                    if (dungeon.RoomTiles.Contains(next))
                    {
                        pathTiles.Add(next);
                        current = next;
                        continue;
                    }

                    exitTile = next;
                    break;
                }

                if (exitTile == null)
                    continue;

                var ventExteriorTile = new Vector2i(exitTile.Value.X + stepX, exitTile.Value.Y);

                // Place lattice tile for the passive vent.
                var latticeDef = (ContentTileDefinition) _tileDefManager[gen.LatticeTile];
                _maps.SetTile(_gridUid, _grid, ventExteriorTile, _tile.GetVariantTile(latticeDef, random));

                // Add interior path tiles + exit tile to scrubber network for pipe placement.
                foreach (var pt in pathTiles)
                    scrubberPipeTiles.Add(pt);
                scrubberPipeTiles.Add(exitTile.Value);

                // Place pipe at exit tile - oriented horizontally (E-W).
                var exitEnt = _entManager.SpawnEntity(gen.PipeStraight, _maps.GridTileToLocal(_gridUid, _grid, exitTile.Value));
                _transform.SetLocalRotation(exitEnt, new Angle(Math.PI / 2)); // E-W

                // Place passive vent on the exterior tile, rotated to face inward (toward ship).
                var ventEnt = _entManager.SpawnEntity(gen.PassiveVent, _maps.GridTileToLocal(_gridUid, _grid, ventExteriorTile));
                // PassiveVent base direction = South. Rotate so it faces inward.
                // stepX=+1 means vent is East of ship → face West (3π/2).
                // stepX=-1 means vent is West of ship → face East (π/2).
                var ventAngle = stepX > 0 ? new Angle(3 * Math.PI / 2) : new Angle(Math.PI / 2);
                _transform.SetLocalRotation(ventEnt, ventAngle);
                return;
            }
        }
    }

    /// <summary>
    /// Places pipe entities on a set of tiles with correct fittings (straight, bend, T-junction,
    /// fourway) and rotations based on neighbor connectivity.
    /// </summary>
    private void PlacePipeNetwork(
        ShipSystemsDunGen gen,
        HashSet<Vector2i> networkTiles,
        IReadOnlyCollection<Vector2i> deviceTiles,
        string straightProto,
        string bendProto,
        string tJunctionProto,
        string fourwayProto)
    {
        // Connection flag bits.
        const int flagN = 1, flagS = 2, flagW = 4, flagE = 8;

        // Combined set for neighbor lookups (network tiles + device tiles).
        var allConnectable = new HashSet<Vector2i>(networkTiles);
        foreach (var d in deviceTiles)
            allConnectable.Add(d);

        foreach (var tile in networkTiles)
        {
            var flags = 0;
            if (allConnectable.Contains(new Vector2i(tile.X, tile.Y + 1))) flags |= flagN;
            if (allConnectable.Contains(new Vector2i(tile.X, tile.Y - 1))) flags |= flagS;
            if (allConnectable.Contains(new Vector2i(tile.X - 1, tile.Y))) flags |= flagW;
            if (allConnectable.Contains(new Vector2i(tile.X + 1, tile.Y))) flags |= flagE;

            if (flags == 0)
                continue;

            string proto;
            Angle rotation;

            switch (flags)
            {
                // Straights (including dead-ends).
                case flagN | flagS:
                case flagN:
                case flagS:
                    proto = straightProto;
                    rotation = Angle.Zero; // N-S
                    break;
                case flagE | flagW:
                case flagE:
                case flagW:
                    proto = straightProto;
                    rotation = new Angle(Math.PI / 2); // E-W
                    break;

                // Bends.
                case flagS | flagW:
                    proto = bendProto;
                    rotation = Angle.Zero;
                    break;
                case flagS | flagE:
                    proto = bendProto;
                    rotation = new Angle(Math.PI / 2);
                    break;
                case flagN | flagE:
                    proto = bendProto;
                    rotation = new Angle(Math.PI);
                    break;
                case flagN | flagW:
                    proto = bendProto;
                    rotation = new Angle(3 * Math.PI / 2);
                    break;

                // T-junctions.
                case flagS | flagW | flagE:
                    proto = tJunctionProto;
                    rotation = Angle.Zero;
                    break;
                case flagN | flagS | flagE:
                    proto = tJunctionProto;
                    rotation = new Angle(Math.PI / 2);
                    break;
                case flagN | flagW | flagE:
                    proto = tJunctionProto;
                    rotation = new Angle(Math.PI);
                    break;
                case flagN | flagS | flagW:
                    proto = tJunctionProto;
                    rotation = new Angle(3 * Math.PI / 2);
                    break;

                // Fourway.
                case flagN | flagS | flagW | flagE:
                    proto = fourwayProto;
                    rotation = Angle.Zero;
                    break;

                default:
                    continue;
            }

            var ent = _entManager.SpawnEntity(proto, _maps.GridTileToLocal(_gridUid, _grid, tile));
            _transform.SetLocalRotation(ent, rotation);
        }
    }

    /// <summary>
    /// Rotates device entities (vents/scrubbers) to face their nearest pipe network neighbor.
    /// </summary>
    private void RotateDevicesTowardPipe(List<Vector2i> devicePositions, HashSet<Vector2i> pipeTiles, Vector2i center)
    {
        foreach (var devTile in devicePositions)
        {
            // Find the cardinal direction toward the nearest pipe neighbor.
            Direction? bestDir = null;
            foreach (var (dir, offset) in new[]
            {
                (Direction.North, new Vector2i(0, 1)),
                (Direction.South, new Vector2i(0, -1)),
                (Direction.East, new Vector2i(1, 0)),
                (Direction.West, new Vector2i(-1, 0)),
            })
            {
                if (pipeTiles.Contains(devTile + offset))
                {
                    bestDir = dir;
                    break;
                }
            }

            if (bestDir == null)
                continue;

            Angle angle = bestDir.Value switch
            {
                Direction.South => Angle.Zero,
                Direction.East  => new Angle(Math.PI / 2),
                Direction.North => new Angle(Math.PI),
                Direction.West  => new Angle(3 * Math.PI / 2),
                _ => Angle.Zero,
            };

            // Find and rotate the entity at devTile.
            var anchored = _maps.GetAnchoredEntitiesEnumerator(_gridUid, _grid, devTile);
            while (anchored.MoveNext(out var uid))
            {
                _transform.SetLocalRotation(uid.Value, angle);
            }
        }
    }
}

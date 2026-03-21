using System.Numerics;
using System.Threading.Tasks;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonGenerators;
using Robust.Shared.Map;
using Robust.Shared.Noise;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// <see cref="ShipHullDunGen"/>
    /// </summary>
    private async Task<Dungeon> GenerateShipHullDunGen(
        Vector2i position,
        ShipHullDunGen gen,
        HashSet<Vector2i> reservedTiles,
        int seed,
        Random random)
    {
        var interiorTiles = new HashSet<Vector2i>();
        var accentTiles = new HashSet<Vector2i>();

        var halfLen = gen.HalfLength;
        var halfWid = gen.HalfWidth;

        // Auto-create edge noise when only amplitude was configured (YAML configs
        // set edgeNoiseAmplitude but don't supply an explicit FastNoiseLite object).
        if (gen.EdgeNoise == null && gen.EdgeNoiseAmplitude > 0f)
        {
            gen.EdgeNoise = new FastNoiseLite(seed);
            gen.EdgeNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
            gen.EdgeNoise.SetFrequency(0.15f);
        }

        // --- Main hull body ---
        switch (gen.Profile)
        {
            case ShipHullProfile.Box:
                GenerateBoxHull(position, gen, interiorTiles, seed, halfLen, halfWid);
                break;
            case ShipHullProfile.Diamond:
                GenerateDiamondHull(position, gen, interiorTiles, seed, halfLen, halfWid);
                break;
            case ShipHullProfile.Asymmetric:
                GenerateAsymmetricHull(position, gen, interiorTiles, seed, halfLen, halfWid);
                break;
            default: // Ellipse
                GenerateEllipseHull(position, gen, interiorTiles, seed, halfLen, halfWid);
                break;
        }

        // Remove any tiles that land on reserved spots.
        interiorTiles.ExceptWith(reservedTiles);

        // --- Engine block protrusion ---
        if (gen.EngineBlockWidth > 0 && gen.EngineBlockLength > 0)
        {
            var ebHalfW = gen.EngineBlockWidth / 2;
            for (var y = -halfLen - gen.EngineBlockLength; y < -halfLen; y++)
            {
                for (var x = -ebHalfW; x <= ebHalfW; x++)
                {
                    var tile = new Vector2i(position.X + x, position.Y + y);
                    if (!reservedTiles.Contains(tile))
                    {
                        interiorTiles.Add(tile);
                        accentTiles.Add(tile);
                    }
                }
            }
        }

        // --- Side pods ---
        if (gen.SidePodWidth > 0 && gen.SidePodLength > 0)
        {
            var podHalfLen = gen.SidePodLength / 2;
            for (var y = -podHalfLen; y <= podHalfLen; y++)
            {
                for (var px = 0; px < gen.SidePodWidth; px++)
                {
                    // Starboard pod
                    var starboard = new Vector2i(position.X + halfWid + 1 + px, position.Y + y);
                    if (!reservedTiles.Contains(starboard))
                    {
                        interiorTiles.Add(starboard);
                        accentTiles.Add(starboard);
                    }

                    // Port pod
                    var port = new Vector2i(position.X - halfWid - 1 - px, position.Y + y);
                    if (!reservedTiles.Contains(port))
                    {
                        interiorTiles.Add(port);
                        accentTiles.Add(port);
                    }
                }
            }
        }

        // --- Compute exterior ring ---
        var exteriorTiles = new HashSet<Vector2i>();
        foreach (var tile in interiorTiles)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    var neighbor = new Vector2i(tile.X + dx, tile.Y + dy);
                    if (!interiorTiles.Contains(neighbor) && !reservedTiles.Contains(neighbor))
                        exteriorTiles.Add(neighbor);
                }
            }
        }

        // --- Place tiles ---
        var floorTileDef = _prototype.Index(gen.FloorTile);
        var hullTileDef = _prototype.Index(gen.HullTile);
        var accentFloorDef = gen.AccentFloorTile != null ? _prototype.Index(gen.AccentFloorTile.Value) : floorTileDef;

        var tileChanges = new List<(Vector2i Index, Tile Tile)>(interiorTiles.Count + exteriorTiles.Count);

        foreach (var tile in interiorTiles)
        {
            var def = accentTiles.Contains(tile) ? accentFloorDef : floorTileDef;
            tileChanges.Add((tile, _tile.GetVariantTile(def, random)));
        }

        foreach (var tile in exteriorTiles)
        {
            tileChanges.Add((tile, _tile.GetVariantTile(hullTileDef, random)));
        }

        _maps.SetTiles(_gridUid, _grid, tileChanges);

        await SuspendDungeon();
        if (!ValidateResume())
            return Dungeon.Empty;

        // Compute bounding box including appendages.
        var minX = int.MaxValue; var maxX = int.MinValue;
        var minY = int.MaxValue; var maxY = int.MinValue;
        foreach (var tile in interiorTiles)
        {
            minX = Math.Min(minX, tile.X); maxX = Math.Max(maxX, tile.X);
            minY = Math.Min(minY, tile.Y); maxY = Math.Max(maxY, tile.Y);
        }

        var center = new Vector2(position.X, position.Y);
        var bounds = new Box2i(minX - 1, minY - 1, maxX + 1, maxY + 1);

        var room = new DungeonRoom(interiorTiles, center, bounds, exteriorTiles);
        var dungeon = new Dungeon(new List<DungeonRoom> { room });
        dungeon.RefreshAllTiles();

        return dungeon;
    }

    // ---- Profile generators ----

    private void GenerateEllipseHull(
        Vector2i position, ShipHullDunGen gen,
        HashSet<Vector2i> interiorTiles, int seed,
        int halfLen, int halfWid)
    {
        for (var y = -halfLen; y <= halfLen; y++)
        {
            float taper;
            if (y > 0)
            {
                var t = (float) y / halfLen;
                taper = 1f - gen.BowTaper * t * t;
            }
            else if (y < 0)
            {
                var t = (float) -y / halfLen;
                taper = 1f - gen.SternTaper * t * t;
            }
            else
            {
                taper = 1f;
            }

            var effectiveWidth = halfWid * taper;
            // Apply asymmetry: shift the centre of the ellipse cross-section.
            var centerShift = gen.Asymmetry * halfWid * (1f - Math.Abs((float) y / halfLen));

            for (var x = -halfWid; x <= halfWid; x++)
            {
                if (effectiveWidth <= 0.1f)
                    continue;

                var shiftedX = x - centerShift;
                var normalizedDist = (float) (shiftedX * shiftedX) / (effectiveWidth * effectiveWidth);
                if (normalizedDist > 1f)
                    continue;

                var edgeDist = 1f - normalizedDist;
                if (gen.EdgeNoise != null && edgeDist < 0.3f)
                {
                    var noiseVal = gen.EdgeNoise.GetNoise(x + seed, y + seed);
                    if (edgeDist + noiseVal * gen.EdgeNoiseAmplitude * 0.1f < 0f)
                        continue;
                }

                interiorTiles.Add(new Vector2i(position.X + x, position.Y + y));
            }
        }
    }

    private void GenerateBoxHull(
        Vector2i position, ShipHullDunGen gen,
        HashSet<Vector2i> interiorTiles, int seed,
        int halfLen, int halfWid)
    {
        var cr = Math.Max(0, gen.CornerRadius);

        for (var y = -halfLen; y <= halfLen; y++)
        {
            // Apply bow/stern taper to narrow the box ends.
            float taper = 1f;
            if (y > 0)
            {
                var t = (float) y / halfLen;
                taper = 1f - gen.BowTaper * t * t * 0.5f; // gentler taper for box
            }
            else if (y < 0)
            {
                var t = (float) -y / halfLen;
                taper = 1f - gen.SternTaper * t * t * 0.5f;
            }

            var effectiveWidth = (int)(halfWid * taper);

            for (var x = -effectiveWidth; x <= effectiveWidth; x++)
            {
                // Round corners: check distance to the nearest corner.
                if (cr > 0)
                {
                    var distFromCorner = IsInRoundedCorner(x, y, effectiveWidth, halfLen, cr);
                    if (distFromCorner)
                        continue;
                }

                // Edge noise for organic boundary.
                var edgeDistX = effectiveWidth > 0 ? 1f - (float)Math.Abs(x) / effectiveWidth : 0f;
                var edgeDistY = halfLen > 0 ? 1f - (float)Math.Abs(y) / halfLen : 0f;
                var edgeDist = Math.Min(edgeDistX, edgeDistY);

                if (gen.EdgeNoise != null && edgeDist < 0.15f)
                {
                    var noiseVal = gen.EdgeNoise.GetNoise(x + seed, y + seed);
                    if (edgeDist + noiseVal * gen.EdgeNoiseAmplitude * 0.05f < 0f)
                        continue;
                }

                interiorTiles.Add(new Vector2i(position.X + x, position.Y + y));
            }
        }
    }

    private void GenerateDiamondHull(
        Vector2i position, ShipHullDunGen gen,
        HashSet<Vector2i> interiorTiles, int seed,
        int halfLen, int halfWid)
    {
        for (var y = -halfLen; y <= halfLen; y++)
        {
            // Diamond: width linearly decreases from center to tips.
            float taper;
            if (y > 0)
            {
                var t = (float) y / halfLen;
                taper = 1f - t; // linear shrink toward bow
                taper *= (1f - gen.BowTaper * t); // additional bow sharpening
            }
            else if (y < 0)
            {
                var t = (float) -y / halfLen;
                taper = 1f - t;
                taper *= (1f - gen.SternTaper * t);
            }
            else
            {
                taper = 1f;
            }

            var effectiveWidth = (int)Math.Ceiling(halfWid * taper);

            for (var x = -effectiveWidth; x <= effectiveWidth; x++)
            {
                if (gen.EdgeNoise != null)
                {
                    var edgeDist = effectiveWidth > 0 ? 1f - (float)Math.Abs(x) / (effectiveWidth + 1) : 0f;
                    if (edgeDist < 0.2f)
                    {
                        var noiseVal = gen.EdgeNoise.GetNoise(x + seed, y + seed);
                        if (edgeDist + noiseVal * gen.EdgeNoiseAmplitude * 0.08f < 0f)
                            continue;
                    }
                }

                interiorTiles.Add(new Vector2i(position.X + x, position.Y + y));
            }
        }
    }

    private void GenerateAsymmetricHull(
        Vector2i position, ShipHullDunGen gen,
        HashSet<Vector2i> interiorTiles, int seed,
        int halfLen, int halfWid)
    {
        // Asymmetric: use ellipse but with different port/starboard half-widths.
        var asym = Math.Clamp(gen.Asymmetry, -0.6f, 0.6f);
        var portHalfWid = halfWid * (1f - asym);
        var starboardHalfWid = halfWid * (1f + asym);

        for (var y = -halfLen; y <= halfLen; y++)
        {
            float taper;
            if (y > 0)
            {
                var t = (float) y / halfLen;
                taper = 1f - gen.BowTaper * t * t;
            }
            else if (y < 0)
            {
                var t = (float) -y / halfLen;
                taper = 1f - gen.SternTaper * t * t;
            }
            else
            {
                taper = 1f;
            }

            var effPort = portHalfWid * taper;
            var effStarboard = starboardHalfWid * taper;

            for (var x = -(int)Math.Ceiling(effPort); x <= (int)Math.Ceiling(effStarboard); x++)
            {
                float normalizedDist;
                if (x < 0)
                    normalizedDist = effPort > 0.1f ? (float)(x * x) / (effPort * effPort) : 2f;
                else
                    normalizedDist = effStarboard > 0.1f ? (float)(x * x) / (effStarboard * effStarboard) : 2f;

                if (normalizedDist > 1f)
                    continue;

                var edgeDist = 1f - normalizedDist;
                if (gen.EdgeNoise != null && edgeDist < 0.3f)
                {
                    var noiseVal = gen.EdgeNoise.GetNoise(x + seed, y + seed);
                    if (edgeDist + noiseVal * gen.EdgeNoiseAmplitude * 0.1f < 0f)
                        continue;
                }

                interiorTiles.Add(new Vector2i(position.X + x, position.Y + y));
            }
        }
    }

    /// <summary>
    /// Returns true if the tile (x,y) falls inside a rounded corner cutout.
    /// </summary>
    private static bool IsInRoundedCorner(int x, int y, int halfWid, int halfLen, int radius)
    {
        // Only check tiles near the four corners.
        var cornerX = halfWid - radius;
        var cornerY = halfLen - radius;

        if (Math.Abs(x) <= cornerX || Math.Abs(y) <= cornerY)
            return false;

        // Distance from the corner circle centre.
        var dx = Math.Abs(x) - cornerX;
        var dy = Math.Abs(y) - cornerY;
        return dx * dx + dy * dy > radius * radius;
    }
}

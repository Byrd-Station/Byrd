using System.Numerics;
using System.Threading.Tasks;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonLayers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// <see cref="DepartmentAnchorsDunGen"/>
    /// </summary>
    private async Task PostGen(DepartmentAnchorsDunGen gen, Dungeon dungeon, HashSet<Vector2i> reservedTiles, Random random)
    {
        if (dungeon.Rooms.Count == 0)
            return;

        var room = dungeon.Rooms[0];
        var center = room.Center.Floored();

        var freeTiles = new List<Vector2i>();
        foreach (var tile in dungeon.RoomTiles)
        {
            if (reservedTiles.Contains(tile))
                continue;

            if (!_anchorable.TileFree(_grid, tile, DungeonSystem.CollisionLayer, DungeonSystem.CollisionMask))
                continue;

            freeTiles.Add(tile);
        }

        if (freeTiles.Count == 0)
            return;

        freeTiles.Sort((a, b) =>
        {
            var da = (a - center).LengthSquared;
            var db = (b - center).LengthSquared;
            return da.CompareTo(db);
        });

        var usedTiles = new HashSet<Vector2i>();

        // Bow = north (+Y), stern = south (-Y), matching the hull's Y-length orientation.
        // Port = west (-X), starboard = east (+X).
        PlaceAnchorsInSection(gen.BowAnchors, freeTiles, usedTiles, dungeon,
            t => t.Y * 10 - Math.Abs(t.X - center.X));
        PlaceAnchorsInSection(gen.PortBowAnchors, freeTiles, usedTiles, dungeon,
            t => t.Y * 6 + (center.X - t.X) * 6);
        PlaceAnchorsInSection(gen.StarboardBowAnchors, freeTiles, usedTiles, dungeon,
            t => t.Y * 6 + (t.X - center.X) * 6);
        PlaceAnchorsInSection(gen.MidAnchors, freeTiles, usedTiles, dungeon,
            t => -(t - center).LengthSquared);
        PlaceAnchorsInSection(gen.PortSternAnchors, freeTiles, usedTiles, dungeon,
            t => -t.Y * 6 + (center.X - t.X) * 6);
        PlaceAnchorsInSection(gen.StarboardSternAnchors, freeTiles, usedTiles, dungeon,
            t => -t.Y * 6 + (t.X - center.X) * 6);
        PlaceAnchorsInSection(gen.SternAnchors, freeTiles, usedTiles, dungeon,
            t => -t.Y * 10 - Math.Abs(t.X - center.X));

        await SuspendDungeon();
    }

    private void PlaceAnchorsInSection(
        List<EntProtoId> anchors,
        List<Vector2i> freeTiles,
        HashSet<Vector2i> usedTiles,
        Dungeon dungeon,
        Func<Vector2i, float> scorer)
    {
        if (anchors.Count == 0)
            return;

        Vector2i? lastTile = null;

        foreach (var anchor in anchors)
        {
            Vector2i? tile = null;

            if (lastTile != null)
                tile = FindAdjacentFree(lastTile.Value, freeTiles, usedTiles, dungeon);

            tile ??= FindBestTile(freeTiles, usedTiles, scorer);
            if (tile == null)
                return;

            usedTiles.Add(tile.Value);
            lastTile = tile;
            _entManager.SpawnEntity(anchor, _maps.GridTileToLocal(_gridUid, _grid, tile.Value));
        }
    }
}

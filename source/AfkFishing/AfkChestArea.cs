using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace BetterStardewValley.AfkFishing;

internal enum AfkGround { Valid, Outside, Water, Building, Forbidden, Obstacle }
internal sealed record AfkArea(TileNode Center, IReadOnlyList<(Point Tile, AfkGround Ground)> Cells)
{
    // Full-area validity is descriptive only; one invalid cell never vetoes its neighbors.
    internal bool AllGroundValid => Cells.Count == 9 && Cells.All(p => p.Ground == AfkGround.Valid);
    internal AfkGround Reason => Cells.FirstOrDefault(p => p.Ground != AfkGround.Valid).Ground;
    internal bool Contains(Point p) => Math.Abs(p.X - Center.X) <= 1 && Math.Abs(p.Y - Center.Y) <= 1;
    internal bool Usable(Point p) => Contains(p) && Cells.Any(c => c.Tile == p && c.Ground == AfkGround.Valid);
}

// No remembered fishing-position radius and no map-wide chest search. This nine-cell
// snapshot is disposable; every transaction takes a fresh one AFTER obtaining the mutex.
internal static class AfkChestArea
{
    internal static AfkArea Inspect(GameLocation map, Point center, Farmer who)
    {
        var cells = new List<(Point, AfkGround)>(9);
        for (int y = center.Y - 1; y <= center.Y + 1; y++)
            for (int x = center.X - 1; x <= center.X + 1; x++)
                cells.Add((new(x, y), Ground(map, new(x, y), who)));
        return new(new(map.NameOrUniqueName, center.X, center.Y), cells);
    }
    internal static AfkGround Ground(GameLocation map, Point tile, Farmer who)
    {
        var v = new Vector2(tile.X, tile.Y);
        if (map.Map == null || !map.isTileOnMap(v)) return AfkGround.Outside;
        if (map.isWaterTile(tile.X, tile.Y)) return AfkGround.Water;
        if (map.getBuildingAt(v) != null || map.buildings.Any(b => b.intersects(new(tile.X * 64, tile.Y * 64, 64, 64))))
            return AfkGround.Building;
        if (Utility.isPlacementForbiddenHere(map) || !map.isTilePlaceable(v)) return AfkGround.Forbidden;
        // Only ordinary, usable player chests are ignored as object occupancy. Native
        // checks still decide floors, crops, walls, terrain, furniture and other objects.
        var mask = CollisionMask.All & ~CollisionMask.Farmers;
        if (map.objects.TryGetValue(v, out var obj) && AfkWorld.EligibleChest(obj)) mask &= ~CollisionMask.Objects;
        if (!map.CanItemBePlacedHere(v, collisionMask: mask)) return AfkGround.Obstacle;
        var box = new Rectangle(tile.X * 64, tile.Y * 64, 64, 64);
        if (map.farmers.Any(p => p != who && p.GetBoundingBox().Intersects(box))) return AfkGround.Obstacle;
        return AfkGround.Valid;
    }
    internal static bool Accessible(Chest chest, Farmer who, bool ownsLock = false)
    {
        var map = who.currentLocation;
        if (!AfkWorld.EligibleChest(chest) || map == null || chest.Location != map
            || !map.objects.TryGetValue(chest.TileLocation, out var actual) || !ReferenceEquals(actual, chest)
            || !chest.checkForAction(who, justCheckingForActivity: true)) return false;
        var mutex = chest.GetMutex();
        if (mutex.IsLocked() && !(ownsLock && mutex.IsLockHeld())) return false;
        var area = Inspect(map, who.TilePoint, who);
        return area.Usable(new((int)chest.TileLocation.X, (int)chest.TileLocation.Y));
    }
    internal static List<Chest> Candidates(Farmer who, Chest? preferred = null)
    {
        var map = who.currentLocation;
        var area = Inspect(map, who.TilePoint, who);
        return area.Cells.Where(p => p.Ground == AfkGround.Valid)
            .Select(p => map.getObjectAtTile(p.Tile.X, p.Tile.Y)).OfType<Chest>()
            .Where(c => Accessible(c, who))
            .OrderBy(c => ReferenceEquals(c, preferred) ? 0 : 1)
            .ThenBy(c => Vector2.DistanceSquared(c.TileLocation, who.Tile))
            .ThenBy(c => c.TileLocation.Y).ThenBy(c => c.TileLocation.X).ToList();
    }
}

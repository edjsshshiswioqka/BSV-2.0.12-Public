using System.Globalization;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using SObject = StardewValley.Object;

namespace BetterStardewValley.Orders;

// BSV-native anchor + native Object reservations; no custom XML save type or extra item ID.
internal static class TerminalEntity
{
    internal const string Prefix = OrderRules.Prefix + "Terminal2x2/";
    internal const string IdKey = Prefix + "Id", RoleKey = Prefix + "Role", XKey = Prefix + "X", YKey = Prefix + "Y", PendingKey = Prefix + "Pending";
    internal const int Width = 2, Depth = 1, WorldSize = 2;
    // Opaque extent of the 64x128 formal asset (fringe-free). Drawn at a whole-number
    // scale so every art pixel is the same 2x2 screen block, centred on the two ground
    // cells with the legs standing on the bottom edge of the base row.
    internal static readonly Rectangle WorldSource = new(9, 68, 48, 54);
    internal const int WorldScale = 2;
    internal static Vector2 DrawPosition(Vector2 anchor) => new(anchor.X * 64 + (Width * 64 - WorldSource.Width * WorldScale) / 2,
        (anchor.Y + Depth) * 64 - WorldSource.Height * WorldScale);
    internal static bool IsPart(SObject o) => OrderTerminal.IsTerminal(o) && o.modData.TryGetValue(RoleKey, out var role) && role == "part";
    internal static bool Pending(SObject o) => o.modData.ContainsKey(PendingKey);
    internal static string Id(SObject o) => o.modData.TryGetValue(IdKey, out var id) ? id : "";
    internal static IEnumerable<Vector2> Tiles(Vector2 anchor)
    {
        for (int x = 0; x < Width; x++) yield return anchor + new Vector2(x, 0);
    }
    internal static Rectangle CellBounds(Vector2 tile) => new((int)tile.X * 64, (int)tile.Y * 64, 64, 64);
    internal static Rectangle Bounds(Vector2 anchor) => new((int)anchor.X * 64, (int)anchor.Y * 64, Width * 64, Depth * 64);
    internal static Rectangle VisualBounds(Vector2 anchor) => new((int)anchor.X * 64, ((int)anchor.Y - 1) * 64, WorldSize * 64, WorldSize * 64);
    internal static bool FarmMap(GameLocation? location) => location is Farm && ReferenceEquals(location, Game1.getFarm());
    internal static bool HomeMap(GameLocation? location) => location is FarmHouse house && house.OwnerId != 0
        && (ReferenceEquals(house, Game1.getLocationFromName("FarmHouse")) || ReferenceEquals(house.GetRootLocation(), Game1.getFarm()));
    internal static bool Allowed(GameLocation? location, Farmer who) => SupportedMap(location);
    internal static bool SupportedMap(GameLocation? location) => location?.map?.GetLayer("Back") != null && location.map.GetLayer("Buildings") != null;
    internal static bool InReach(Farmer who, Vector2 anchor, bool single = false)
    {
        int edge = single ? 0 : Width - 1;
        return who.Tile.X >= anchor.X - 1 && who.Tile.X <= anchor.X + edge + 1
            && who.Tile.Y >= anchor.Y - 1 && who.Tile.Y <= anchor.Y + Depth;
    }
    internal static bool Resolve(GameLocation location, Vector2 tile, out SObject anchor)
    {
        anchor = null!;
        if (!location.objects.TryGetValue(tile, out var found)) return false;
        if (!OrderTerminal.IsTerminal(found) || found.TileLocation != tile) return false;
        if (!IsPart(found)) { anchor = found; return true; }
        if (!int.TryParse(found.modData.GetValueOrDefault(XKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(found.modData.GetValueOrDefault(YKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            || !location.objects.TryGetValue(new(x, y), out var root) || !OrderTerminal.IsTerminal(root) || IsPart(root)
            || Id(root).Length == 0 || Id(root) != Id(found) || !Tiles(root.TileLocation).Contains(tile)) return false;
        anchor = root; return true;
    }
    internal static bool Complete(GameLocation l, SObject anchor) => !IsPart(anchor) && !Pending(anchor) && Id(anchor).Length > 0
        && Tiles(anchor.TileLocation).All(t => l.objects.TryGetValue(t, out var physical)
            && Resolve(l, t, out var a) && ReferenceEquals(a, anchor)
            && physical.boundingBox.Value == (t == anchor.TileLocation ? Bounds(t) : CellBounds(t))
            && !physical.isTemporarilyInvisible && !physical.CanBeGrabbed);
    // Native collision queries must see the same footprint, not a stale serialized box.
    // A blocked migration keeps only its real, existing cells; it never claims a neighbor.
    internal static Rectangle CollisionBounds(SObject obj, Vector2 tile) => IsPart(obj) || Pending(obj) ? CellBounds(tile) : Bounds(tile);
    internal static bool CanUse(Farmer who, string location, int x, int y) => who.currentLocation?.NameOrUniqueName == location
        && Allowed(who.currentLocation, who) && Resolve(who.currentLocation, new(x, y), out var a)
        && InReach(who, a.TileLocation, Pending(a)) && (Pending(a) || Complete(who.currentLocation, a));

    internal static bool ClearTile(GameLocation location, Vector2 tile, string? ownId = null)
    {
        if (!location.isTileOnMap(tile) || !location.isTilePlaceable(tile) || !location.isTilePassable(tile)
            || location.isWaterTile((int)tile.X, (int)tile.Y)
            || location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Action", "Buildings") != null
            || location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "TouchAction", "Back") != null
            || EntranceTile(location, tile)
            || location.GetHoeDirtAtTile(tile)?.crop != null) return false;
        bool own = location.objects.TryGetValue(tile, out var current) && ownId != null
            && OrderTerminal.IsTerminal(current) && Id(current) == ownId;
        if (location.objects.ContainsKey(tile) && !own) return false;
        // Like native placement: grass, flooring and tilled soil without a crop may sit under the desk;
        // trees, saplings, bushes and other features may not.
        if (location.terrainFeatures.TryGetValue(tile, out var feature)
            && feature is not (StardewValley.TerrainFeatures.Grass or StardewValley.TerrainFeatures.Flooring or StardewValley.TerrainFeatures.HoeDirt)) return false;
        // Native rugs are passable. Inspect every overlapping piece so a rug cannot
        // hide a table/chair behind GetFurnitureAt's first result.
        if (location.furniture.Any(f => f.GetBoundingBox().Intersects(CellBounds(tile)) && !f.isPassable())) return false;
        var mask = CollisionMask.All & ~(CollisionMask.Flooring | CollisionMask.Furniture);
        // A terminal already standing here must not be judged by someone walking past it.
        if (own) mask &= ~(CollisionMask.Objects | CollisionMask.Farmers | CollisionMask.Characters);
        return !location.IsTileOccupiedBy(tile, mask, PassableIgnored);
    }
    // Native placement ignores passable things (grass tufts, flooring) but never people or objects.
    internal const CollisionMask PassableIgnored = CollisionMask.All & ~(CollisionMask.Objects | CollisionMask.Farmers | CollisionMask.Characters);
    // Placement has its own range; interaction and pickup retain InReach unchanged.
    internal static int PlacementDistance = 3;
    internal static bool PlacementReach(Farmer who, Vector2 anchor)
    {
        int range = Math.Clamp(PlacementDistance, 1, 64);
        // Distance to the nearest physical cell, independent of sprite height.
        float dx = Math.Max(Math.Max(anchor.X - who.Tile.X, who.Tile.X - anchor.X - Width + 1), 0);
        float dy = Math.Max(Math.Max(anchor.Y - who.Tile.Y, who.Tile.Y - anchor.Y - Depth + 1), 0);
        return Math.Max(dx, dy) <= range;
    }
    internal static bool PlacementLineClear(GameLocation location, Vector2 anchor, Farmer who)
    {
        // Supercover grid traversal: even exact diagonal corner crossings check both sides.
        var start = who.Tile;
        int x = (int)start.X, y = (int)start.Y, tx = Math.Clamp(x, (int)anchor.X, (int)anchor.X + Width - 1), ty = (int)anchor.Y;
        int nx = Math.Abs(tx - x), ny = Math.Abs(ty - y), sx = Math.Sign(tx - x), sy = Math.Sign(ty - y);
        bool Clear(int cx, int cy)
        {
            var tile = new Vector2(cx, cy);
            if (tile == start) return true; // the placing farmer isn't an intervening obstacle
            return location.isTileOnMap(tile) && location.isTilePassable(tile)
                && !location.isWaterTile(cx, cy)
                && !location.furniture.Any(f => f.GetBoundingBox().Intersects(CellBounds(tile)) && !f.isPassable())
                && !location.IsTileOccupiedBy(tile, CollisionMask.All & ~(CollisionMask.Flooring | CollisionMask.Furniture), PassableIgnored);
        }
        int ix = 0, iy = 0;
        while (ix < nx || iy < ny)
        {
            long decision = (1L + 2L * ix) * ny - (1L + 2L * iy) * nx;
            if (decision == 0)
            {
                if (!Clear(x + sx, y) || !Clear(x, y + sy)) return false;
                x += sx; y += sy; ix++; iy++;
            }
            else if (decision < 0) { x += sx; ix++; }
            else { y += sy; iy++; }
            if (!Clear(x, y)) return false;
        }
        return true;
    }
    internal static string? PlacementRejection(GameLocation location, Vector2 anchor, Farmer who)
    {
        if (!ReferenceEquals(who.currentLocation, location) || !Allowed(location, who)) return "map";
        if (!PlacementReach(who, anchor)) return "range";
        foreach (var tile in Tiles(anchor)) if (!ClearTile(location, tile)) return "blocked:" + tile;
        return PlacementLineClear(location, anchor, who) ? null : "line";
    }
    internal static bool CanPlace(GameLocation location, Vector2 anchor, Farmer who) => PlacementRejection(location, anchor, who) == null;
    private static bool EntranceTile(GameLocation location, Vector2 tile)
    {
        if (location.warps.Any(w => w.X == (int)tile.X && w.Y == (int)tile.Y)) return true;
        // Keep the approach square outside a building door free too.
        foreach (var building in location.buildings)
        {
            if (building.humanDoor.Value.X < 0) continue;
            var door = building.getPointForHumanDoor();
            if (tile.X == door.X && (tile.Y == door.Y || tile.Y == door.Y + 1)) return true;
        }
        // A warp arrival can differ from its exit square. Never cover a known
        // arrival, including interiors, or the player could spawn inside the base.
        bool arrival = false;
        Utility.ForEachLocation(other =>
        {
            arrival = other.warps.Any(w => (w.TargetName == location.NameOrUniqueName || w.TargetName == location.Name)
                && w.TargetX == (int)tile.X && w.TargetY == (int)tile.Y);
            return !arrival;
        }, includeInteriors: true, includeGenerated: true);
        return arrival;
    }
    internal static void Stamp(SObject obj, GameLocation location, Vector2 tile, Vector2 anchor, string id, bool part)
    {
        obj.TileLocation = tile; obj.Location = location; obj.Stack = 1; obj.Fragility = 0;
        obj.CanBeGrabbed = false; // Recovery goes through one host transaction, never a native per-cell grab.
        obj.isTemporarilyInvisible = false;
        obj.boundingBox.Value = part ? CellBounds(tile) : Bounds(tile);
        obj.modData[IdKey] = id; obj.modData[RoleKey] = part ? "part" : "anchor";
        obj.modData[XKey] = ((int)anchor.X).ToString(CultureInfo.InvariantCulture);
        obj.modData[YKey] = ((int)anchor.Y).ToString(CultureInfo.InvariantCulture);
    }
    internal static SObject NewPart(GameLocation l, Vector2 tile, SObject anchor)
    {
        var part = ItemRegistry.Create<SObject>("(BC)" + OrderRules.TerminalId);
        part.owner.Value = anchor.owner.Value;
        Stamp(part, l, tile, anchor.TileLocation, Id(anchor), true); return part;
    }
    internal static void Strip(Item item)
    {
        foreach (string key in item.modData.Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).ToArray()) item.modData.Remove(key);
    }
}

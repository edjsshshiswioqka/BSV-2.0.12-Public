using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;

namespace BetterStardewValley.AfkFishing;

internal sealed class AfkWorld
{
    private readonly Farmer Player;
    private readonly Dictionary<string, GameLocation?> Maps = new();
    private readonly Dictionary<TileNode, bool> Passable = new();
    private readonly Dictionary<TileNode, bool> Actors = new();
    private readonly Dictionary<string, List<WorldLink>> Graph = new();
    internal sealed record WorldLink(Point Door, TileNode Target, bool Warp);
    internal AfkWorld(Farmer player) { Player = player; }
    internal static TileNode At(Farmer p) => new(p.currentLocation.NameOrUniqueName, p.TilePoint.X, p.TilePoint.Y);
    internal static bool Supported(GameLocation? location) => location?.Map != null
        && !location.InIslandContext() && location is not MineShaft && location is not Desert
        && location is not VolcanoDungeon && location.currentEvent == null
        && !location.NameOrUniqueName.StartsWith("Temp", StringComparison.OrdinalIgnoreCase)
        && !location.NameOrUniqueName.Contains("Festival", StringComparison.OrdinalIgnoreCase);
    internal GameLocation? Map(string name)
    {
        if (!Maps.TryGetValue(name, out var result))
        { result = Game1.getLocationFromName(name); if (result != null) result.updateMap(); Maps[name] = result; }
        return result;
    }
    internal static Rectangle FootBox(Point tile) => new(tile.X * 64 + 8, tile.Y * 64 + 16, 48, 32);
    internal static bool DynamicBlocked(GameLocation map, Rectangle box, Farmer player) =>
        map.farmers.Any(f => f != player && f.GetBoundingBox().Intersects(box))
        || map.characters.Any(c => !c.IsInvisible && CharacterBox(c).Intersects(box))
        || map.animals.Values.Any(a => a.GetBoundingBox().Intersects(box));
    internal static Rectangle CharacterBox(NPC c)
    {
        var box = c.GetBoundingBox();
        if (c.layingDown) { box.Y -= 64; box.Height += 64; }
        return box;
    }
    internal static bool MonotonicExit(Rectangle before,Rectangle after,Rectangle obstacle)
    {
        var a=Rectangle.Intersect(before,obstacle);var b=Rectangle.Intersect(after,obstacle);
        return a.Width*a.Height>0 && b.Width*b.Height<=a.Width*a.Height
            && Vector2.DistanceSquared(after.Center.ToVector2(),obstacle.Center.ToVector2())>=Vector2.DistanceSquared(before.Center.ToVector2(),obstacle.Center.ToVector2());
    }
    internal static bool DynamicStepClear(GameLocation map,Rectangle before,Rectangle after,Farmer player,NPC? nativePet=null)
    {
        var sweep=Rectangle.Union(before,after);
        if(map.farmers.Any(f=>f!=player&&f.GetBoundingBox().Intersects(sweep))||map.animals.Values.Any(a=>a.GetBoundingBox().Intersects(sweep)))return false;
        foreach(var npc in map.characters)
        {
            if(npc.IsInvisible)continue;var box=CharacterBox(npc);if(!box.Intersects(sweep))continue;
            if(ReferenceEquals(npc,nativePet))continue; // native collision is still executed by Farmer.MovePosition
            if(!MonotonicExit(before,after,box))return false;
        }
        return true;
    }
    internal static bool GateCanOpen(Fence gate) => gate.GetType() == typeof(Fence) && gate.isGate.Value
        && gate.health.Value > 1 && gate.Location != null
        // These are the native 1.6.15 connectivity masks accepted by toggleGate, not a
        // replacement gate-opening implementation. Invalid free-standing gates stay blocked.
        && gate.getDrawSum() is 110 or 1500 or 1000 or 500 or 100 or 10;
    internal static Fence? ClosedGate(GameLocation map, Point tile) =>
        map.objects.TryGetValue(new Vector2(tile.X, tile.Y), out var obj) && obj is Fence f && !f.isPassable() && GateCanOpen(f) ? f : null;
    internal static bool TryOpenGate(GameLocation map, Point tile, Farmer who)
    {
        var gate = ClosedGate(map, tile);
        if (gate == null) return true;
        if (map != who.currentLocation || Math.Abs(who.TilePoint.X - tile.X) + Math.Abs(who.TilePoint.Y - tile.Y) > 1
            || !gate.checkForAction(who, justCheckingForActivity: true)) return false;
        // checkForAction(false) includes a trapped-farmer fence-removal fallback in vanilla.
        // Call ONLY the native gate-open operation. Null 'who' avoids TemporaryPassableTiles;
        // movement still waits for a genuinely passable gate, including linked double gates.
        gate.toggleGate(true, false, null);
        return gate.isPassable();
    }
    private static bool Within(GameLocation map, Point tile) => tile.X >= 0 && tile.Y >= 0
        && tile.X < map.Map.Layers[0].LayerWidth && tile.Y < map.Map.Layers[0].LayerHeight;
    private static bool LayerClear(GameLocation map, Point tile)
    {
        if (!Within(map, tile) || (map.isWaterTile(tile.X, tile.Y) && map.doesTileHaveProperty(tile.X, tile.Y, "Passable", "Buildings") == null)) return false;
        var back = map.Map.GetLayer("Back")?.Tiles[tile.X, tile.Y];
        if (back == null || map.doesTileHaveProperty(tile.X, tile.Y, "Passable", "Back") != null
            || map.doesTileHaveProperty(tile.X, tile.Y, "TemporaryBarrier", "Back") != null) return false;
        var wall = map.Map.GetLayer("Buildings")?.Tiles[tile.X, tile.Y];
        return wall == null || map.doesTileHaveProperty(tile.X, tile.Y, "Passable", "Buildings") != null
            || wall.TileIndexProperties.ContainsKey("Shadow");
    }
    private static bool ObjectsClear(GameLocation map, Point tile, Farmer player, bool allowGate, Rectangle? footprint = null)
    {
        var box = footprint ?? FootBox(tile); var key = new Vector2(tile.X, tile.Y);
        if (map.objects.TryGetValue(key, out var obj) && !obj.isPassable() && obj.GetBoundingBox().Intersects(box)
            && !(allowGate && obj is Fence fence && GateCanOpen(fence))) return false;
        if (map.terrainFeatures.TryGetValue(key, out var terrain) && !terrain.isPassable(player) && terrain.getBoundingBox().Intersects(box)) return false;
        if (map.resourceClumps.Any(c => c.getBoundingBox().Intersects(box))) return false;
        if (map.largeTerrainFeatures?.Any(f => f.getBoundingBox().Intersects(box)) == true) return false;
        if (map.furniture.Any(f => f.furniture_type.Value != 12 && f.IntersectsForCollision(box))) return false;
        if (map.buildings.Any(b => b.intersects(box))) return false;
        return true;
    }
    internal static bool StaticClear(GameLocation? map, Point tile, Farmer player, bool allowGate = true)
    {
        if (!Supported(map) || !LayerClear(map!, tile) || !ObjectsClear(map!, tile, player, allowGate)) return false;
        // The native query includes animals even with pathfinding=true. Separate dynamic
        // occupancy from immutable collision so an animal is expensive, not a severed graph.
        // Built-in static geometry was checked above; execution always checks native movement.
        if (DynamicBlocked(map!, FootBox(tile), player) || (allowGate && ClosedGate(map!, tile) != null)) return true;
        return !map!.isCollidingPosition(FootBox(tile), Game1.viewport, true, 0, false, player,
            pathfinding: true, projectile: false, ignoreCharacterRequirement: false, skipCollisionEffects: true);
    }
    // Sweep the actual Farmer footprint along a segment, not just tile centers/endpoints.
    // Conservative union of consecutive samples also covers native axis-by-axis diagonal
    // movement. No gaps at corners; no water, border, closed gates or accidental warps.
    internal static bool SegmentClear(GameLocation map, Point from, Point to, Farmer who, NPC? nativePet=null)
    {
        var delta = new Vector2(to.X - from.X, to.Y - from.Y);
        float length = delta.Length();
        if (length > 2048) return false; // local shortcut bound; ordinary grid search handles longer routes.
        var size = who.GetBoundingBox();
        Rectangle Box(Point p) => new(p.X - size.Width / 2, p.Y - size.Height / 2, size.Width, size.Height);
        var previous = Box(from);
        int samples = Math.Max(1, (int)Math.Ceiling(length / 4));
        for (int i = 0; i <= samples; i++)
        {
            var point = new Point((int)Math.Round(from.X + delta.X * i / samples), (int)Math.Round(from.Y + delta.Y * i / samples));
            var next = Box(point); var swept = Rectangle.Union(previous, next);
            for (int y = (int)Math.Floor(swept.Top / 64d); y <= (int)Math.Floor((swept.Bottom - 1) / 64d); y++)
                for (int x = (int)Math.Floor(swept.Left / 64d); x <= (int)Math.Floor((swept.Right - 1) / 64d); x++)
                {
                    if (!Within(map, new(x, y)) || !LayerClear(map, new(x, y))
                        || !ObjectsClear(map, new(x, y), who, false, swept)) return false;
                    if (map.warps.Any(w => !w.npcOnly.Value && w.X == x && w.Y == y)) return false;
                }
            if (!DynamicStepClear(map,previous,next,who,nativePet) || map.isCollidingPosition(swept, Game1.viewport, true, 0, false, who,
                pathfinding: true, projectile: false, ignoreCharacterRequirement: false, skipCollisionEffects: true)) return false;
            previous = next;
        }
        return true;
    }
    internal static bool CanStand(GameLocation? map, Point tile, Farmer player) => StaticClear(map, tile, player, false)
        && !DynamicBlocked(map!, FootBox(tile), player);
    internal bool Walkable(TileNode node) => CanStand(Map(node.Map), new Point(node.X, node.Y), Player);
    internal bool StaticWalkable(TileNode node)
    {
        if (!Passable.TryGetValue(node, out bool result)) Passable[node] = result = StaticClear(Map(node.Map), new Point(node.X, node.Y), Player);
        return result;
    }
    private bool Occupied(TileNode node)
    {
        if (!Actors.TryGetValue(node, out bool value))
        { var map = Map(node.Map); Actors[node] = value = map != null && DynamicBlocked(map, FootBox(new(node.X, node.Y)), Player); }
        return value;
    }
    internal IReadOnlyList<WorldLink> Links(string name)
    {
        if (Graph.TryGetValue(name, out var result)) return result;
        result = new(); Graph[name] = result;
        var map = Map(name); if (!Supported(map)) return result;
        foreach (var warp in map!.warps)
            if (!warp.npcOnly.Value && Supported(Map(warp.TargetName)))
                result.Add(new(new(warp.X, warp.Y), Landing(map, warp.TargetName, warp.TargetX, warp.TargetY), true));
        foreach (var pair in map.doors.Pairs)
        {
            string action = map.doesTileHaveProperty(pair.Key.X, pair.Key.Y, "Action", "Buildings") ?? "";
            if (!action.StartsWith("Warp ", StringComparison.Ordinal)) continue;
            var warp = map.getWarpFromDoor(pair.Key, Player);
            if (warp != null && Supported(Map(warp.TargetName))) result.Add(new(pair.Key, Landing(map, warp.TargetName, warp.TargetX, warp.TargetY), false));
        }
        var home = Utility.getHomeOfFarmer(Player);
        foreach (var building in map.buildings)
        {
            if (!ReferenceEquals(building.GetIndoors(), home) || building.daysOfConstructionLeft.Value > 0) continue;
            Point door = building.getPointForHumanDoor(), entry = home.getEntryLocation();
            result.Add(new(door, new(home.NameOrUniqueName, entry.X, entry.Y), false));
        }
        return result;
    }
    private TileNode Landing(GameLocation from, string name, int x, int y)
    {
        var target = Map(name)!;
        // Game1 uses Farm map entry properties for the vanilla edge warps. This is map
        // metadata, never a hardcoded Beach/Town/Farm route. Replan at actual arrival too.
        string? property = name == "Farm" ? (from.NameOrUniqueName, x, y) switch
        {
            ("Forest", 41, 64) => "ForestEntry", ("BusStop", 79, 17) => "BusStopEntry",
            ("Backwoods", 40, 0) => "BackwoodsEntry", ("FarmCave", 34, 6) => "FarmCaveEntry", _ => null
        } : null;
        if (property != null && target.TryGetMapPropertyAs(property, out Point point, false)) { x = point.X; y = point.Y; }
        else if (property != null)
        {
            // Native 1.6.15 fallback entry coordinates when these farm types omit metadata.
            // These describe where a normal warp lands; they never relocate the Farmer.
            Point? fallback = (property, Game1.whichFarm) switch
            {
                ("ForestEntry", 6) => new(82, 103), ("ForestEntry", 5) => new(40, 64),
                ("FarmCaveEntry", 6) => new(34, 16), ("FarmCaveEntry", 5) => new(30, 36), _ => null
            };
            if (fallback.HasValue) { x = fallback.Value.X; y = fallback.Value.Y; }
        }
        if (name == "BusStop" && x < 10) x = 10; // native arrival normalization, not a movement shortcut.
        return new(target.NameOrUniqueName, x, y);
    }
    // Explicit GameLocation graph, independent of sprite bounds. An initial shortest hop
    // route establishes connectivity; tile search refines it and can select other exits.
    internal List<string>? LocationRoute(string start, string goal)
    {
        var queue = new Queue<string>(); var parents = new Dictionary<string, string?> { [start] = null }; queue.Enqueue(start);
        while (parents.Count <= 128 && queue.TryDequeue(out var name))
        {
            if (name == goal)
            { var route = new List<string>(); for (string? n = name; n != null; n = parents[n]) route.Add(n); route.Reverse(); return route; }
            foreach (var link in Links(name)) if (parents.TryAdd(link.Target.Map, name)) queue.Enqueue(link.Target.Map);
        }
        return null;
    }
    internal IEnumerable<RouteStep> Edges(TileNode node)
    {
        var map = Map(node.Map); if (!Supported(map)) yield break;
        var links = Links(node.Map);
        foreach (var next in AfkPath.Neighbors(node))
        {
            var exits = links.Where(l => l.Door.X == next.X && l.Door.Y == next.Y).ToArray();
            foreach (var link in exits)
                if (StaticWalkable(link.Target) && (!link.Warp || ObjectsClear(map!, new(next.X, next.Y), Player, false)))
                    yield return new(node, link.Target, next.X, next.Y);
            if (exits.Any(l => l.Warp)) continue;
            if (StaticWalkable(next)) yield return new(node, next, Gate: ClosedGate(map!, new(next.X, next.Y)) != null);
        }
    }
    internal AfkRouteSearch BeginRoute(TileNode start, IReadOnlyDictionary<TileNode, int> goals, AfkNavigationMemory memory, double now)
    {
        var reachableMaps = goals.Keys.Select(n => n.Map).Distinct().Where(name => LocationRoute(start.Map, name) != null).ToHashSet();
        var valid = goals.Where(p => reachableMaps.Contains(p.Key.Map)).ToDictionary(p => p.Key, p => p.Value);
        return new(start, valid, Edges, step =>
        {
            long occupied = Occupied(step.To) ? 1 : 0;
            long cost = 1 + memory.Penalty(step.To, now);
            if (step.Transition)
            {
                // The exit/door tile belongs to the source map, not the landing map.
                // Include it so an NPC at an exit makes another legal exit preferable.
                var door = new TileNode(step.From.Map, step.DoorX, step.DoorY);
                if (Occupied(door)) occupied++;
                cost += memory.Penalty(door, now);
            }
            return new(occupied, cost);
        });
    }
    internal List<RouteStep>? Route(TileNode start, TileNode end)
    {
        var search = BeginRoute(start, new Dictionary<TileNode, int> { [end] = 0 }, new(), 0);
        while (search.Advance(4096, double.MaxValue) == RouteSearchStatus.Searching) { }
        return search.Route;
    }
    internal TileNode? Bed()
    {
        var home = Utility.getHomeOfFarmer(Player); var bed = home?.GetPlayerBed();
        if (bed == null) return null;
        Point spot = bed.GetBedSpot(); return new(home!.NameOrUniqueName, spot.X, spot.Y);
    }
    internal static bool EligibleChest(StardewValley.Object obj) => obj is Chest c && c.GetType() == typeof(Chest)
        && c.playerChest.Value && !c.fridge.Value && !c.giftbox.Value && c.GlobalInventoryId == null
        && c.SpecialChestType is Chest.SpecialChestTypes.None or Chest.SpecialChestTypes.BigChest
        && c.QualifiedItemId is "(BC)130" or "(BC)232" or "(BC)BigChest" or "(BC)BigStoneChest"
        && !Eddie.FishingMachine.ModEntry.IsAnchor(c);
    internal Chest? FindChest(TileNode standing, Chest? require = null)
    {
        // Callers must use the actual local player's position, never a saved fishing spot.
        if (standing != At(Player)) return null;
        if (require != null) return AfkChestArea.Accessible(require, Player) ? require : null;
        return AfkChestArea.Candidates(Player).FirstOrDefault();
    }
}

using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.ObjectModel;
using xTile.Tiles;

namespace BetterStardewValley;

internal static class CellarMap
{
    internal const string LayoutKey = WineRules.Prefix + "/CellarLayout";
    /// <summary>Test hook: false reproduces the 2.0.12 map (no bottom-edge relocation).</summary>
    internal static bool VisualPass = true;
    private static readonly string[] Interactive = { "Action", "TouchAction", "Water", "WaterSource", "Seat" };

    internal static string Expand(Map map, string? requiredStamp = null)
    {
        if (map.Properties.TryGetValue(LayoutKey, out var existing))
        {
            if (requiredStamp != null && requiredStamp != existing.ToString())
                throw new InvalidOperationException("Saved cellar layout does not match this map; nothing changed.");
            return existing.ToString();
        }
        var back = map.GetLayer("Back") ?? throw new InvalidOperationException("Cellar has no Back layer.");
        var buildings = map.GetLayer("Buildings") ?? throw new InvalidOperationException("Cellar has no Buildings layer.");
        int width = back.LayerWidth, height = back.LayerHeight;
        // Refuse unrelated cellar replacements. Never reshape an unknown mod's interior.
        if (width != 20 || height != 18 || map.Layers.Any(l => l.LayerWidth != width || l.LayerHeight != height))
            throw new MapConflictException($"Expected the native 20x18 cellar, found {width}x{height}. Another mod may replace the cellar map.");
        var floors = FloorTiles(map);
        if (floors.Count < 170 || floors.Count > 220)
            throw new MapConflictException($"Unrecognized cellar floor/collision layout ({floors.Count} tiles).");
        var rows = floors.GroupBy(p => p.Y).ToDictionary(g => g.Key, g => g.Count());
        int rowWidth = rows.Values.Max();
        // Extend below every original usable tile. Existing objects, furniture, players, and
        // the entrance keep their coordinates. Only the old empty bottom wall moves.
        int cut = floors.Max(p => p.Y) + 1;
        var candidates = rows.Where(p => p.Value == rowWidth && p.Key >= 7 && p.Key < cut)
            .Select(p => p.Key).OrderBy(y => y).Where(y => SafeRow(map, y)).ToArray();
        if (candidates.Length < 2 || cut >= height || rowWidth < 14)
            throw new MapConflictException("No safe cellar wall/floor bands were found.");
        int addedRows = (int)Math.Round(floors.Count * (WineRules.CellarCapacityMultiplier - 1) / rowWidth);
        if (addedRows < 1) throw new InvalidOperationException("Invalid cellar expansion.");
        int SourceY(int y) => y < cut ? y : y < cut + addedRows ? candidates[(y - cut) % candidates.Length] : y - addedRows;
        int MoveY(int y) => y >= cut ? y + addedRows : y;
        var properties = map.Properties.ToDictionary(p => p.Key, p => p.Value);
        // Existing warps are never duplicated. This preserves native farmhouse/cabin targeting.
        MoveProperty(properties, "Warp", 5, 1, MoveY);
        MoveProperty(properties, "NPCWarp", 5, 1, MoveY);
        MoveProperty(properties, "DefaultWarpLocation", 2, 1, MoveY);
        MoveProperty(properties, "Doors", 4, 1, MoveY);
        foreach (string key in new[] { "Light", "WindowLight", "BrookSounds" })
            ExtendProperty(properties, key, 3, 1, cut, addedRows, SourceY, MoveY);
        foreach (string key in new[] { "DayTiles", "NightTiles" })
            ExtendProperty(properties, key, 4, 2, cut, addedRows, SourceY, MoveY);

        var replacements = new List<Layer>();
        foreach (var old in map.Layers)
        {
            var layer = new Layer(old.Id, map, new Size(width, height + addedRows), old.TileSize)
            { Visible = old.Visible, Description = old.Description };
            foreach (var p in old.Properties) layer.Properties[p.Key] = p.Value;
            for (int y = 0; y < layer.LayerHeight; y++)
                for (int x = 0; x < width; x++)
                {
                    if (old.Id == "Paths" && y >= cut && y < cut + addedRows) continue;
                    layer.Tiles[x, y] = Clone(old.Tiles[x, SourceY(y)], layer);
                }
            replacements.Add(layer);
        }
        if (VisualPass) MoveBottomEdge(map, replacements, cut, addedRows);
        var expandedBack = replacements.Single(l => l.Id == "Back");
        var expandedBuildings = replacements.Single(l => l.Id == "Buildings");
        foreach (var p in floors)
            if (!CanPlace(expandedBack, expandedBuildings, p.X, p.Y))
                throw new InvalidOperationException("Expansion would obstruct an existing floor tile.");
        var expandedFloors = FloorTiles(expandedBack, expandedBuildings);
        int expandedCount = expandedFloors.Count;
        if (expandedCount != floors.Count + addedRows * rowWidth || !floors.IsSubsetOf(expandedFloors))
            throw new InvalidOperationException("The expanded floor is disconnected or would change existing floor tiles.");
        string stamp = $"1/{width}/{height}/{cut}/{addedRows}/{floors.Count}/{expandedCount}";
        if (requiredStamp != null && requiredStamp != stamp)
            throw new InvalidOperationException("Cannot restore the saved cellar onto a different base layout; nothing changed.");
        properties[LayoutKey] = stamp;
        foreach (var old in map.Layers.ToArray()) map.RemoveLayer(old);
        foreach (var layer in replacements) map.AddLayer(layer);
        foreach (var p in properties) map.Properties[p.Key] = p.Value;
        return stamp;
    }

    /// <summary>
    /// The native cellar draws its bottom wall over the last floor row (Front layer, plus corner pieces). The rows
    /// are inserted below that row, so without this the old wall line would cross the middle of the room and the
    /// new bottom edge would have no wall. The drawing is swapped with the last inserted row. Only tiles that behave
    /// identically are swapped (Front/AlwaysFront are drawing-only; Buildings only with the same collision), so
    /// floor, collision, placed objects and the layout stamp are unchanged.
    /// </summary>
    private static void MoveBottomEdge(Map original, List<Layer> layers, int cut, int addedRows)
    {
        var front = original.GetLayer("Front");
        int edge = cut - 1, last = cut + addedRows - 1, width = front?.LayerWidth ?? 0;
        // Recognize the native edge: a continuous Front row over the last floor row, nothing walkable below it.
        if (front == null || edge < 1 || Enumerable.Range(0, width).Any(x => front.Tiles[x, edge] == null)) return;
        foreach (var layer in layers)
        {
            if (layer.Id is not ("Front" or "AlwaysFront" or "Buildings")) continue;
            for (int x = 0; x < layer.LayerWidth; x++)
            {
                Tile? a = layer.Tiles[x, edge], b = layer.Tiles[x, last];
                if (Interactive.Any(k => GreenhouseMap.Property(a, k) != null || GreenhouseMap.Property(b, k) != null)) continue;
                if (layer.Id == "Buildings" && !GreenhouseMap.SameBehaviour(a, b)) continue;
                layer.Tiles[x, edge] = b; layer.Tiles[x, last] = a;
            }
        }
    }

    internal static HashSet<(int X, int Y)> FloorTiles(Map map)
        => FloorTiles(map.GetLayer("Back"), map.GetLayer("Buildings"));

    private static HashSet<(int X, int Y)> FloorTiles(Layer back, Layer buildings)
    {
        var visited = new HashSet<(int X, int Y)>();
        var floor = new HashSet<(int X, int Y)>();
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((6, 8)); // A native starting cask tile.
        while (queue.TryDequeue(out var p))
        {
            if (p.X < 0 || p.Y < 0 || p.X >= back.LayerWidth || p.Y >= back.LayerHeight || !visited.Add(p)) continue;
            if (!Walkable(back, buildings, p.X, p.Y)) continue;
            if (CanPlace(back, buildings, p.X, p.Y)) floor.Add(p);
            queue.Enqueue((p.X - 1, p.Y)); queue.Enqueue((p.X + 1, p.Y));
            queue.Enqueue((p.X, p.Y - 1)); queue.Enqueue((p.X, p.Y + 1));
        }
        return floor;
    }

    private static bool Walkable(Layer back, Layer buildings, int x, int y)
        => back.Tiles[x, y] != null && !back.Tiles[x, y].TileIndexProperties.ContainsKey("Passable")
            && (buildings.Tiles[x, y] == null || buildings.Tiles[x, y].TileIndexProperties.ContainsKey("Passable")
                || buildings.Tiles[x, y].TileIndexProperties.ContainsKey("Shadow"))
            && GreenhouseMap.Property(back.Tiles[x, y], "Water") == null;

    private static bool CanPlace(Layer back, Layer buildings, int x, int y)
        => Walkable(back, buildings, x, y) && GreenhouseMap.Property(back.Tiles[x, y], "NoFurniture") == null
            && !Interactive.Any(k => GreenhouseMap.Property(back.Tiles[x, y], k) != null
                || GreenhouseMap.Property(buildings.Tiles[x, y], k) != null);

    internal static bool SafeRow(Map map, int y)
    {
        foreach (var layer in map.Layers)
            for (int x = 0; x < layer.LayerWidth; x++)
                if (Interactive.Any(k => GreenhouseMap.Property(layer.Tiles[x, y], k) != null)) return false;
        foreach (string key in new[] { "Warp", "NPCWarp" })
            if (map.Properties.TryGetValue(key, out var value))
            {
                var parts = Split(value.ToString());
                for (int i = 0; i + 4 < parts.Length; i += 5)
                    if (int.Parse(parts[i + 1]) == y) return false;
            }
        return true;
    }

    internal static Tile? Clone(Tile? tile, Layer layer)
    {
        if (tile == null) return null;
        Tile copy = tile is AnimatedTile animated
            ? new AnimatedTile(layer, animated.TileFrames.Select(t => (StaticTile)Clone(t, layer)!).ToArray(), animated.FrameInterval)
            : tile.Clone(layer);
        foreach (var p in tile.Properties) copy.Properties[p.Key] = p.Value;
        return copy;
    }

    private static string[] Split(string value) => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    private static void MoveProperty(Dictionary<string, PropertyValue> properties, string key, int stride, int yi, Func<int, int> move)
    {
        if (!properties.TryGetValue(key, out var value)) return;
        var parts = Split(value.ToString());
        if (parts.Length % stride != 0) throw new InvalidOperationException("Unknown cellar " + key + " format.");
        for (int i = 0; i < parts.Length; i += stride) parts[i + yi] = move(int.Parse(parts[i + yi])).ToString();
        properties[key] = string.Join(" ", parts);
    }

    private static void ExtendProperty(Dictionary<string, PropertyValue> properties, string key, int stride, int yi,
        int cut, int added, Func<int, int> source, Func<int, int> move)
    {
        if (!properties.TryGetValue(key, out var value)) return;
        var parts = Split(value.ToString());
        MoveProperty(properties, key, stride, yi, move);
        var result = new List<string>(Split(properties[key].ToString()));
        for (int y = cut; y < cut + added; y++)
            for (int i = 0; i < parts.Length; i += stride)
                if (int.Parse(parts[i + yi]) == source(y))
                {
                    var copy = parts.Skip(i).Take(stride).ToArray();
                    copy[yi] = y.ToString();
                    result.AddRange(copy);
                }
        properties[key] = string.Join(" ", result);
    }
}

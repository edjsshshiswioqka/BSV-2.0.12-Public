using System;
using System.Collections.Generic;
using System.Linq;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.ObjectModel;
using xTile.Tiles;

namespace BetterStardewValley;

internal static class GreenhouseMap
{
    internal const string LayoutKey = "Eddie.BetterStardewValley/GreenhouseLayout";

    internal static string? Property(Tile? tile, string key)
    {
        if (tile is null) return null;
        if (tile.Properties.TryGetValue(key, out var value)) return value.ToString();
        return tile.TileIndexProperties.TryGetValue(key, out value) ? value.ToString() : null;
    }

    internal static GreenhouseLayout Expand(Map map, string? requiredStamp = null)
    {
        if (map.Properties.TryGetValue(LayoutKey, out var stamp))
        {
            if (requiredStamp != null && requiredStamp != stamp.ToString())
                throw new InvalidOperationException("Saved greenhouse layout does not match this map; nothing changed.");
            return GreenhouseLayout.Parse(stamp.ToString());
        }

        Layer back = map.GetLayer("Back") ?? throw new InvalidOperationException("Missing Back layer.");
        Layer buildings = map.GetLayer("Buildings") ?? throw new InvalidOperationException("Missing Buildings layer.");
        int width = back.LayerWidth, height = back.LayerHeight;
        var soil = new List<(int X, int Y)>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (Property(back.Tiles[x, y], "Diggable") is not null)
                    soil.Add((x, y));

        // Refuse to double an unrelated custom greenhouse or another expansion mod.
        if (soil.Count != 120) throw new InvalidOperationException($"Expected 120 tillable tiles, found {soil.Count}. Another greenhouse map may be installed.");
        int left = soil.Min(p => p.X), top = soil.Min(p => p.Y);
        if (soil.Max(p => p.X) - left != 11 || soil.Max(p => p.Y) - top != 9)
            throw new InvalidOperationException("Expected a single 12 by 10 soil rectangle.");
        var layout = new GreenhouseLayout(left, top);
        if (requiredStamp != null && requiredStamp != layout.Stamp)
            throw new InvalidOperationException("Cannot restore the saved greenhouse onto a different base layout; nothing changed.");
        var layers = map.Layers.ToArray();
        if (layers.Any(l => l.LayerWidth != width || l.LayerHeight != height))
            throw new InvalidOperationException("Greenhouse layers have different dimensions.");
        foreach (var p in soil)
            if (buildings.Tiles[p.X, p.Y] is not null && Property(buildings.Tiles[p.X, p.Y], "Passable") is null)
                throw new InvalidOperationException("A custom collision tile occupies the planting bed.");

        int fillColumn = PickStrip(map, layout, horizontal: true);
        int fillRow = PickStrip(map, layout, horizontal: false);
        var properties = map.Properties.ToDictionary(p => p.Key, p => p.Value);
        foreach (string key in new[] { "Warp", "NPCWarp" })
            MoveProperty(properties, layout, key, 5, 0, 1);
        foreach (string key in new[] { "Light", "WindowLight", "BrookSounds" })
            MoveProperty(properties, layout, key, 3, 0, 1);
        foreach (string key in new[] { "DayTiles", "NightTiles" })
            MoveProperty(properties, layout, key, 4, 1, 2);
        MoveProperty(properties, layout, "Doors", 4, 0, 1);
        MoveProperty(properties, layout, "DefaultWarpLocation", 2, 0, 1);
        properties[LayoutKey] = layout.Stamp;

        // Prepare all replacement layers before changing the asset. New layers also reset
        // xTile's cached draw dimensions, which raw LayerSize changes don't invalidate.
        var replacements = new List<Layer>();
        foreach (Layer old in layers)
        {
            var layer = new Layer(old.Id, map,
                new Size(width + GreenhouseLayout.AddedWidth, height + GreenhouseLayout.AddedHeight), old.TileSize)
            { Visible = old.Visible, Description = old.Description };
            foreach (var p in old.Properties) layer.Properties[p.Key] = p.Value;
            for (int y = 0; y < layer.LayerHeight; y++)
            {
                int sy = layout.SourceY(y, fillRow);
                for (int x = 0; x < layer.LayerWidth; x++)
                {
                    if (old.Id == "Paths" && layout.IsInserted(x, y)) continue;
                    int sx = layout.SourceX(x, fillColumn);
                    layer.Tiles[x, y] = CloneTile(old.Tiles[sx, sy], layer);
                }
            }
            replacements.Add(layer);
        }
        Layer expandedBack = replacements.Single(l => l.Id == "Back");
        Layer expandedBuildings = replacements.Single(l => l.Id == "Buildings");
        int count = 0;
        for (int y = 0; y < expandedBack.LayerHeight; y++)
            for (int x = 0; x < expandedBack.LayerWidth; x++)
                if (Property(expandedBack.Tiles[x, y], "Diggable") is not null) count++;
        if (count != 480) throw new InvalidOperationException($"Expanded map has {count} tillable tiles instead of 480.");
        for (int y = top; y < top + 20; y++)
            for (int x = left; x < left + 24; x++)
                if (Property(expandedBack.Tiles[x, y], "Diggable") is null
                    || (expandedBuildings.Tiles[x, y] is not null && Property(expandedBuildings.Tiles[x, y], "Passable") is null))
                    throw new InvalidOperationException("Expanded planting bed contains a blocked tile.");

        foreach (var old in layers) map.RemoveLayer(old);
        foreach (var layer in replacements) map.AddLayer(layer);
        foreach (var p in properties) map.Properties[p.Key] = p.Value;
        return layout;
    }

    private static Tile? CloneTile(Tile? source, Layer layer)
    {
        if (source is null) return null;
        Tile copy = source is AnimatedTile animated
            ? new AnimatedTile(layer, animated.TileFrames.Select(f => (StaticTile)CloneTile(f, layer)!).ToArray(), animated.FrameInterval)
            : source.Clone(layer);
        // xTile's Clone copies graphics but deliberately omits instance properties.
        // WaterSource, Diggable, Passable and actions must be copied explicitly.
        foreach (var property in source.Properties) copy.Properties[property.Key] = property.Value;
        return copy;
    }

    private static void MoveProperty(Dictionary<string, PropertyValue> properties,
        GreenhouseLayout layout, string key, int stride, int x, int y)
    {
        if (properties.TryGetValue(key, out var value))
            properties[key] = layout.MoveProperty(value.ToString(), stride, x, y);
    }

    private static int PickStrip(Map map, GreenhouseLayout layout, bool horizontal)
    {
        Layer back = map.GetLayer("Back");
        int start = horizontal ? layout.SoilX + 1 : layout.SoilY + 1;
        int end = horizontal ? layout.CutX - 1 : layout.CutY - 1;
        int length = horizontal ? back.LayerHeight : back.LayerWidth;
        int best = -1, bestScore = int.MaxValue;
        for (int candidate = start; candidate < end; candidate++)
        {
            int score = 0;
            for (int i = 0; i < length; i++)
            {
                int x = horizontal ? candidate : i;
                int y = horizontal ? i : candidate;
                foreach (Layer layer in map.Layers)
                {
                    Tile? tile = layer.Tiles[x, y];
                    if (tile is null) continue;
                    // Avoid repeating the water trough, exits, seats, or interactive decorations.
                    if (new[] { "Action", "TouchAction", "Water", "WaterSource", "Seat" }.Any(k => Property(tile, k) is not null))
                        score += 100000;
                    if (layer.Id == "Buildings") score += 8;
                    else if (layer.Id != "Back") score++;
                }
            }
            if (map.Properties.TryGetValue("Warp", out var warp))
            {
                var parts = warp.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i + 4 < parts.Length; i += 5)
                    if (int.TryParse(parts[i + (horizontal ? 0 : 1)], out int coordinate) && coordinate == candidate)
                        score += 100000;
            }
            if (score < bestScore) { best = candidate; bestScore = score; }
        }
        if (best < 0 || bestScore >= 100000)
            throw new InvalidOperationException("No safe wall/floor strip was available for greenhouse expansion.");
        return best;
    }
}

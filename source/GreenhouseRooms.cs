using System;
using System.Collections.Generic;
using System.Linq;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.ObjectModel;
using xTile.Tiles;

namespace BetterStardewValley;

/// <summary>
/// Map pieces for the three greenhouse upgrades. Every picture comes from the player's own game maps:
/// the orchard and the upper floor reuse the (BSV-expanded) greenhouse's own tiles, the mushroom cave reuses
/// the vanilla farm cave and its mine tilesheet. Nothing here is a redistributed image.
/// </summary>
internal static class GreenhouseRooms
{
    internal const string Sheet = "untitled tile sheet";
    internal const int BaseWidth = 43, BaseHeight = 34;
    // Orchard annex (upgrade 1): to the right of the greenhouse, joined by a 3-tile corridor.
    internal const int OrchardX = 46, OrchardWidth = 29, OrchardHeight = 38;
    internal const int CorridorTop = 18, CorridorRows = 3;
    // Planter grid inside the orchard (centre tile of each 3 x 3 bed).
    internal static IEnumerable<(int X, int Y)> OrchardTrees()
    {
        foreach (int x in new[] { 3, 7, 11, 15, 19, 23 })
            foreach (int y in new[] { 10, 14, 18, 22, 26, 30 })
                yield return (OrchardX + x + 1, y + 1);
    }
    // Upper floor stairs (upgrade 3) in the middle of the greenhouse's back wall.
    internal const int StairsX = 20, StairsTop = 4;
    internal static readonly int[] StairCols = { StairsX + 1, StairsX + 2 };
    // Mushroom cave (upgrade 2): an underground room reached by stairs down in the greenhouse's bottom wall.
    internal const int CaveWidth = 24, CaveHeight = 30, CaveLadderX = 12, CaveLadderY = 5, CaveArrivalY = 6;
    internal const int CaveStairsX = 3, CaveStairsTop = BaseHeight - 1;   // bottom-left corner, far from the door: columns 3, 4; walls at 2 and 5
    internal const string CaveLayoutKey = "Eddie.BetterStardewValley/MushroomCaveLayout", CaveLayout = "2";
    internal static IEnumerable<(int X, int Y)> MushroomBoxes()
    {
        foreach (int y in new[] { 10, 13, 16, 19, 22 })
            for (int x = 3; x <= 21; x += 3)
                yield return (x, y);
    }
    /// <summary>Box spots of the first cave layout (20 x 28), in the same order as <see cref="MushroomBoxes"/>.</summary>
    internal static IEnumerable<(int X, int Y)> OldMushroomBoxes()
    {
        foreach (int y in new[] { 8, 11, 14, 19, 22 })
            for (int x = 3; x <= 15; x += 2)
                yield return (x, y);
    }

    // ------------------------------------------------------------------ tile helpers
    private static string? Prop(Tile? t, string key) => GreenhouseMap.Property(t, key);
    private static Tile? Copy(Tile? source, Layer layer)
    {
        if (source is null) return null;
        TileSheet sheet = layer.Map.GetTileSheet(source.TileSheet.Id)
            ?? throw new InvalidOperationException("Missing tilesheet " + source.TileSheet.Id);
        Tile copy;
        if (source is AnimatedTile animated)
            copy = new AnimatedTile(layer, animated.TileFrames.Select(f => (StaticTile)Copy(f, layer)!).ToArray(), animated.FrameInterval);
        else
            copy = new StaticTile(layer, sheet, source.BlendMode, source.TileIndex);
        foreach (var p in source.Properties) copy.Properties[p.Key] = p.Value;
        return copy;
    }
    private static StaticTile New(Layer layer, string sheet, int index) => new(layer, layer.Map.GetTileSheet(sheet), BlendMode.Alpha, index);
    private static Map Blank(Map source, string id, int width, int height)
    {
        var map = new Map(id);
        foreach (var s in source.TileSheets)
        {
            var sheet = new TileSheet(s.Id, map, s.ImageSource, s.SheetSize, s.TileSize);
            foreach (var p in s.Properties) sheet.Properties[p.Key] = p.Value;
            map.AddTileSheet(sheet);
        }
        foreach (var old in source.Layers)
        {
            var layer = new Layer(old.Id, map, new Size(width, height), old.TileSize) { Visible = old.Visible, Description = old.Description };
            foreach (var p in old.Properties) layer.Properties[p.Key] = p.Value;
            map.AddLayer(layer);
        }
        return map;
    }
    private static void AddWarps(Map map, string warps)
    {
        string old = map.Properties.TryGetValue("Warp", out var w) ? w.ToString() : "";
        map.Properties["Warp"] = (old + " " + warps).Trim();
    }

    // ------------------------------------------------------------------ upgrade 1: orchard
    /// <summary>Returns the greenhouse map grown to the right with the orchard (coordinates of the greenhouse unchanged).</summary>
    internal static void AddOrchard(Map map)
    {
        Layer back0 = map.GetLayer("Back");
        if (back0.LayerWidth != BaseWidth || back0.LayerHeight != BaseHeight)
            throw new InvalidOperationException($"Expected the 43 x 34 BSV greenhouse, found {back0.LayerWidth} x {back0.LayerHeight}.");
        int width = OrchardX + OrchardWidth, height = Math.Max(BaseHeight, OrchardHeight);
        var old = map.Layers.ToArray();
        var grown = new List<Layer>();
        foreach (var o in old)
        {
            var layer = new Layer(o.Id, map, new Size(width, height), o.TileSize) { Visible = o.Visible, Description = o.Description };
            foreach (var p in o.Properties) layer.Properties[p.Key] = p.Value;
            for (int y = 0; y < o.LayerHeight; y++)
                for (int x = 0; x < o.LayerWidth; x++)
                    layer.Tiles[x, y] = Copy(o.Tiles[x, y], layer);
            grown.Add(layer);
        }
        Layer Get(string id) => grown.First(l => l.Id == id);
        Layer Src(string id) => old.First(l => l.Id == id);
        // The room: a greenhouse-style hall made only from the greenhouse's own columns and rows.
        int w = OrchardWidth, h = OrchardHeight;
        int[] srcCols = new[] { 0, 1, 2 }.Concat(Enumerable.Range(0, w - 6).Select(i => 11 + i % 2)).Concat(new[] { 40, 41, 42 }).ToArray();
        int[] srcRows = Enumerable.Range(0, 9).Concat(Enumerable.Repeat(12, h - 12)).Concat(new[] { 31, 32, 33 }).ToArray();
        foreach (string id in new[] { "Back", "Buildings", "Front" })
        {
            Layer src = Src(id), dst = Get(id);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sx = srcCols[x], sy = srcRows[y];
                    Tile? t = src.Tiles[sx, sy];
                    if (id != "Back" && sy >= 9 && sy <= 30 && x >= 1 && x < w - 1 && sx != 0 && sx != 42) t = null;
                    if (id == "Buildings" && (y == 7 || y == h - 2) && x >= 2 && x < w - 2) t = null;   // furniture of the source rows
                    if (id == "Front" && (y == 6 || y == h - 3) && x >= 2 && x < w - 2) t = null;
                    if (id == "Front" && y == h - 1) t = null;                                        // no door
                    dst.Tiles[OrchardX + x, y] = Copy(t, dst);
                }
        }
        Layer back = Get("Back"), buildings = Get("Buildings"), front = Get("Front");
        for (int y = 9; y < h - 3; y++)
            for (int x = 1; x < w - 1; x++) back.Tiles[OrchardX + x, y] = New(back, Sheet, 2109);
        for (int x = 2; x < w - 2; x++) back.Tiles[OrchardX + x, h - 3] = New(back, Sheet, 2109);
        for (int x = 1; x < w - 1; x++)
        {
            front.Tiles[OrchardX + x, h - 2] = New(front, Sheet, x == 1 ? 161 : x == w - 2 ? 166 : 165);
            back.Tiles[OrchardX + x, h - 1] = New(back, Sheet, 0);
            buildings.Tiles[OrchardX + x, h - 1] = New(buildings, Sheet, 0);
        }
        // Raised beds (decorative guides; greenhouse floors accept fruit trees anywhere).
        foreach (var (cx, cy) in OrchardTrees())
            for (int y = cy - 1; y <= cy + 1; y++)
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    bool top = y == cy - 1, bottom = y == cy + 1, side = x != cx;
                    int i = top ? (side ? 2078 : 1885) : bottom ? (side ? 2078 : 2079) : side ? 2106 : 2075;
                    back.Tiles[x, y] = New(back, Sheet, i);
                }
        // Corridor through the two walls only (greenhouse wall column 42, the gap, orchard wall column 46): both rooms
        // keep their own floor and edge strip, so nothing sticks out into their walkways.
        Open(grown, BaseWidth - 1, OrchardX, CorridorTop, CorridorTop + CorridorRows - 1);
        // Cells that are empty on every layer (the gap, the space under the greenhouse) become solid black, so nobody
        // can walk off the map. Cells with any tile (e.g. the glass walls, which have no floor under them) are left alone.
        var layersAll = grown.ToArray();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (layersAll.All(l => l.Tiles[x, y] == null)) { back.Tiles[x, y] = New(back, Sheet, 0); buildings.Tiles[x, y] = New(buildings, Sheet, 0); }
        foreach (var l in old) map.RemoveLayer(l);
        foreach (var l in grown) map.AddLayer(l);
    }
    private static void Open(List<Layer> layers, int x0, int x1, int y0, int y1)
    {
        Layer back = layers.First(l => l.Id == "Back");
        foreach (var l in layers.Where(l => l.Id is "Buildings" or "Front" or "AlwaysFront"))
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) l.Tiles[x, y] = null;
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) back.Tiles[x, y] = New(back, Sheet, y == y0 ? 1887 : 2109);
    }

    // ------------------------------------------------------------------ upgrade 2: stairs down to the mushroom cave
    /// <summary>Stairs down through the bottom wall, drawn like the vanilla farmhouse cellar stairs (same tilesheet).</summary>
    internal static void AddCaveDoor(Map map, string caveName)
    {
        Layer back = map.GetLayer("Back"), buildings = map.GetLayer("Buildings"), front = map.GetLayer("Front");
        if (back.LayerHeight < CaveStairsTop + 3) throw new InvalidOperationException("The greenhouse map is too short for the cave stairs.");
        int x0 = CaveStairsX, l = x0 - 1, r = x0 + 2, y = CaveStairsTop;
        void Set(Layer layer, int x, int yy, int index) => layer.Tiles[x, yy] = index < 0 ? null : New(layer, Sheet, index);
        // The corner's bush and basket (map decoration, not items) move four tiles right to make room for the stairs.
        void Move(Layer layer, int x, int yy)
        {
            if (layer.Tiles[x, yy] is not { } t) return;
            if (layer.Tiles[x + 4, yy] == null) layer.Tiles[x + 4, yy] = Copy(t, layer);
            layer.Tiles[x, yy] = null;
        }
        for (int x = l; x <= r - 1; x++) { Move(buildings, x, y - 2); Move(buildings, x, y - 1); Move(front, x, y - 2); }
        // Wall caps on the floor row above the opening.
        Set(front, l, y - 1, 162); Set(front, r, y - 1, 163); Set(front, x0, y - 1, -1); Set(front, x0 + 1, y - 1, -1);
        for (int x = x0; x <= x0 + 1; x++)
        {
            Set(back, x, y, 1043); Set(buildings, x, y, -1); Set(front, x, y, -1);
            Set(back, x, y + 1, 1075); Set(buildings, x, y + 1, -1); Set(front, x, y + 1, 165);
        }
        Set(back, l, y, 0); Set(buildings, l, y, 64); Set(front, l, y, 64); Set(back, r, y, 0); Set(buildings, r, y, 68); Set(front, r, y, 68);
        Set(back, l, y + 1, 0); Set(buildings, l, y + 1, 96); Set(front, l, y + 1, 96); Set(back, r, y + 1, 0); Set(buildings, r, y + 1, 130); Set(front, r, y + 1, 130);
        for (int x = l; x <= r; x++) { Set(back, x, y + 2, 0); Set(buildings, x, y + 2, 0); Set(front, x, y + 2, 0); }
        AddWarps(map, $"{x0} {y + 1} {caveName} {CaveLadderX} {CaveArrivalY} {x0 + 1} {y + 1} {caveName} {CaveLadderX} {CaveArrivalY}");
    }

    // ------------------------------------------------------------------ upgrade 3: stairs
    private static readonly int[,] StairBack = { { 1404, 1043, 1043, 1404 }, { 1436, 1075, 1075, 1436 }, { 1468, 2109, 2109, 1468 } };
    private static readonly int[,] StairBuildings = { { 155, -1, -1, 154 }, { 187, -1, -1, 186 }, { 219, -1, -1, 218 } };
    internal static void AddStairs(Map map, string target)
    {
        Layer back = map.GetLayer("Back"), buildings = map.GetLayer("Buildings"), front = map.GetLayer("Front");
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 4; c++)
            {
                int x = StairsX + c, y = StairsTop + r;
                back.Tiles[x, y] = New(back, Sheet, StairBack[r, c]);
                buildings.Tiles[x, y] = StairBuildings[r, c] < 0 ? null : New(buildings, Sheet, StairBuildings[r, c]);
                if (front != null) front.Tiles[x, y] = null;
            }
        for (int c = 0; c < 4; c++) buildings.Tiles[StairsX + c, StairsTop + 3] = null;
        AddWarps(map, string.Join(" ", StairCols.Select(x => $"{x} {StairsTop} {target} {x} {StairsTop + 3}")));
    }

    internal static Map CopyOf(Map source, string id)
    {
        Layer b = source.GetLayer("Back");
        var map = Blank(source, id, b.LayerWidth, b.LayerHeight);
        foreach (var src in source.Layers)
        {
            var dst = map.GetLayer(src.Id);
            for (int y = 0; y < src.LayerHeight; y++) for (int x = 0; x < src.LayerWidth; x++) dst.Tiles[x, y] = Copy(src.Tiles[x, y], dst);
        }
        foreach (var p in source.Properties) if (p.Key is not ("Warp" or "NPCWarp" or "Doors")) map.Properties[p.Key] = p.Value;
        return map;
    }

    // ------------------------------------------------------------------ the upper floor
    /// <summary>A copy of the 700-tile greenhouse (without annexes or the outside door) with stairs down.</summary>
    internal static Map BuildUpper(Map greenhouse, string id, string downstairs)
    {
        if (!greenhouse.Properties.ContainsKey(GreenhouseMap.LayoutKey)) throw new MapConflictException("The greenhouse is not BSV's 700-tile greenhouse.");
        var map = Blank(greenhouse, id, BaseWidth, BaseHeight);
        foreach (var src in greenhouse.Layers)
        {
            var dst = map.GetLayer(src.Id);
            for (int y = 0; y < BaseHeight; y++)
                for (int x = 0; x < BaseWidth; x++) dst.Tiles[x, y] = Copy(src.Tiles[x, y], dst);
        }
        // The ground floor may already have the orchard / cave openings in its side walls: upstairs they stay closed.
        foreach (var dst in map.Layers)
            for (int y = CorridorTop; y < CorridorTop + CorridorRows; y++)
                foreach (int x in new[] { 0, 1, 2, BaseWidth - 3, BaseWidth - 2, BaseWidth - 1 })
                    dst.Tiles[x, y] = Copy(greenhouse.GetLayer(dst.Id)?.Tiles[x, 12], dst);
        foreach (var p in greenhouse.Properties)
            if (p.Key is not ("Warp" or "NPCWarp" or "Doors" or "DefaultWarpLocation") && !p.Key.StartsWith("Eddie.", StringComparison.Ordinal))
                map.Properties[p.Key] = p.Value;
        // Close the outside door: bottom wall continues where the door was, nothing below it.
        Layer back = map.GetLayer("Back"), buildings = map.GetLayer("Buildings"), front = map.GetLayer("Front");
        for (int x = 0; x < BaseWidth; x++)
        {
            if (Prop(back.Tiles[x, BaseHeight - 1], "TouchAction") != null || back.Tiles[x, BaseHeight - 1]?.TileIndex == 2109) back.Tiles[x, BaseHeight - 1] = New(back, Sheet, 0);
            if (front.Tiles[x, BaseHeight - 1] != null) front.Tiles[x, BaseHeight - 1] = null;
            if (x > 0 && x < BaseWidth - 1) buildings.Tiles[x, BaseHeight - 1] = New(buildings, Sheet, 0);
        }
        for (int x = 2; x < BaseWidth - 2; x++)
            if (front.Tiles[x, BaseHeight - 2] is null || front.Tiles[x, BaseHeight - 2]!.TileIndex is 162 or 163) front.Tiles[x, BaseHeight - 2] = New(front, Sheet, 165);
        map.Properties["IsGreenhouse"] = "T";
        AddStairs(map, downstairs);
        return map;
    }

    // ------------------------------------------------------------------ the mushroom cave
    /// <summary>The vanilla farm cave stretched to 24 x 30 (same rock walls, ground and mine tilesheet), lit, with a ladder up.</summary>
    internal static Map BuildCave(Map farmCave, string id, string greenhouse)
    {
        Layer srcBack = farmCave.GetLayer("Back");
        if (srcBack.LayerWidth != 12 || srcBack.LayerHeight != 14)
            throw new MapConflictException($"Expected the vanilla 12 x 14 farm cave, found {srcBack.LayerWidth} x {srcBack.LayerHeight}.");
        int W = CaveWidth, H = CaveHeight;
        var map = Blank(farmCave, id, W, H);
        string cave = srcBack.Tiles[0, 0]?.TileSheet.Id ?? "cave";
        foreach (var src in farmCave.Layers)
        {
            if (src.Id == "Paths") continue;
            var dst = map.GetLayer(src.Id);
            for (int y = 0; y < H; y++)
            {
                int sy = y < 6 ? y : y >= H - 4 ? 14 - (H - y) : 6 + (y - 6) % 4;
                bool top = sy <= 5, bottom = sy >= 10;
                for (int x = 0; x < W; x++)
                {
                    int sx = top ? (x < 4 ? x : x >= W - 3 ? 12 - (W - x) : 4 + (x - 4) % 4)
                        : bottom ? (x < 3 ? x : x >= W - 2 ? 12 - (W - x) : 3 + (x - 3) % 4)
                        : (x < 2 ? x : x >= W - 2 ? 12 - (W - x) : 3 + ((x - 3) % 6 + 6) % 6);   // x = 2 wraps like the drawing, not onto the wall shadow
                    int ssy = !top && !bottom && (x < 2 || x >= W - 2) ? 7 + (y - 6) % 2 : sy;
                    Tile? t = src.Tiles[sx, ssy];
                    if (src.Id == "Back" && ssy is >= 6 and <= 9 && sx is >= 2 and <= 9) { dst.Tiles[x, y] = New(dst, cave, 138); continue; }
                    if (src.Id == "Back" && ssy == 5 && x >= 2 && x < W - 2) { dst.Tiles[x, y] = New(dst, cave, 234); continue; }
                    if (src.Id == "Front" && ssy == 11 && sx is >= 3 and <= 6) { dst.Tiles[x, y] = New(dst, cave, 77); continue; }
                    if (src.Id == "Front" && t?.TileIndex == 51) t = null;          // the farm cave's own decoration
                    dst.Tiles[x, y] = Copy(t, dst);
                }
            }
        }
        Layer back = map.GetLayer("Back"), buildings = map.GetLayer("Buildings"), front = map.GetLayer("Front");
        // Mine ladder up the back wall (the cave is underground); stepping onto its foot climbs back to the greenhouse.
        int lx = CaveLadderX;
        foreach (var (y, index) in new[] { (2, 67), (3, 83), (4, 99) }) { buildings.Tiles[lx, y] = New(buildings, cave, index); front.Tiles[lx, y] = null; }
        back.Tiles[lx, CaveLadderY] = New(back, cave, 115); buildings.Tiles[lx, CaveLadderY] = null; front.Tiles[lx, CaveLadderY] = null;
        AddWarps(map, $"{lx} {CaveLadderY} {greenhouse} {CaveStairsX} {CaveStairsTop - 1}");
        // Light: wall lanterns from the mine tilesheet along the back wall, plus soft lamps over the floor
        // (Paths tile 8 is the game's own light marker, as in the cellar and the mines).
        var paths = map.GetLayer("Paths");
        foreach (int x in new[] { 4, 8, 16, 20 }) { buildings.Tiles[x, 4] = New(buildings, cave, 82); front.Tiles[x, 4] = null; paths.Tiles[x, 4] = New(paths, "paths", 8); }
        foreach (var (x, y) in new[] { (6, 11), (18, 11), (12, 14), (6, 20), (18, 20), (12, 24) }) paths.Tiles[x, y] = New(paths, "paths", 8);
        paths.Tiles[lx, CaveArrivalY + 1] = New(paths, "paths", 8);
        map.Properties["AmbientLight"] = "60 60 25";
        return map;
    }
}

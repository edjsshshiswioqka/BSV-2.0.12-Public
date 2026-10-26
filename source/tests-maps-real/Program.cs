using System.Text.Json;
using BetterStardewValley;
using xTile;
using xTile.Format;
using xTile.Layers;
using xTile.Tiles;

// Runs BSV's greenhouse/cellar map code on the REAL game maps (Greenhouse.tbin / Cellar.tbin extracted
// locally from the player's own Content/Maps; they are not part of the repository). Compares the new
// visual pass with the 2.0.12 tile choice and dumps both maps so they can be rendered and inspected.
// This is an offline map check, not an in-game test.
internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAILED: " + name);
        checks++; Console.WriteLine("PASS: " + name);
    }
    private static Map Load(string dir, string name) => FormatManager.Instance.LoadMap(Path.Combine(dir, name + ".tbin"));
    private static object Dump(Map map) => map.Layers.ToDictionary(l => l.Id, l =>
        Enumerable.Range(0, l.LayerHeight).Select(y => Enumerable.Range(0, l.LayerWidth).Select(x => l.Tiles[x, y] is { } t ? t.TileSheet.Id + ":" + t.TileIndex : null).ToArray()).ToArray());
    private static int Count(Map map, string key)
    {
        var back = map.GetLayer("Back"); int n = 0;
        for (int y = 0; y < back.LayerHeight; y++) for (int x = 0; x < back.LayerWidth; x++) if (GreenhouseMap.Property(back.Tiles[x, y], key) != null) n++;
        return n;
    }
    private static void SameBehaviour(Map a, Map b, string what)
    {
        Check(a.Layers.Count == b.Layers.Count && a.Layers.Zip(b.Layers).All(p => p.First.Id == p.Second.Id && p.First.LayerSize == p.Second.LayerSize), what + ": same layers and size");
        foreach (var (la, lb) in a.Layers.Zip(b.Layers))
        {
            int differ = 0;
            if (la.Id is not ("Back" or "Buildings" or "Paths")) continue; // Front/AlwaysFront are drawing only
            for (int y = 0; y < la.LayerHeight; y++) for (int x = 0; x < la.LayerWidth; x++)
                if (!GreenhouseMap.SameBehaviour(la.Tiles[x, y], lb.Tiles[x, y])) differ++;
            Check(differ == 0, $"{what}: {la.Id} layer behaves exactly like 2.0.12 (collision, soil, water, actions)");
        }
        var pa = a.Properties.Where(p => p.Key != CellarAppearance.Key).ToDictionary(p => p.Key, p => p.Value.ToString());
        var pb = b.Properties.Where(p => p.Key != CellarAppearance.Key).ToDictionary(p => p.Key, p => p.Value.ToString());
        Check(pa.Count == pb.Count && pa.All(p => pb.TryGetValue(p.Key, out var v) && v == p.Value), what + ": warps, lights and map properties unchanged");
    }
    private static int Main(string[] args)
    {
        string dir = Environment.GetEnvironmentVariable("BSV_REAL_MAPS") ?? (args.Length > 0 ? args[0] : "");
        string output = dir;
        if (!File.Exists(Path.Combine(dir, "Greenhouse.tbin"))) { Console.WriteLine("SKIP: real maps not found in " + dir); return 0; }

        // Greenhouse.
        var original = Load(dir, "Greenhouse");
        int soil = Count(original, "Diggable");
        GreenhouseMap.VisualPass = false; var legacy = Load(dir, "Greenhouse"); var layout = GreenhouseMap.Expand(legacy);
        GreenhouseMap.VisualPass = true; var fixedMap = Load(dir, "Greenhouse"); var layout2 = GreenhouseMap.Expand(fixedMap);
        Check(soil == 120, "real greenhouse has 120 soil tiles");
        Check(layout.Stamp == layout2.Stamp, "layout stamp unchanged (existing saves keep their coordinates)");
        Check(Count(fixedMap, "Diggable") == 700, "real greenhouse expands to 700 soil tiles (35 x 20)");
        SameBehaviour(legacy, fixedMap, "greenhouse");
        var back0 = original.GetLayer("Back"); var back = fixedMap.GetLayer("Back");
        int midY = layout.SoilY + 5, edge = back0.Tiles[layout.SoilX + 11, midY].TileIndex, inner = back0.Tiles[layout.SoilX + 5, midY].TileIndex;
        if (edge != inner)
        {
            Check(back.Tiles[layout.SoilX + 11, midY].TileIndex != edge, "old right soil edge no longer cuts through the middle of the field");
            Check(back.Tiles[layout.SoilX + layout.BedWidth - 1, midY].TileIndex == edge, "right soil edge is drawn on the new outer column");
        }
        var b0 = original.GetLayer("Buildings"); var b1 = fixedMap.GetLayer("Buildings");
        int windowRow = Enumerable.Range(1, layout.SoilY - 1).First(y => b0.Tiles[layout.CutX - 1, y] != null && b0.Tiles[layout.CutX - 2, y] != null && b0.Tiles[layout.CutX - 1, y].TileIndex != b0.Tiles[layout.CutX - 2, y].TileIndex);
        Check(Enumerable.Range(layout.CutX, layout.AddedWidth - 1).All(x => b1.Tiles[x, windowRow].TileIndex != b1.Tiles[x + 1, windowRow].TileIndex), "window frames keep their alternating native rhythm across the added wall");

        // Cellar.
        var cellar0 = Load(dir, "Cellar"); var floors0 = CellarMap.FloorTiles(cellar0);
        CellarMap.VisualPass = false; var cLegacy = Load(dir, "Cellar"); string stamp = CellarMap.Expand(cLegacy);
        CellarMap.VisualPass = true; var cFixed = Load(dir, "Cellar"); string stamp2 = CellarMap.Expand(cFixed);
        Check(stamp == stamp2, "cellar layout stamp unchanged: " + stamp);
        var f = stamp.Split('/'); int cut = int.Parse(f[3]), added = int.Parse(f[4]);
        Check(CellarMap.FloorTiles(cLegacy).SetEquals(CellarMap.FloorTiles(cFixed)) && floors0.IsSubsetOf(CellarMap.FloorTiles(cFixed)), "cellar floor (cask spots) identical to 2.0.12 and every original spot kept");
        SameBehaviour(cLegacy, cFixed, "cellar (expansion)");
        var front0 = cellar0.GetLayer("Front"); var front = cFixed.GetLayer("Front");
        Check(Enumerable.Range(0, front0.LayerWidth).All(x => front.Tiles[x, cut + added - 1]?.TileIndex == front0.Tiles[x, cut - 1]?.TileIndex), "bottom wall is drawn along the new bottom row");
        Check(Enumerable.Range(1, front0.LayerWidth - 2).Count(x => front.Tiles[x, cut - 1]?.TileIndex == front0.Tiles[x, cut - 1]?.TileIndex) == 0, "old bottom wall line no longer crosses the middle of the cellar");
        var polished = Load(dir, "Cellar"); CellarMap.Expand(polished);
        Check(CellarAppearance.Apply(polished), "floor polish recognizes the real expanded cellar");
        Check(CellarMap.FloorTiles(polished).SetEquals(CellarMap.FloorTiles(cFixed)), "floor polish changes no cask spot");
        SameBehaviour(cFixed, polished, "cellar (floor polish)");
        var pb = polished.GetLayer("Back");
        int repeats = Enumerable.Range(cut, added - 2).Count(y => Enumerable.Range(0, pb.LayerWidth).All(x => pb.Tiles[x, y]?.TileIndex == pb.Tiles[x, y + 2]?.TileIndex));
        Check(repeats == 0, "added cellar rows no longer repeat every second row");
        Check(CellarAppearance.Apply(polished), "floor polish is idempotent on reload");

        // Greenhouse upgrades (orchard, mushroom cave door, stairs, upper floor) on the real expanded greenhouse.
        var up = Load(dir, "Greenhouse"); GreenhouseMap.Expand(up);
        int soilBefore = Count(up, "Diggable");
        GreenhouseRooms.AddOrchard(up);
        var ub = up.GetLayer("Back"); var ubu = up.GetLayer("Buildings");
        Console.WriteLine($"INFO: orchard soil tiles {Count(up, "Diggable") - soilBefore}");
        Check(up.Layers.All(l => l.LayerWidth == 75 && l.LayerHeight == 38), "orchard grows every layer to 75 x 38");
        Check(Enumerable.Range(0, 43).All(x => Enumerable.Range(0, 34).All(y => GreenhouseMap.Property(ub.Tiles[x, y], "Diggable") == GreenhouseMap.Property(fixedMap.GetLayer("Back").Tiles[x, y], "Diggable"))), $"greenhouse soil unchanged (soil tiles now {Count(up, "Diggable")}, before {soilBefore})");
        Check(Enumerable.Range(0, 43).All(x => Enumerable.Range(0, 34).All(y => x is >= 41 && y is >= 18 and <= 20 || GreenhouseMap.SameBehaviour(fixedMap.GetLayer("Buildings").Tiles[x, y], ubu.Tiles[x, y]))), "greenhouse walls unchanged except the orchard corridor");
        Check(Enumerable.Range(1, 6).All(y => ubu.Tiles[GreenhouseRooms.OrchardX + 6, y]?.TileIndex is int i && i != 0), "orchard glass wall is drawn (not blacked out)");
        var door = up.Properties["Warp"].ToString().Split(' ');
        Check(ubu.Tiles[int.Parse(door[0]), int.Parse(door[1])] == null, $"greenhouse front door warp tile {door[0]},{door[1]} stays open");
        var trees = GreenhouseRooms.OrchardTrees().ToList();
        Check(trees.Count == 36 && trees.Distinct().Count() == 36, "36 tree spots");
        Check(trees.All(t => ub.Tiles[t.X, t.Y] != null && ubu.Tiles[t.X, t.Y] == null && up.GetLayer("Front").Tiles[t.X, t.Y] == null), "every tree spot is open floor");
        bool Walk(int x, int y) => ub.Tiles[x, y] != null && ubu.Tiles[x, y] == null;
        Check(Enumerable.Range(38, 12).All(x => Enumerable.Range(18, 3).All(y => Walk(x, y))), "corridor from the greenhouse into the orchard is walkable");
        var seen = new HashSet<(int, int)>(); var queue = new Queue<(int, int)>(); queue.Enqueue((20, 20)); seen.Add((20, 20));
        while (queue.Count > 0) { var (x, y) = queue.Dequeue(); foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) { int nx = x + dx, ny = y + dy; if (nx >= 0 && ny >= 0 && nx < 75 && ny < 38 && Walk(nx, ny) && seen.Add((nx, ny))) queue.Enqueue((nx, ny)); } }
        Check(trees.All(t => seen.Contains((t.X, t.Y))), "every tree spot can be reached on foot from the greenhouse");
        Check(!seen.Any(p => p.Item1 >= 43 && p.Item2 >= 37), "orchard has a closed bottom wall");
        Check(!seen.Any(p => p.Item1 is >= 43 and <= 45 && p.Item2 is < 18 or > 20), "nobody can walk into the black gap between greenhouse and orchard");
        Check(!seen.Any(p => p.Item1 < 43 && p.Item2 >= 34), "nobody can walk below the greenhouse");
        GreenhouseRooms.AddCaveDoor(up, "Cave");
        Check(up.Properties["Warp"].ToString().Contains("3 34 Cave 12 6 4 34 Cave 12 6"), "stairs down in the bottom wall warp into the mushroom cave");
        Check(Walk(3, 32) && Walk(3, 33) && Walk(4, 34) && !Walk(2, 33) && !Walk(5, 34) && !Walk(3, 35), "cave stairs (bottom-left corner) are walkable between their walls");
        Check(Enumerable.Range(0, 43).Count(x => ub.Tiles[x, 33]?.TileIndex == 2109) == 1, "the greenhouse's own front door is untouched");
        GreenhouseRooms.AddStairs(up, "Upper");
        Check(up.Properties["Warp"].ToString().Contains("21 4 Upper 21 7"), "stairs warp to the upper floor");
        Check(Walk(21, 5) && Walk(22, 5) && Walk(21, 7), "stairs are walkable");
        up.Properties[GreenhouseMap.LayoutKey] = layout.Stamp;
        var upper = GreenhouseRooms.BuildUpper(up, "Upper", "Greenhouse");
        var pb2 = upper.GetLayer("Back"); var pbu = upper.GetLayer("Buildings");
        Check(upper.Layers.All(l => l.LayerWidth == 43 && l.LayerHeight == 34), "upper floor is 43 x 34");
        Check(Count(upper, "Diggable") == 700, "upper floor has 700 soil tiles");
        Check(upper.Properties["IsGreenhouse"].ToString() == "T" && upper.Properties["Warp"].ToString() == "21 4 Greenhouse 21 7 22 4 Greenhouse 22 7", "upper floor is a greenhouse with only the stairs down");
        Check(Enumerable.Range(18, 3).All(y => pbu.Tiles[0, y] != null && pbu.Tiles[42, y] != null), "upper floor side walls are closed");
        Check(Enumerable.Range(0, 43).All(x => pb2.Tiles[x, 33] == null || GreenhouseMap.Property(pb2.Tiles[x, 33], "TouchAction") == null), "upper floor has no outside door");
        string cavePath = Path.Combine(dir, "FarmCave.tbin");
        Map? caveMap = null;
        if (File.Exists(cavePath))
        {
            caveMap = GreenhouseRooms.BuildCave(Load(dir, "FarmCave"), "Cave", "Greenhouse");
            var cb = caveMap.GetLayer("Back"); var cbu = caveMap.GetLayer("Buildings");
            Check(caveMap.Layers.All(l => l.LayerWidth == 24 && l.LayerHeight == 30), "mushroom cave is 24 x 30");
            var boxes = GreenhouseRooms.MushroomBoxes().ToList();
            Check(boxes.Count == 35 && boxes.All(b => cb.Tiles[b.X, b.Y] != null && cbu.Tiles[b.X, b.Y] == null), "35 mushroom box spots on open cave floor");
            Check(caveMap.Properties["Warp"].ToString() == "12 5 Greenhouse 3 32", "cave ladder climbs back to the greenhouse stairs");
            Check(cb.Tiles[12, 6] != null && cbu.Tiles[12, 6] == null && cbu.Tiles[12, 5] == null, "cave arrival and ladder foot are walkable");
            var cp = caveMap.GetLayer("Paths");
            int lights = Enumerable.Range(0, 24).Sum(x => Enumerable.Range(0, 30).Count(y => cp.Tiles[x, y]?.TileIndex == 8));
            Check(lights >= 8, $"mushroom cave has {lights} light sources");
            var cseen = new HashSet<(int, int)>(); var cq = new Queue<(int, int)>(); cq.Enqueue((12, 6)); cseen.Add((12, 6));
            bool CWalk(int x, int y) => x >= 0 && y >= 0 && x < 24 && y < 30 && cb.Tiles[x, y] != null && cbu.Tiles[x, y] == null && !boxes.Contains((x, y));
            while (cq.Count > 0) { var (x, y) = cq.Dequeue(); foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) if (CWalk(x + dx, y + dy) && cseen.Add((x + dx, y + dy))) cq.Enqueue((x + dx, y + dy)); }
            Check(boxes.All(b => new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d => cseen.Contains((b.X + d.Item1, b.Y + d.Item2)))), "every mushroom box can be reached from the ladder");
        }
        else Console.WriteLine("SKIP: FarmCave.tbin not found; mushroom cave checked in game only.");

        File.WriteAllText(Path.Combine(output, "maps-dump.json"), JsonSerializer.Serialize(new
        {
            greenhouse = new { legacy = Dump(legacy), @fixed = Dump(fixedMap), upgraded = Dump(up), upper = Dump(upper), cave = caveMap is null ? null : Dump(caveMap) },
            cellar = new { legacy = Dump(cLegacy), @fixed = Dump(cFixed), polished = Dump(polished) }
        }));
        Console.WriteLine($"{{\"suite\":\"real-game-maps\",\"checks\":{checks},\"status\":\"PASS\",\"inGame\":false}}");
        return 0;
    }
}

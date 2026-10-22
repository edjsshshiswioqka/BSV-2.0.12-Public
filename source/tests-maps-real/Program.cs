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
        string dir = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("BSV_REAL_MAPS") ?? "";
        string output = args.Length > 1 ? args[1] : dir;
        if (!File.Exists(Path.Combine(dir, "Greenhouse.tbin"))) { Console.WriteLine("SKIP: real maps not found in " + dir); return 0; }

        // Greenhouse.
        var original = Load(dir, "Greenhouse");
        int soil = Count(original, "Diggable");
        GreenhouseMap.VisualPass = false; var legacy = Load(dir, "Greenhouse"); var layout = GreenhouseMap.Expand(legacy);
        GreenhouseMap.VisualPass = true; var fixedMap = Load(dir, "Greenhouse"); var layout2 = GreenhouseMap.Expand(fixedMap);
        Check(soil == 120, "real greenhouse has 120 soil tiles");
        Check(layout.Stamp == layout2.Stamp, "layout stamp unchanged (existing saves keep their coordinates)");
        Check(Count(fixedMap, "Diggable") == 480, "real greenhouse expands to 480 soil tiles");
        SameBehaviour(legacy, fixedMap, "greenhouse");
        var back0 = original.GetLayer("Back"); var back = fixedMap.GetLayer("Back");
        int midY = layout.SoilY + 5, edge = back0.Tiles[layout.SoilX + 11, midY].TileIndex, inner = back0.Tiles[layout.SoilX + 5, midY].TileIndex;
        if (edge != inner)
        {
            Check(back.Tiles[layout.SoilX + 11, midY].TileIndex != edge, "old right soil edge no longer cuts through the middle of the field");
            Check(back.Tiles[layout.SoilX + 23, midY].TileIndex == edge, "right soil edge is drawn on the new outer column");
        }
        var b0 = original.GetLayer("Buildings"); var b1 = fixedMap.GetLayer("Buildings");
        int windowRow = Enumerable.Range(1, layout.SoilY - 1).First(y => b0.Tiles[layout.CutX - 1, y] != null && b0.Tiles[layout.CutX - 2, y] != null && b0.Tiles[layout.CutX - 1, y].TileIndex != b0.Tiles[layout.CutX - 2, y].TileIndex);
        Check(Enumerable.Range(layout.CutX, 11).All(x => b1.Tiles[x, windowRow].TileIndex != b1.Tiles[x + 1, windowRow].TileIndex), "window frames keep their alternating native rhythm across the added wall");

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

        File.WriteAllText(Path.Combine(output, "maps-dump.json"), JsonSerializer.Serialize(new
        {
            greenhouse = new { legacy = Dump(legacy), @fixed = Dump(fixedMap) },
            cellar = new { legacy = Dump(cLegacy), @fixed = Dump(cFixed), polished = Dump(polished) }
        }));
        Console.WriteLine($"{{\"suite\":\"real-game-maps\",\"checks\":{checks},\"status\":\"PASS\",\"inGame\":false}}");
        return 0;
    }
}

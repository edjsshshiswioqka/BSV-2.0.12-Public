using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using BetterStardewValley;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }

    // This synthetic map uses real xTile classes. It is NOT the game's Greenhouse asset.
    private static Map Fixture()
    {
        var map = new Map("GreenhouseFixture");
        var sheet = new TileSheet("fixture", map, "not-rendered.png", new Size(8, 8), new Size(16, 16));
        map.AddTileSheet(sheet);
        sheet.TileIndexProperties[1]["Diggable"] = "T";
        var back = new Layer("Back", map, new Size(20, 26), new Size(16, 16));
        var buildings = new Layer("Buildings", map, new Size(20, 26), new Size(16, 16));
        var front = new Layer("Front", map, new Size(20, 26), new Size(16, 16));
        map.AddLayer(back); map.AddLayer(buildings); map.AddLayer(front);
        for (int y = 0; y < 26; y++)
            for (int x = 0; x < 20; x++)
            {
                bool soil = x >= 4 && x < 16 && y >= 7 && y < 17;
                back.Tiles[x, y] = new StaticTile(back, sheet, BlendMode.Alpha, soil ? 1 : 0);
                if (((x == 1 || x == 18) && y >= 2 && y <= 24)
                    || ((y == 2 || y == 24) && x >= 1 && x <= 18 && x != 10))
                    buildings.Tiles[x, y] = new StaticTile(buildings, sheet, BlendMode.Alpha, 2);
            }
        back.Tiles[6, 4].Properties["Water"] = "T";
        buildings.Tiles[6, 4] = new StaticTile(buildings, sheet, BlendMode.Alpha, 3);
        buildings.Tiles[6, 4].Properties["WaterSource"] = "T";
        front.Tiles[18, 5] = new AnimatedTile(front, new[]
        {
            new StaticTile(front, sheet, BlendMode.Alpha, 4),
            new StaticTile(front, sheet, BlendMode.Alpha, 5)
        }, 100);
        back.Tiles[17, 20].Properties["Sentinel"] = "preserved";
        map.Properties["Warp"] = "10 24 Farm 28 16";
        map.Properties["Light"] = "18 21 4";
        map.Properties["WindowLight"] = "18 5 2";
        map.Properties["DayTiles"] = "Front 18 5 4";
        map.Properties["NightTiles"] = "Front 18 5 5";
        map.Properties["IsGreenhouse"] = "T";
        map.Properties["AmbientLight"] = "95 95 95";
        return map;
    }

    private static int CountProperty(Map map, string layer, string property)
    {
        var l = map.GetLayer(layer);
        int count = 0;
        for (int y = 0; y < l.LayerHeight; y++)
            for (int x = 0; x < l.LayerWidth; x++)
                if (GreenhouseMap.Property(l.Tiles[x, y], property) is not null) count++;
        return count;
    }

    private static void Main()
    {
        Map map = Fixture();
        Check(CountProperty(map, "Back", "Diggable") == 120, "fixture has 120 starting soil tiles");
        var layout = GreenhouseMap.Expand(map);
        Check(CountProperty(map, "Back", "Diggable") == 700, "actual xTile expansion yields exactly 700 soil tiles (35 x 20)");
        Check(map.Layers.All(l => l.LayerWidth == 43 && l.LayerHeight == 36), "all map layers resized together");
        Check(map.Properties["Warp"].ToString() == "10 34 Farm 28 16", "exit moves down; external destination stays unchanged");
        Check(map.Properties["Light"].ToString() == "41 31 4", "perimeter lamp follows both expanded axes");
        Check(map.Properties["DayTiles"].ToString() == "Front 41 5 4"
            && map.Properties["NightTiles"].ToString() == "Front 41 5 5", "day/night tile changes follow the window");
        Check(CountProperty(map, "Back", "Water") == 1 && CountProperty(map, "Buildings", "WaterSource") == 1,
            $"water trough remains single and interactive ({CountProperty(map, "Back", "Water")}/{CountProperty(map, "Buildings", "WaterSource")})");
        Check(map.GetLayer("Front").Tiles[41, 5] is AnimatedTile, "animated tile survives expansion");
        Check(map.GetLayer("Back").Tiles[40, 30].Properties["Sentinel"].ToString() == "preserved", "original tile metadata moves intact");
        Check(map.Properties["AmbientLight"].ToString() == "95 95 95", "non-coordinate map properties stay intact");
        Check(map.GetLayer("Buildings").Tiles[10, 33] is null, "arrival tile north of exit is open");

        var visited = new HashSet<(int X, int Y)>();
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((10, 33));
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (p.X < 0 || p.Y < 0 || p.X >= 43 || p.Y >= 36 || visited.Contains(p)) continue;
            if (map.GetLayer("Buildings").Tiles[p.X, p.Y] is not null) continue;
            visited.Add(p);
            queue.Enqueue((p.X - 1, p.Y)); queue.Enqueue((p.X + 1, p.Y));
            queue.Enqueue((p.X, p.Y - 1)); queue.Enqueue((p.X, p.Y + 1));
        }
        Check(Enumerable.Range(4, 35).All(x => Enumerable.Range(7, 20).All(y => visited.Contains((x, y)))),
            "all 700 planting tiles reachable from the entrance");

        var sameLayout = GreenhouseMap.Expand(map);
        var restored = Fixture();
        Check(GreenhouseMap.Expand(restored, layout.Stamp).Stamp == layout.Stamp, "recorded expansion can be restored on matching native-shaped map");
        var conflict = Fixture(); var originalLayers = conflict.Layers.ToArray();
        bool mismatch = false;
        try { GreenhouseMap.Expand(conflict, "foreign-layout"); } catch (InvalidOperationException) { mismatch = true; }
        Check(mismatch && conflict.Layers.SequenceEqual(originalLayers) && !conflict.Properties.ContainsKey(GreenhouseMap.LayoutKey), "mismatched saved layout never commits replacement layers");
        mismatch = false;
        try { GreenhouseMap.Expand(map, "foreign-layout"); } catch (InvalidOperationException) { mismatch = true; }
        Check(mismatch && GreenhouseMap.Expand(map).Stamp == layout.Stamp, "already expanded mismatched stamp is refused without changing the map");
        Check(sameLayout.Stamp == layout.Stamp && map.GetLayer("Back").LayerWidth == 43
            && CountProperty(map, "Back", "Diggable") == 700, "same map cannot expand twice");
        Check(GreenhouseLayout.Parse(layout.Stamp).CutX == 16, "saved migration stamp round-trips");
        Check(Enumerable.Range(4, 12).All(x => layout.MoveX(x) == x)
            && Enumerable.Range(7, 10).All(y => layout.MoveY(y) == y), "every original crop coordinate stays unchanged");
        Check(layout.MoveX(16) == 39 && layout.MoveY(17) == 27, "right/bottom perimeter moves outward");
        Check(layout.MovePixelX(16 * 64 + 13.5f) == 39 * 64 + 13.5f, "character movement preserves within-tile offset");

        var identities = new Dictionary<(int X, int Y), object>();
        for (int y = 0; y < 26; y++)
            for (int x = 0; x < 20; x++) identities.Add((x, y), new object());
        var migrated = identities.ToDictionary(p => (layout.MoveX(p.Key.X), layout.MoveY(p.Key.Y)), p => p.Value);
        Check(migrated.Count == identities.Count && identities.All(p => ReferenceEquals(p.Value, migrated[(layout.MoveX(p.Key.X), layout.MoveY(p.Key.Y))])),
            "full map coordinate migration has no collisions and retains every item instance");

        Map bad = Fixture(); bad.GetLayer("Back").Tiles[4, 7].Properties["Diggable"] = "T";
        bad.GetLayer("Back").Tiles[3, 7].Properties["Diggable"] = "T";
        bool refused = false;
        try { GreenhouseMap.Expand(bad); } catch (InvalidOperationException) { refused = true; }
        Check(refused && bad.GetLayer("Back").LayerWidth == 20 && !bad.Properties.ContainsKey(GreenhouseMap.LayoutKey),
            "custom planting layout is refused without partial map edits");

        Map malformed = Fixture(); malformed.Properties["Warp"] = "10 24 Farm";
        refused = false;
        try { GreenhouseMap.Expand(malformed); } catch (InvalidOperationException) { refused = true; }
        Check(refused && malformed.GetLayer("Back").LayerWidth == 20, "malformed warp cannot partially expand a map");

        // A 2.0.12 save (v1, 24 x 20) is rebuilt exactly while the expansion is switched off, and upgrades in place.
        var v1 = Fixture();
        var v1Layout = GreenhouseMap.Expand(v1, "v1:4,7,12,10");
        Check(v1Layout.Stamp == "v1:4,7,12,10" && CountProperty(v1, "Back", "Diggable") == 480 && v1.GetLayer("Back").LayerWidth == 32,
            "older v1 layout is restored exactly (480 tiles)");
        Check(Enumerable.Range(0, 28).All(x => layout.UpgradeX(v1Layout, x) == x) && layout.UpgradeX(v1Layout, 28) == 39
            && Enumerable.Range(0, 40).All(y => layout.UpgradeY(v1Layout, y) == y) && layout.Grows(v1Layout),
            "v1 -> v2 upgrade keeps every old bed tile; the right wall moves 11 tiles out");
        Map instanceSoil = Fixture();
        var sheet = instanceSoil.TileSheets[0];
        sheet.TileIndexProperties[1].Remove("Diggable");
        for (int y = 7; y < 17; y++)
            for (int x = 4; x < 16; x++) instanceSoil.GetLayer("Back").Tiles[x,y].Properties["Diggable"] = "T";
        GreenhouseMap.Expand(instanceSoil);
        Check(CountProperty(instanceSoil, "Back", "Diggable") == 700, "per-instance Diggable properties survive cloning too");
        instanceSoil.GetLayer("Back").Tiles[16,7].Properties["TestUnique"] = "T";
        Check(GreenhouseMap.Property(instanceSoil.GetLayer("Back").Tiles[5,7], "TestUnique") is null,
            "new tiles have independent property collections");

        string temp = Path.Combine(Path.GetTempPath(), "bsv-backup-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            string save = Path.Combine(temp, "Eddie_123");
            Directory.CreateDirectory(save);
            File.WriteAllText(Path.Combine(save, "Eddie_123"), "original world and inventories");
            File.WriteAllText(Path.Combine(save, "SaveGameInfo"), "original player info");
            string backup = GreenhouseBackup.Create(save, Path.Combine(temp, "backups"))!;
            File.WriteAllText(Path.Combine(save, "Eddie_123"), "modified world");
            Check(File.ReadAllText(Path.Combine(backup, "Eddie_123")) == "original world and inventories"
                && File.ReadAllText(Path.Combine(backup, "SaveGameInfo")) == "original player info", "pre-expansion backup retains original save bytes");
            Check(GreenhouseBackup.Create(null, Path.Combine(temp, "backups")) is null, "brand-new game needs no on-disk backup");
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        Console.WriteLine($"{checks} greenhouse checks passed. Synthetic map, not an in-game playtest.");
    }
}

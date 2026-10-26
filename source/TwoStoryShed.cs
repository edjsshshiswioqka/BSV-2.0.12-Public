using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Buildings;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace BetterStardewValley;

/// <summary>
/// Two-Story Shed: Robin upgrades a Big Shed overnight (500 wood, 500 stone, 60,000g). Both floors live in the
/// shed's own interior map, side by side: the ground floor is the vanilla Big Shed (same coordinates, nothing
/// moves on upgrade), the upper floor is the same room without a door. Stairs right of the door, set into the
/// bottom wall like the farmhouse cellar stairs, connect them. Everything on both floors is saved with the shed.
/// </summary>
internal static class TwoStoryShed
{
    internal const string Id = "Eddie.BetterStardewValley_TwoStoryShed";
    internal const string MapAsset = "Maps/" + Id, MarkerKey = "Eddie.BetterStardewValley/TwoStoryShed";
    internal const string Sheet = "untitled tile sheet";
    // The gap is wider than half of any screen, so only one floor is ever in view (the camera also stays on one floor).
    internal const int RoomWidth = 19, RoomHeight = 17, Gap = 64, UpperX = RoomWidth + Gap;  // upper floor starts at x = 83
    private const int OldUpperX = 22;                                                          // first test build
    internal const int StairX = 12, StairTop = RoomHeight - 1;                                 // stairs at x 12, 13; rows 16, 17
    internal const int Gold = 60_000, Wood = 500, Stone = 500;
    private static IModHelper Helper = null!;
    private static IMonitor Monitor = null!;

    internal static void Install(IModHelper helper, IMonitor monitor)
    {
        Helper = helper; Monitor = monitor;
        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.UpdateTicked += (_, _) => { try { Stairs(); } catch (Exception ex) { Monitor.LogOnce("BSV two-story shed stairs: " + ex.Message, LogLevel.Warn); } };
        helper.Events.Content.LocaleChanged += (_, _) => helper.GameContent.InvalidateCache("Data/Buildings");
        helper.Events.GameLoop.SaveLoaded += (_, _) => Migrate();
        helper.Events.GameLoop.DayStarted += (_, _) => Migrate();
        try
        {
            var h = new HarmonyLib.Harmony(helper.ModRegistry.ModID + ".TwoStoryShed");
            var post = new HarmonyLib.HarmonyMethod(typeof(TwoStoryShed), nameof(Camera));
            foreach (var m in typeof(Game1).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                         .Where(m => m.Name is "UpdateViewPort" or "clampViewportToGameMap"))
                h.Patch(m, postfix: post);
        }
        catch (Exception ex) { Monitor.Log("BSV two-story shed camera: " + ex.Message, LogLevel.Warn); }
    }

    /// <summary>Keeps the camera on the floor the player is on, centred like a vanilla room when it is smaller than the screen.</summary>
    private static void Camera()
    {
        try
        {
            var here = Game1.currentLocation; var who = Game1.player;
            if (here?.Map?.Properties.ContainsKey(MarkerKey) != true || who?.currentLocation != here) return;
            int left = who.Position.X >= (RoomWidth + Gap / 2) * Game1.tileSize ? UpperX * Game1.tileSize : 0;
            int w = RoomWidth * Game1.tileSize, vw = Game1.viewport.Width;
            Game1.viewport.X = w <= vw ? left - (vw - w) / 2 : Math.Clamp(Game1.viewport.X, left, left + w - vw);
        }
        catch { }
    }

    /// <summary>Things placed upstairs in the first test build (upper floor at x 22) move to the new upper floor.</summary>
    private static void Migrate()
    {
        if (!Context.IsMainPlayer) return;
        Utility.ForEachLocation(loc =>
        {
            if (loc.Map?.Properties.ContainsKey(MarkerKey) != true) return true;
            bool Old(Vector2 t) => t.X >= RoomWidth && t.X < UpperX;
            var shift = new Vector2(UpperX - OldUpperX, 0);
            foreach (var pair in loc.objects.Pairs.Where(p => Old(p.Key)).ToList())
            {
                loc.objects.Remove(pair.Key);
                pair.Value.TileLocation = pair.Key + shift;
                loc.objects[pair.Key + shift] = pair.Value;
            }
            foreach (var f in loc.furniture.Where(f => Old(f.TileLocation)).ToList())
            {
                f.TileLocation += shift;
                f.boundingBox.Value = new Microsoft.Xna.Framework.Rectangle((int)f.TileLocation.X * 64, (int)f.TileLocation.Y * 64, f.boundingBox.Value.Width, f.boundingBox.Value.Height);
                f.updateDrawPosition();
            }
            return true;
        });
    }
    private static string T(string key) => BsvI18n.Get(Helper, "two-story-shed." + key);

    private static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("Data/Buildings"))
            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, BuildingData>().Data;
                if (!data.TryGetValue("Big Shed", out var big)) { Monitor.LogOnce("BSV two-story shed: no Big Shed building data; the upgrade is not offered.", LogLevel.Warn); return; }
                // Shallow copy: the shed shares the Big Shed's look and rules; every field changed below gets a new value.
                var shed = (BuildingData)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(big, null)!;
                shed.Name = T("name");
                shed.Description = T("description");
                shed.BuildingToUpgrade = "Big Shed";
                shed.BuildCost = Gold;
                shed.BuildDays = 1;
                shed.BuildMaterials = new List<BuildingMaterial> { new() { ItemId = "(O)388", Amount = Wood }, new() { ItemId = "(O)390", Amount = Stone } };
                shed.IndoorMap = Id;
                data[Id] = shed;
            }, BsvAssets.EditPriority);
        else if (e.NameWithoutLocale.IsEquivalentTo(MapAsset))
            e.LoadFrom(() =>
            {
                var big = Helper.GameContent.Load<Map>("Maps/Shed2");
                try { return Build(big); }
                catch (Exception ex)
                {
                    Monitor.Log("BSV could not build the two-story shed from this Big Shed map; it stays one floor. " + ex.Message, LogLevel.Warn);
                    return GreenhouseRooms.CopyOf(big, Id);
                }
            }, AssetLoadPriority.Exclusive);
    }

    /// <summary>The two floors side by side, far apart; the gap between them is solid black.</summary>
    internal static Map Build(Map big)
    {
        Layer srcBack = big.GetLayer("Back");
        if (srcBack.LayerWidth != RoomWidth || srcBack.LayerHeight != RoomHeight)
            throw new MapConflictException($"Expected the vanilla 19 x 17 Big Shed, found {srcBack.LayerWidth} x {srcBack.LayerHeight}.");
        int width = UpperX + RoomWidth, height = RoomHeight + 2;
        var map = GreenhouseRooms.Blank(big, Id, width, height);
        foreach (var src in big.Layers)
        {
            var dst = map.GetLayer(src.Id);
            for (int y = 0; y < RoomHeight; y++)
                for (int x = 0; x < RoomWidth; x++)
                {
                    dst.Tiles[x, y] = GreenhouseRooms.Copy(src.Tiles[x, y], dst);
                    var upper = GreenhouseRooms.Copy(src.Tiles[x, y], dst);
                    // The upper floor's wallpaper and flooring are decorated separately from the ground floor.
                    if (upper != null)
                        foreach (var key in new[] { "FloorID", "WallID" })
                            if (upper.Properties.TryGetValue(key, out var v)) upper.Properties[key] = v.ToString() + "2";
                    dst.Tiles[UpperX + x, y] = upper;
                }
        }
        foreach (var p in big.Properties) map.Properties[p.Key] = p.Value;
        map.Properties["FloorIDs"] = AddIds(big, "FloorIDs", "Floor");
        map.Properties["WallIDs"] = AddIds(big, "WallIDs", "Wall");
        map.Properties[MarkerKey] = "T";
        Layer back = map.GetLayer("Back"), buildings = map.GetLayer("Buildings"), front = map.GetLayer("Front");
        // Upper floor: the bottom wall continues where the door is downstairs.
        int door = Enumerable.Range(0, RoomWidth).FirstOrDefault(x => buildings.Tiles[x, RoomHeight - 1] == null && back.Tiles[x, RoomHeight - 1] != null, -1);
        if (door < 0) throw new MapConflictException("The Big Shed door was not found.");
        for (int x = door - 1; x <= door + 1; x++)
        {
            front.Tiles[UpperX + x, RoomHeight - 2] = GreenhouseRooms.New(front, Sheet, 165);
            back.Tiles[UpperX + x, RoomHeight - 1] = null; front.Tiles[UpperX + x, RoomHeight - 1] = null;
            buildings.Tiles[UpperX + x, RoomHeight - 1] = GreenhouseRooms.New(buildings, Sheet, 0);
        }
        AddStairs(map, StairX);
        AddStairs(map, UpperX + StairX);
        // Everything empty on every layer (the gap, the rows under the rooms) is solid black.
        GreenhouseRooms.FillVoid(map, map.Layers.ToList());
        return map;
    }
    private static string AddIds(Map big, string key, string id)
    {
        string own = big.Properties.TryGetValue(key, out var v) ? v.ToString() : id;
        return string.Join(", ", own.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
            .Concat(own.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Select(s => s.Split(' ')[0] + "2")));
    }
    /// <summary>Stairs in the bottom wall, the same tiles as the farmhouse cellar stairs (columns x, x + 1).</summary>
    private static void AddStairs(Map map, int x0)
    {
        Layer back = map.GetLayer("Back"), buildings = map.GetLayer("Buildings"), front = map.GetLayer("Front");
        int l = x0 - 1, r = x0 + 2, y = StairTop;
        void Set(Layer layer, int x, int yy, int index) => layer.Tiles[x, yy] = index < 0 ? null : GreenhouseRooms.New(layer, Sheet, index);
        Set(front, l, y - 1, 162); Set(front, r, y - 1, 163); Set(front, x0, y - 1, -1); Set(front, x0 + 1, y - 1, -1);
        for (int x = x0; x <= x0 + 1; x++)
        {
            Set(back, x, y, 1043); Set(buildings, x, y, -1); Set(front, x, y, -1);
            Set(back, x, y + 1, 1075); Set(buildings, x, y + 1, -1); Set(front, x, y + 1, 165);
        }
        Set(back, l, y, 0); Set(buildings, l, y, 64); Set(front, l, y, 64); Set(back, r, y, 0); Set(buildings, r, y, 68); Set(front, r, y, 68);
        Set(back, l, y + 1, 0); Set(buildings, l, y + 1, 96); Set(front, l, y + 1, 96); Set(back, r, y + 1, 0); Set(buildings, r, y + 1, 130); Set(front, r, y + 1, 130);
        for (int x = l; x <= r; x++) { Set(back, x, y + 2, 0); Set(buildings, x, y + 2, 0); Set(front, x, y + 2, 0); }
    }

    /// <summary>Walking onto the lower step of either staircase moves the farmer to the other floor (same shed).</summary>
    private static void Stairs()
    {
        if (!Context.IsWorldReady || !Context.IsPlayerFree || Game1.locationRequest != null) return;
        var who = Game1.player; var here = who.currentLocation;
        if (here?.Map?.Properties.ContainsKey(MarkerKey) != true) return;
        var tile = who.TilePoint;
        if (tile.Y != StairTop + 1) return;
        int target = tile.X is StairX or StairX + 1 ? tile.X + UpperX : tile.X is UpperX + StairX or UpperX + StairX + 1 ? tile.X - UpperX : -1;
        if (target < 0) return;
        Game1.playSound("stairsdown");
        Game1.warpFarmer(Game1.getLocationRequest(here.NameOrUniqueName, here.isStructure.Value), target, StairTop - 1, 0);
    }
}

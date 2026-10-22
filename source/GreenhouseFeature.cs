using System;
using System.Linq;
using System.IO;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using HarmonyLib;

namespace BetterStardewValley;

internal sealed class GreenhouseFeature
{
    private readonly IMonitor monitor;
    private readonly string backupRoot;
    private bool loggedMapError;
    private bool loggedMigrationError;
    private readonly Func<bool> enabled;
    private bool loggedRetained;
    private readonly IModHelper helper;
    private string? retainedStamp;

    internal GreenhouseFeature(IModHelper helper, IMonitor monitor, Func<bool>? enabled = null)
    {
        this.monitor = monitor;
        this.helper = helper;
        this.enabled = enabled ?? (() => true);
        backupRoot = Path.Combine(helper.DirectoryPath, "backups", "greenhouse-0.2");
        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.SaveLoaded += (_, _) => MigrateIfNeeded();
        helper.Events.GameLoop.DayStarted += (_, _) => MigrateIfNeeded();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            loggedMigrationError = false; loggedRetained = false; loggedMapError = false; retainedStamp = null;
            helper.GameContent.InvalidateCache("Maps/Greenhouse");
        };
        helper.Events.Player.Warped += (_, e) => { if (e.IsLocalPlayer && e.NewLocation.NameOrUniqueName == "Greenhouse") { MigrateIfNeeded(); DiagnoseSoil(e.NewLocation); } };
        // Diagnostic: what actually happens when a hoe or watering can is used in the greenhouse.
        try
        {
            Diagnostics = monitor;
            var h = new Harmony(helper.ModRegistry.ModID + ".GreenhouseTools");
            foreach (var type in new[] { typeof(Hoe), typeof(WateringCan) })
                h.Patch(AccessTools.Method(type, nameof(Tool.DoFunction)), postfix: new HarmonyMethod(typeof(GreenhouseFeature), nameof(AfterTool)));
        }
        catch (Exception ex) { monitor.Log("[BSV greenhouse soil] tool diagnostic unavailable: " + ex.Message, LogLevel.Trace); }
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if ((!enabled() && retainedStamp == null) || !e.NameWithoutLocale.IsEquivalentTo("Maps/Greenhouse")) return;
        e.Edit(asset =>
        {
            try
            {
                GreenhouseMap.Expand(asset.AsMap().Data, enabled() ? null : retainedStamp);
                monitor.Log("BSV greenhouse: 24 x 20 = 480 planting tiles.", LogLevel.Trace);
            }
            catch (MapConflictException ex) when (enabled())
            {
                // Another mod's greenhouse: BSV leaves it exactly as that mod made it. This is expected with
                // map replacement mods, so it is a one-line warning, not an error (players can switch it off).
                if (!loggedMapError)
                {
                    loggedMapError = true;
                    monitor.Log(BsvI18n.Get(helper,"diag.greenhouse-foreign")+" ("+ex.Message+")", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                if (!loggedMapError)
                {
                    loggedMapError = true;
            monitor.Log(BsvI18n.Get(helper,"diag.map-error")+"\n"+BsvI18n.Get(helper,"diag.greenhouse-conflict")+$"\n{ex}", LogLevel.Error);
                }
            }
        }, AssetEditPriority.Late);
    }

    private void MigrateIfNeeded()
    {
        if (!Context.IsWorldReady) return;
        try
        {
            GameLocation? location = Game1.getLocationFromName("Greenhouse");
            if (location is null) return;
            // An opt-out must not shrink a previously expanded save around its contents.
            // Restore only the exact recorded layout on a recognized base map. No item
            // movement, reverse migration, config write or foreign-map replacement here.
            if (!enabled() && location.modData.TryGetValue(GreenhouseMap.LayoutKey, out string retained))
            {
                retainedStamp = retained;
                if (!location.Map.Properties.TryGetValue(GreenhouseMap.LayoutKey, out var current) || current.ToString() != retained)
                {
                    if (loggedMigrationError) return;
                    helper.GameContent.InvalidateCache("Maps/Greenhouse");
                    // Use the native loader to rebuild water, light and map caches too.
                    location.loadMap(location.mapPath.Value, force_reload: true);
                    location.updateWarps();
                }
                if (!location.Map.Properties.TryGetValue(GreenhouseMap.LayoutKey, out current) || current.ToString() != retained)
                    throw new InvalidOperationException("Recorded greenhouse could not be restored on this map. Original items retained; restore the previous map setup before saving.");
                if (!loggedRetained)
                {
                    loggedRetained = true;
                    monitor.Log(BsvI18n.Get(helper,"diag.map-retained"), LogLevel.Warn);
                }
                return;
            }
            if (!enabled()) return;
            if (!Context.IsMainPlayer) return;
            if (!location.Map.Properties.TryGetValue(GreenhouseMap.LayoutKey, out var stamp))
            {
                if (location.modData.ContainsKey(GreenhouseMap.LayoutKey))
                    throw new InvalidOperationException("This save uses BSV's expanded greenhouse but its map is unavailable. Restore the expanded map before saving.");
                return;
            }
            var layout = GreenhouseLayout.Parse(stamp.ToString());
            if (location.modData.TryGetValue(GreenhouseMap.LayoutKey, out string previous))
            {
                if (previous != layout.Stamp)
                    throw new InvalidOperationException("The greenhouse base layout changed after migration. Restore the previous map setup.");
                return;
            }
            string? backup = GreenhouseBackup.Create(Constants.CurrentSavePath, backupRoot);
            if (backup is not null) monitor.Log("BSV saved a pre-expansion backup to: " + backup, LogLevel.Debug);
            Migrate(location, layout);
            location.modData[GreenhouseMap.LayoutKey] = layout.Stamp;
            monitor.Log("BSV greenhouse expanded to 480 planting tiles. Existing crops kept their positions; right/bottom perimeter items moved with the walls.", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            if (!loggedMigrationError)
            {
                loggedMigrationError = true;
            monitor.Log(BsvI18n.Get(helper,"diag.map-error")+$"\nGreenhouse migration: {ex}", LogLevel.Error);
            }
        }
    }

    /// <summary>Logs, once per visit, how the game itself judges every tile of the planting bed: diggable,
    /// passable, occupied, and whether seeds may be planted. Read-only; nothing is changed.</summary>
    private void DiagnoseSoil(GameLocation location)
    {
        try
        {
            if (!location.Map.Properties.TryGetValue(GreenhouseMap.LayoutKey, out var stamp)) return;
            var layout = GreenhouseLayout.Parse(stamp.ToString());
            int diggable = 0, blocked = 0, occupied = 0, plantable = 0, total = 0;
            var problems = new List<string>();
            string? denied = null;
            for (int y = layout.SoilY; y < layout.SoilY + 10 + GreenhouseLayout.AddedHeight; y++)
                for (int x = layout.SoilX; x < layout.SoilX + 12 + GreenhouseLayout.AddedWidth; x++)
                {
                    total++;
                    var tile = new Vector2(x, y);
                    bool dig = location.doesTileHaveProperty(x, y, "Diggable", "Back") != null;
                    bool pass = location.isTilePassable(tile);
                    bool occ = location.IsTileOccupiedBy(tile);
                    bool plant = location.CanPlantSeedsHere("(O)472", x, y, false, out string why);
                    if (dig) diggable++; if (!pass) blocked++; if (occ) occupied++; if (plant) plantable++; else denied ??= why;
                    if ((!dig || !pass || occ || !plant) && problems.Count < 12) problems.Add($"{x},{y}:{(dig ? "" : "notDiggable ")}{(pass ? "" : "blocked ")}{(occ ? "occupied " : "")}{(plant ? "" : "noPlant")}");
                }
            var back = location.Map.GetLayer("Back");
            monitor.Log($"[BSV greenhouse soil] map {back.LayerWidth}x{back.LayerHeight}, bed {total} tiles: diggable {diggable}, blocked {blocked}, occupied {occupied}, plantable {plantable}"
                + (denied != null ? $"; plant denied: {denied}" : "") + (problems.Count > 0 ? "; first problems: " + string.Join(" | ", problems) : "; all tiles OK"),
                diggable == total && blocked == 0 && plantable == total ? LogLevel.Debug : LogLevel.Warn);
        }
        catch (Exception ex) { monitor.Log("[BSV greenhouse soil] check failed: " + ex.Message, LogLevel.Debug); }
    }

    private static IMonitor? Diagnostics;
    private static void AfterTool(Tool __instance, GameLocation location, int x, int y, int power, Farmer who)
    {
        try
        {
            if (location?.NameOrUniqueName != "Greenhouse" || Diagnostics == null) return;
            var tile = new Vector2(x / 64, y / 64);
            location.terrainFeatures.TryGetValue(tile, out var feature);
            string state = feature is HoeDirt dirt ? $"tilled (watered={dirt.state.Value == 1}, crop={(dirt.crop != null)})" : feature?.GetType().Name ?? "untilled";
            Diagnostics.Log($"[BSV greenhouse soil] {__instance.GetType().Name} at {tile.X},{tile.Y} power {power}: diggable={location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Diggable", "Back") != null}, passable={location.isTilePassable(tile)}, now {state}"
                + (__instance is WateringCan can ? $", water left {can.WaterLeft}" : ""), LogLevel.Debug);
        }
        catch { }
    }

    private static Vector2 Move(Vector2 tile, GreenhouseLayout layout) =>
        new(layout.MoveX((int)tile.X), layout.MoveY((int)tile.Y));
    private static Vector2 MovePixels(Vector2 position, GreenhouseLayout layout) =>
        new(layout.MovePixelX(position.X), layout.MovePixelY(position.Y));

    private static void Migrate(GameLocation location, GreenhouseLayout layout)
    {
        // Move saved instances instead of reconstructing items or crops. This preserves
        // contents, fruit, fertilizer, quality, machine timers, and other mods' data.
        var objects = location.objects.Pairs.ToArray();
        var terrain = location.terrainFeatures.Pairs.ToArray();
        var furniture = location.furniture.Select(f => (Item: f, Tile: f.TileLocation)).ToArray();
        var large = location.largeTerrainFeatures.Select(f => (Item: f, Tile: f.Tile)).ToArray();
        var clumps = location.resourceClumps.Select(f => (Item: f, Tile: f.Tile)).ToArray();
        var characters = location.characters.Select(c => (Item: c, Position: c.Position)).ToArray();
        var farmers = Game1.getAllFarmers().Where(f => f.currentLocation == location)
            .Select(f => (Item: f, Position: f.Position)).ToArray();
        try
        {
            foreach (var pair in objects)
                if (Move(pair.Key, layout) != pair.Key) location.objects.Remove(pair.Key);
            foreach (var pair in objects)
            {
                Vector2 next = Move(pair.Key, layout);
                if (next == pair.Key) continue;
                location.objects.Add(next, pair.Value);
                pair.Value.TileLocation = next;
                if (pair.Value is IndoorPot pot) pot.hoeDirt.Value.OnAdded(location, next);
            }
            foreach (var pair in terrain)
                if (Move(pair.Key, layout) != pair.Key) location.terrainFeatures.Remove(pair.Key);
            foreach (var pair in terrain)
            {
                Vector2 next = Move(pair.Key, layout);
                if (next != pair.Key) location.terrainFeatures.Add(next, pair.Value);
            }
            foreach (var pair in furniture) pair.Item.InitializeAtTile(Move(pair.Tile, layout));
            foreach (var pair in large) pair.Item.Tile = Move(pair.Tile, layout);
            foreach (var pair in clumps) pair.Item.Tile = Move(pair.Tile, layout);
            foreach (var pair in characters) pair.Item.Position = MovePixels(pair.Position, layout);
            foreach (var pair in farmers) pair.Item.Position = MovePixels(pair.Position, layout);
        }
        catch
        {
            // Restore original instances and coordinates if a move fails. The caller
            // records the migration stamp only after the whole operation succeeds.
            location.objects.Clear();
            foreach (var pair in objects)
            {
                location.objects.Add(pair.Key, pair.Value);
                pair.Value.TileLocation = pair.Key;
                if (pair.Value is IndoorPot pot) pot.hoeDirt.Value.OnAdded(location, pair.Key);
            }
            location.terrainFeatures.Clear();
            foreach (var pair in terrain) location.terrainFeatures.Add(pair.Key, pair.Value);
            foreach (var pair in furniture) pair.Item.InitializeAtTile(pair.Tile);
            foreach (var pair in large) pair.Item.Tile = pair.Tile;
            foreach (var pair in clumps) pair.Item.Tile = pair.Tile;
            foreach (var pair in characters) pair.Item.Position = pair.Position;
            foreach (var pair in farmers) pair.Item.Position = pair.Position;
            throw;
        }
    }
}

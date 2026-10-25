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
        helper.Events.Player.Warped += (_, e) => { if (e.IsLocalPlayer && e.NewLocation.NameOrUniqueName == "Greenhouse") { MigrateIfNeeded(); } };
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if ((!enabled() && retainedStamp == null) || !e.NameWithoutLocale.IsEquivalentTo("Maps/Greenhouse")) return;
        e.Edit(asset =>
        {
            try
            {
                GreenhouseMap.Expand(asset.AsMap().Data, enabled() ? null : retainedStamp);
                monitor.Log("BSV greenhouse map expanded.", LogLevel.Trace);
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
        }, BetterStardewValley.BsvAssets.EditPriority);
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
                if (previous == layout.Stamp) return;
                // A save expanded by an older BSV (24 x 20): widen it in place. Everything inside the old bed keeps
                // its tile; the right wall and anything beyond it moves out with the wall.
                var older = GreenhouseLayout.Parse(previous);
                if (!layout.Grows(older))
                    throw new InvalidOperationException("The greenhouse base layout changed after migration. Restore the previous map setup.");
                string? before = GreenhouseBackup.Create(Constants.CurrentSavePath, backupRoot);
                if (before is not null) monitor.Log("BSV saved a pre-upgrade greenhouse backup to: " + before, LogLevel.Debug);
                Migrate(location, t => new(layout.UpgradeX(older, (int)t.X), layout.UpgradeY(older, (int)t.Y)),
                    p => new(p.X < (older.CutX + older.AddedWidth) * 64 ? p.X : p.X + (layout.AddedWidth - older.AddedWidth) * 64,
                        p.Y < (older.CutY + older.AddedHeight) * 64 ? p.Y : p.Y + (layout.AddedHeight - older.AddedHeight) * 64));
                location.modData[GreenhouseMap.LayoutKey] = layout.Stamp;
                monitor.Log($"BSV greenhouse upgraded from {older.Tiles} to {layout.Tiles} planting tiles ({layout.BedWidth} x {layout.BedHeight}). Crops kept their positions; the right wall and what stood beyond it moved out.", LogLevel.Debug);
                return;
            }
            string? backup = GreenhouseBackup.Create(Constants.CurrentSavePath, backupRoot);
            if (backup is not null) monitor.Log("BSV saved a pre-expansion backup to: " + backup, LogLevel.Debug);
            Migrate(location, t => Move(t, layout), p => MovePixels(p, layout));
            location.modData[GreenhouseMap.LayoutKey] = layout.Stamp;
            monitor.Log($"BSV greenhouse expanded to {layout.Tiles} planting tiles ({layout.BedWidth} x {layout.BedHeight}). Existing crops kept their positions; right/bottom perimeter items moved with the walls.", LogLevel.Debug);
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

    private static Vector2 Move(Vector2 tile, GreenhouseLayout layout) =>
        new(layout.MoveX((int)tile.X), layout.MoveY((int)tile.Y));
    private static Vector2 MovePixels(Vector2 position, GreenhouseLayout layout) =>
        new(layout.MovePixelX(position.X), layout.MovePixelY(position.Y));

    private static void Migrate(GameLocation location, Func<Vector2, Vector2> Move, Func<Vector2, Vector2> MovePixels)
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
                if (Move(pair.Key) != pair.Key) location.objects.Remove(pair.Key);
            foreach (var pair in objects)
            {
                Vector2 next = Move(pair.Key);
                if (next == pair.Key) continue;
                location.objects.Add(next, pair.Value);
                pair.Value.TileLocation = next;
                if (pair.Value is IndoorPot pot) pot.hoeDirt.Value.OnAdded(location, next);
            }
            foreach (var pair in terrain)
                if (Move(pair.Key) != pair.Key) location.terrainFeatures.Remove(pair.Key);
            foreach (var pair in terrain)
            {
                Vector2 next = Move(pair.Key);
                if (next != pair.Key) location.terrainFeatures.Add(next, pair.Value);
            }
            foreach (var pair in furniture) pair.Item.InitializeAtTile(Move(pair.Tile));
            foreach (var pair in large) pair.Item.Tile = Move(pair.Tile);
            foreach (var pair in clumps) pair.Item.Tile = Move(pair.Tile);
            foreach (var pair in characters) pair.Item.Position = MovePixels(pair.Position);
            foreach (var pair in farmers) pair.Item.Position = MovePixels(pair.Position);
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

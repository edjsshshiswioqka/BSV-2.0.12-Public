using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace BetterStardewValley;

/// <summary>
/// BSV's Tea Sapling and Grape Starter have their own IDs (copies of the game's). Saplings and starters a player already
/// has become BSV's (backpacks and chests when a save loads, anything picked up later), and grapes already planted keep
/// growing by BSV's rules. The game's Grape Starter is only converted while it is still the game's grape crop: when
/// another mod turned it into something else, it stays that mod's.
/// </summary>
internal static class OwnSeeds
{
    private static IMonitor? Monitor;

    internal static void Install(IModHelper helper, IMonitor monitor)
    {
        Monitor = monitor;
        helper.Events.GameLoop.SaveLoaded += (_, _) => Guard(World);
        helper.Events.Player.InventoryChanged += (_, e) =>
        {
            if (e.IsLocalPlayer && e.Added.Any(i => Replacement(i) != null)) Guard(() => Convert(e.Player.Items));
        };
    }

    private static void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { Monitor?.LogOnce("BSV could not convert Tea Saplings / Grape Starters to BSV's own: " + ex, LogLevel.Warn); }
    }

    /// <summary>The game's grape is still an ordinary grape crop, and BSV's grape crop exists.</summary>
    private static bool GrapesConvert() =>
        Game1.cropData != null && Game1.cropData.ContainsKey(WineRules.StarterId)
        && Game1.cropData.TryGetValue(WineRules.GrapeSeedId, out var grape) && grape.HarvestItemId is WineRules.GrapeId or "(O)" + WineRules.GrapeId;

    private static string? Replacement(Item? item) => item?.QualifiedItemId switch
    {
        TeaAppleRules.VanillaSapling => TeaAppleRules.Sapling,
        "(O)" + WineRules.GrapeSeedId when GrapesConvert() => WineRules.Starter,
        _ => null
    };

    private static void Convert(IList<Item?> items)
    {
        for (int i = 0; i < items.Count; i++)
            if (Replacement(items[i]) is { } id)
                items[i] = ItemRegistry.Create(id, items[i]!.Stack, items[i]!.Quality);
    }

    private static void World()
    {
        if (!Context.IsWorldReady) return;
        Convert(Game1.player.Items);
        if (!Context.IsMainPlayer) return;
        bool grapes = GrapesConvert();
        int planted = 0;
        Utility.ForEachLocation(location =>
        {
            foreach (var obj in location.objects.Values)
            {
                if (obj is Chest chest) Convert(chest.GetItemsForPlayer());
                else if (obj is IndoorPot pot && Replant(pot.hoeDirt.Value)) planted++;
            }
            if (location.GetFridge(false) is { } fridge) Convert(fridge.GetItemsForPlayer());
            foreach (var feature in location.terrainFeatures.Values)
                if (feature is HoeDirt dirt && Replant(dirt)) planted++;
            return true;
        }, includeInteriors: true, includeGenerated: true);
        if (planted > 0) Monitor?.Log($"BSV grapes: {planted} planted Grape Starter(s) now grow as BSV's Grape Starter.", LogLevel.Debug);

        bool Replant(HoeDirt? dirt)
        {
            if (!grapes || dirt?.crop is not { } crop || crop.netSeedIndex.Value != WineRules.GrapeSeedId) return false;
            crop.netSeedIndex.Value = WineRules.StarterId;
            return true;
        }
    }
}

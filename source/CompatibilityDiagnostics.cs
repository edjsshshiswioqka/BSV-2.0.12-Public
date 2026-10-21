using StardewModdingAPI;
using StardewValley;

namespace BetterStardewValley;

// Read-only, explicit diagnostics. No dependency detection by files, no implicit adapter
// claims and no world scan from startup, rendering or normal update ticks.
internal static class CompatibilityDiagnostics
{
    internal static void Register(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        helper.ConsoleCommands.Add("bsv_compat_status", BsvI18n.Get(helper, "compat.audit.command"), (_, _) =>
        {
            void Log(string key, object? tokens = null) => monitor.Log(BsvI18n.Get(helper, key, tokens), LogLevel.Info);
            Log("compat.audit.header");
            foreach (var mod in helper.ModRegistry.GetAll().OrderBy(m => m.Manifest.UniqueID, StringComparer.Ordinal))
                monitor.Log($"{mod.Manifest.UniqueID} {mod.Manifest.Version}; loaded={helper.ModRegistry.IsLoaded(mod.Manifest.UniqueID)}", LogLevel.Info);
            Log("compat.audit.maps", new { greenhouse = config.EnableGreenhouseExpansion, cellar = config.EnableCellarExpansion });
            Log("compat.audit.orders", new { ids = string.Join(", ", config.OrderItemExclusions ?? Array.Empty<string>()) });
            if (!Context.IsWorldReady) return;
            monitor.Log(AfkFishing.AfkFishingService.CompatibilityStatus, LogLevel.Info);
            Utility.ForEachLocation(location =>
            {
                foreach (string key in new[] { GreenhouseMap.LayoutKey, CellarMap.LayoutKey })
                    if (location.modData.TryGetValue(key, out var saved))
                        monitor.Log($"{location.NameOrUniqueName}: {key}; saved={saved}; loaded="
                            + (location.Map?.Properties.TryGetValue(key, out var stamp) == true ? stamp.ToString() : "unavailable"), LogLevel.Info);
                return true;
            }, includeInteriors: true, includeGenerated: true);
        });
    }
}

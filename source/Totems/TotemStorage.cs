using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace BetterStardewValley.Totems;

// A — additive native modData fields: no new serializable game types or replacement save format.
internal static class TotemStorage
{
    private static string? CachedRaw;
    private static TotemState? CachedState;
    internal static int Season => checked((Game1.year - 1) * 4 + (int)Game1.season);
    internal static TotemState Read()
    {
        Game1.getFarm().modData.TryGetValue(TotemRules.SharedKey, out string raw);
        if (raw == null) return new();
        if (raw != CachedRaw || CachedState == null)
        {
            // Missing fields in a nonempty record must not silently reset a cooldown.
            var data = JObject.Parse(raw);
            if (new[] { "Schema", "Harvest", "Growth", "Processing", "StormDay" }.Any(k => data[k]?.Type != JTokenType.Integer))
                throw new InvalidOperationException("Incomplete BSV totem save state; preserved.");
            CachedState = data.ToObject<TotemState>() ?? throw new InvalidOperationException("Empty totem state.");
            CachedRaw = raw;
        }
        if (!CachedState.Valid(Season, Game1.Date.TotalDays)) throw new InvalidOperationException("Invalid/future totem save state; preserved.");
        return CachedState;
    }
    internal static bool Legal(Crop? crop) => crop != null && !crop.dead.Value && !crop.forageCrop.Value
        && !crop.isWildSeedCrop() && !string.IsNullOrWhiteSpace(crop.netSeedIndex.Value)
        && !string.IsNullOrWhiteSpace(crop.indexOfHarvest.Value) && crop.GetData() != null;
    internal static bool Blessed(Crop? crop, GameLocation? location)
    {
        if (!Context.IsWorldReady || !ReferenceEquals(location, Game1.getFarm()) || !Legal(crop)) return false;
        if (!crop!.modData.TryGetValue(TotemRules.BlessingKey, out string raw)) return false;
        try { return JsonConvert.DeserializeObject<CropBlessing>(raw)?.Matches(crop.netSeedIndex.Value) == true; }
        catch { return false; }
    }
    internal static int Quality(Crop crop, HoeDirt? soil, GameLocation location)
        => TotemRules.QualityFloor(Blessed(crop, location), soil?.GetFertilizerQualityBoostLevel() ?? 0);
    internal static void Reset() { CachedRaw = null; CachedState = null; }
}

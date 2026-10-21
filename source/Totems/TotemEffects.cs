using BetterStardewValley.Transactions;
using Eddie.FishingMachine;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using MachineMod = Eddie.FishingMachine.ModEntry;
using SObject = StardewValley.Object;

namespace BetterStardewValley.Totems;

// A — one-shot plans. Scans Farm only and snapshots targets before the transaction.
internal static class TotemEffects
{
    private static bool Live(GameLocation map, SObject obj) => map.objects.TryGetValue(obj.TileLocation, out var live) && ReferenceEquals(live,obj);
    internal sealed record Plan(List<AtomicStep> Steps, Func<bool> Valid);
    internal static IEnumerable<(Vector2 Tile, HoeDirt Soil, Crop Crop)> Crops(GameLocation farm)
    {
        var seen = new HashSet<Crop>();
        foreach (var p in farm.terrainFeatures.Pairs)
            if (p.Value is HoeDirt soil && TotemStorage.Legal(soil.crop) && seen.Add(soil.crop)) yield return (p.Key, soil, soil.crop);
        foreach (var p in farm.objects.Pairs)
            if (p.Value is IndoorPot pot && TotemStorage.Legal(pot.hoeDirt.Value.crop) && seen.Add(pot.hoeDirt.Value.crop))
                yield return (p.Key, pot.hoeDirt.Value, pot.hoeDirt.Value.crop);
    }
    internal static Chest[] Machines(GameLocation farm)
    {
        var mod = MachineMod.Instance;
        if (mod?.Ready != true) return Array.Empty<Chest>();
        return farm.objects.Values.OfType<Chest>().Where(c => MachineMod.IsAnchor(c) && mod.Production.Read(c).Phase == "working")
            .OrderBy(c => c.TileLocation.Y).ThenBy(c => c.TileLocation.X).ToArray();
    }
    internal static Plan Prepare(TotemKind kind, GameLocation farm, IReadOnlyList<Chest> machines)
    {
        var steps = new List<AtomicStep>(); var checks = new List<Func<bool>>();
        if (kind is TotemKind.Harvest or TotemKind.Growth)
        {
            foreach (var entry in Crops(farm).ToArray())
            {
                var (tile, soil, crop) = entry;
                bool Live() => ReferenceEquals(soil.crop, crop) && TotemStorage.Legal(crop)
                    && ((farm.terrainFeatures.TryGetValue(tile, out var t) && ReferenceEquals(t, soil))
                        || (farm.objects.TryGetValue(tile, out var o) && o is IndoorPot p && ReferenceEquals(p.hoeDirt.Value, soil)));
                if (kind == TotemKind.Harvest)
                {
                    if (TotemStorage.Blessed(crop, farm)) continue;
                    crop.modData.TryGetValue(TotemRules.BlessingKey, out string before);
                    string after = JsonConvert.SerializeObject(new CropBlessing(1, Guid.NewGuid().ToString("N"), crop.netSeedIndex.Value));
                    steps.Add(new(() => crop.modData[TotemRules.BlessingKey] = after,
                        () => { if (before == null) crop.modData.Remove(TotemRules.BlessingKey); else crop.modData[TotemRules.BlessingKey] = before; }));
                    checks.Add(Live);
                }
                else
                {
                    int phase = crop.currentPhase.Value, day = crop.dayOfCurrentPhase.Value;
                    bool regrow = crop.fullyGrown.Value; var durations = crop.phaseDays.ToArray();
                    var next = TotemRules.Growth(durations, phase, day, regrow);
                    if (next == null || next.Value == (phase, day)) continue;
                    steps.Add(new(() => { crop.currentPhase.Value = next.Value.Phase; crop.dayOfCurrentPhase.Value = next.Value.Day; },
                        () => { crop.currentPhase.Value = phase; crop.dayOfCurrentPhase.Value = day; }));
                    checks.Add(() => Live() && crop.fullyGrown.Value == regrow && crop.phaseDays.SequenceEqual(durations));
                }
            }
        }
        else if (kind == TotemKind.Processing)
        {
            foreach (var obj in farm.objects.Values.ToArray())
            {
                if (obj.GetType() != typeof(SObject) || !ProcessingPolicy.SupportsProcessingTotem(obj.QualifiedItemId, obj.GetMachineData())
                    || obj.readyForHarvest.Value || obj.MinutesUntilReady <= 0 || obj.heldObject.Value == null) continue;
                int before = obj.MinutesUntilReady, after = (int)TotemRules.Shorten(before);
                if (before == after) continue;
                var output = obj.heldObject.Value;
                steps.Add(new(() => obj.MinutesUntilReady = after, () => obj.MinutesUntilReady = before));
                checks.Add(() => Live(farm, obj) && ReferenceEquals(obj.heldObject.Value, output) && !obj.readyForHarvest.Value);
            }
            var mod = MachineMod.Instance;
            if (mod?.Ready == true)
                foreach (var machine in machines)
                {
                    var step = mod.Production.TotemTiming(machine);
                    if (step == null) continue;
                    steps.Add(step);
                    checks.Add(() => Live(farm, machine) && machine.GetMutex().IsLockHeld());
                }
        }
        else if (kind == TotemKind.Thunderstorm)
        {
            if (!SafeWeather()) throw new TotemRefusal("totem.weather-blocked");
            var weather = Game1.netWorldState.Value.GetWeatherForLocation("Default");
            string local = Game1.weatherForTomorrow, global = Game1.netWorldState.Value.WeatherForTomorrow, regional = weather.WeatherForTomorrow;
            steps.Add(new(() => { Game1.weatherForTomorrow = "Storm"; Game1.netWorldState.Value.WeatherForTomorrow = "Storm"; weather.WeatherForTomorrow = "Storm"; },
                () => { Game1.weatherForTomorrow = local; Game1.netWorldState.Value.WeatherForTomorrow = global; weather.WeatherForTomorrow = regional; }));
        }
        if (steps.Count == 0) throw new TotemRefusal("totem.no-targets");
        return new(steps, () => checks.All(check => check()));
    }
    private static bool SafeWeather()
    {
        var date = new WorldDate(Game1.Date) { TotalDays = Game1.Date.TotalDays + 1 };
        if (Game1.getFarm().GetLocationContextId() != "Default") return false;
        // Ask the native fixed-date policy using different ordinary forecasts. Any forced result blocks worship.
        foreach (string forecast in new[] { "Sun", "Rain", "Storm" })
            if (Game1.getWeatherModificationsForDate(date, forecast) != forecast) return false;
        if (Utility.TryGetPassiveFestivalDataForDay(date.DayOfMonth, date.Season, "Default", out _, out _)) return false;
        var forecasts = new[] { Game1.weatherForTomorrow, Game1.netWorldState.Value.WeatherForTomorrow,
            Game1.netWorldState.Value.GetWeatherForLocation("Default").WeatherForTomorrow };
        if (forecasts.Any(x => x is not ("Sun" or "Rain" or "Wind" or "Snow" or "Storm"))) return false;
        // Scheduled NPC or player weddings are protected, including overdue weddings awaiting a valid day.
        var farmers = Game1.getAllFarmers().ToArray();
        bool Wedding(Friendship f) => f.IsEngaged() && f.WeddingDate != null && f.WeddingDate.TotalDays <= date.TotalDays;
        foreach (var farmer in farmers)
        {
            if (farmer.friendshipData.Values.Any(Wedding)) return false;
            if (farmer.team.friendshipData.Values.Any(Wedding)) return false;
        }
        // Unknown weather patches are not presumed safe. No third-party weather code is inspected or copied.
        var patched = Harmony.GetPatchInfo(AccessTools.Method(typeof(Game1), nameof(Game1.getWeatherModificationsForDate)));
        return patched == null || !patched.Owners.Any();
    }
}
internal sealed class TotemRefusal : Exception
{
    internal string Key { get; }
    internal TotemRefusal(string key) : base(key) => Key = key;
}

using System.Text.Json;
namespace BetterStardewValley.Totems;

// A — author-defined recipes and BSV's original per-player mail state machine.
internal sealed record TotemRecipe(TotemKind Kind, int Farming, string Ingredients)
{
    internal string Id => TotemRules.Id(Kind);
    internal string Mail => Id + "_WizardRecipe";
    internal string Key => "totem." + Kind.ToString().ToLowerInvariant();
    internal string Data(string name) => Ingredients + "/Home/" + Id + " 1/true/null/" + name;
}
internal sealed class TotemUnlockEntry
{
    public int Eligible { get; set; } = -1;
    public int Scheduled { get; set; } = -1;
    public int Received { get; set; } = -1;
    public bool RecipeUnlocked { get; set; }
}
internal sealed class TotemUnlockState
{
    public int Schema { get; set; } = 1;
    public bool Battery { get; set; }
    public int Crops { get; set; }
    public int Artisan { get; set; }
    public int GoldCrops { get; set; }
    public int LastShippingDay { get; set; } = -1;
    public int LastMailDay { get; set; } = -1;
    public bool LegacyHistoryIncomplete { get; set; }
    public Dictionary<TotemKind, TotemUnlockEntry> Entries { get; set; } = TotemUnlockRules.Recipes.ToDictionary(r => r.Kind, _ => new TotemUnlockEntry());
    internal string Save() => JsonSerializer.Serialize(this);
    internal static TotemUnlockState Load(string raw)
    {
        if (raw.Length > 16000) throw new InvalidOperationException("Oversized totem unlock record; preserved.");
        using var doc = JsonDocument.Parse(raw);
        foreach (string key in new[] { "Schema", "Battery", "Crops", "Artisan", "GoldCrops", "LastShippingDay", "LastMailDay", "LegacyHistoryIncomplete", "Entries" })
            if (!doc.RootElement.TryGetProperty(key, out _)) throw new InvalidOperationException("Incomplete totem unlock record; preserved.");
        var s = JsonSerializer.Deserialize<TotemUnlockState>(raw) ?? throw new InvalidOperationException("Missing totem unlock record.");
        if (s.Schema != 1 || s.Crops is < 0 or > 50 || s.Artisan is < 0 or > 25 || s.GoldCrops is < 0 or > 100
            || s.LastShippingDay < -1 || s.LastMailDay < -1 || s.Entries == null || s.Entries.Count != 4
            || TotemUnlockRules.Recipes.Any(r => !s.Entries.TryGetValue(r.Kind, out var e) || e == null || e.Eligible < -1 || e.Scheduled < -1 || e.Received < -1
                || e.Scheduled >= 0 && (e.Eligible < 0 || e.Scheduled <= e.Eligible)))
            throw new InvalidOperationException("Invalid totem unlock record; preserved.");
        return s;
    }
}
internal static class TotemUnlockRules
{
    internal const string StateKey = TotemRules.Prefix + "TotemUnlocks.v1";
    internal static readonly TotemRecipe[] Recipes =
    {
        new(TotemKind.Thunderstorm, 6, "709 20 335 5 338 10 787 2 726 5"),
        new(TotemKind.Growth, 8, "709 30 771 100 Moss 10 465 10 725 5"),
        new(TotemKind.Processing, 9, "709 30 335 10 336 5 382 20 787 2 725 5"),
        new(TotemKind.Harvest, 10, "709 50 336 10 337 5 368 25 74 1")
    };
    internal static bool Eligible(TotemRecipe r, int farming, TotemUnlockState s) => farming >= r.Farming && r.Kind switch
    {
        TotemKind.Thunderstorm => s.Battery,
        TotemKind.Growth => s.Crops >= 50,
        TotemKind.Processing => s.Artisan >= 25,
        TotemKind.Harvest => s.GoldCrops >= 100,
        _ => false
    };
    internal static void ObserveEligibility(TotemUnlockState s, int farming, int day)
    {
        foreach (var r in Recipes)
        {
            var e = s.Entries[r.Kind];
            if (e.Eligible < 0 && !e.RecipeUnlocked && e.Received < 0 && Eligible(r, farming, s)) e.Eligible = day;
        }
    }
    // At most one reservation at a time; each eligibility is independent, not an extra prerequisite chain.
    internal static void Schedule(TotemUnlockState s, int day)
    {
        if (s.Entries.Values.Any(e => e.Scheduled >= 0 && e.Received < 0 && !e.RecipeUnlocked)) return;
        foreach (var r in Recipes)
        {
            var e = s.Entries[r.Kind];
            if (e.Eligible < 0 || e.RecipeUnlocked || e.Received >= 0) continue;
            e.Scheduled = Math.Max(e.Eligible + 1, Math.Max(day, s.LastMailDay + 1));
            return;
        }
    }
    internal static TotemRecipe? Due(TotemUnlockState s, int day) => s.LastMailDay >= day ? null
        : Recipes.FirstOrDefault(r => { var e = s.Entries[r.Kind]; return e.Scheduled >= 0 && e.Scheduled <= day && e.Received < 0 && !e.RecipeUnlocked; });
    internal static void Delivered(TotemUnlockState s, TotemRecipe r, int day)
    { s.Entries[r.Kind].Received = day; s.LastMailDay = day; }
    internal static void Shipping(TotemUnlockState s, int day, int crops, int artisan, int gold, bool battery)
    {
        if (day <= s.LastShippingDay) return;
        s.Crops = Add(s.Crops, crops, 50); s.Artisan = Add(s.Artisan, artisan, 25); s.GoldCrops = Add(s.GoldCrops, gold, 100);
        s.Battery |= battery; s.LastShippingDay = day;
    }
    private static int Add(int old, int amount, int cap) => (int)Math.Min(cap, (long)old + Math.Max(0, amount));
}

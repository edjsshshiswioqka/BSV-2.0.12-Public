namespace BetterStardewValley.AfkFishing;

internal readonly record struct AfkIngredient(string Id, int Category, bool Crop);

// Positive evidence only. A recipe is safe only if EVERY possible recipe/ingredient
// for that output is terrestrial. Unknown identifiers, category alternatives and cycles
// fail closed; display names/localized strings never participate.
internal sealed class AfkFoodRules
{
    private readonly IReadOnlyDictionary<string, AfkIngredient> Items;
    private readonly HashSet<string> Aquatic;
    private readonly Dictionary<string, List<string[]>> Recipes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> Safe = new(StringComparer.Ordinal);
    private static readonly HashSet<string> RawAquatic = new(StringComparer.Ordinal)
        { "152", "153", "157", "372", "715", "716", "717", "718", "719", "720", "721", "722", "723",
          "SeaJelly", "RiverJelly", "CaveJelly", "SmokedFish" };
    // Native land forage, animal products and pantry ingredients which lack crop/recipe data.
    // These stable IDs are evidence for raw ingredients, not a whitelist of finished dishes.
    private static readonly HashSet<string> TerrestrialRoots = new(StringComparer.Ordinal)
        { "16", "18", "20", "22", "78", "88", "90", "257", "259", "281", "296", "396", "398", "399",
          "404", "406", "408", "410", "412", "414", "416", "418", "420", "422",
          "176", "180", "182", "184", "186", "305", "430", "436", "438", "442",
          "424", "426", "245", "246", "247", "340", "419", "423", "724" };
    internal static string Plain(string id) => id.StartsWith("(O)", StringComparison.Ordinal) ? id[3..] : id;
    internal AfkFoodRules(IReadOnlyDictionary<string, AfkIngredient> items, IEnumerable<string> fish,
        IEnumerable<string> cookingRecipes)
    {
        Items = items; Aquatic = fish.Select(Plain).ToHashSet(StringComparer.Ordinal); Aquatic.UnionWith(RawAquatic);
        foreach (var raw in cookingRecipes)
        {
            var fields = raw.Split('/');
            if (fields.Length < 3) continue;
            var output = fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (output.Length == 0) continue;
            string id = Plain(output[0]);
            var tokens = fields[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] ingredients = tokens.Length > 0 && tokens.Length % 2 == 0
                && Enumerable.Range(0, tokens.Length / 2).All(i => int.TryParse(tokens[2 * i + 1], out int n) && n > 0)
                ? Enumerable.Range(0, tokens.Length / 2).Select(i => Plain(tokens[2 * i])).ToArray() : new[] { "<unknown>" };
            if (!Recipes.TryGetValue(id, out var recipes)) Recipes[id] = recipes = new();
            recipes.Add(ingredients);
        }
    }
    internal bool Allows(string id) => Known(Plain(id), new HashSet<string>(StringComparer.Ordinal));
    private bool Known(string id, HashSet<string> chain)
    {
        if (Aquatic.Contains(id) || id == "-4") return false;
        if (Safe.TryGetValue(id, out bool cached)) return cached;
        if (chain.Count >= 32 || !chain.Add(id)) return false;
        bool result = false;
        if (int.TryParse(id, out int category) && category < 0)
        {
            var possible = Items.Values.Where(i => i.Category == category).ToArray();
            result = possible.Length > 0 && possible.All(i => Known(i.Id, chain));
        }
        else if (Items.TryGetValue(id, out var item) && item.Category != -4)
        {
            if (Recipes.TryGetValue(id, out var alternatives))
                result = alternatives.All(ingredients => ingredients.Length > 0 && ingredients.All(i => Known(i, chain)));
            else result = item.Crop || TerrestrialRoots.Contains(id);
        }
        chain.Remove(id);
        Safe[id] = result;
        return result;
    }
}

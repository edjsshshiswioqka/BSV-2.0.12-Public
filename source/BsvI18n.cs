using System.Runtime.CompilerServices;
using System.Globalization;
using StardewModdingAPI;
using StardewValley;

namespace BetterStardewValley;

internal static class BsvI18n
{
    // ICU may accept arbitrary identifiers as synthetic cultures. Only known
    // cultures are appropriate for display formatting of unknown mod locales.
    private static readonly HashSet<string> NumberLocales = CultureInfo.GetCultures(CultureTypes.NeutralCultures | CultureTypes.SpecificCultures)
        .Select(c=>c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    internal static string Number(IModHelper helper,IFormattable value,string format)
    {
        CultureInfo culture;
        try{
            string locale=helper.Translation.Locale switch{"default"=>"en","pt"=>"pt-BR",var code=>code};
            culture=NumberLocales.Contains(locale)?CultureInfo.GetCultureInfo(locale):CultureInfo.InvariantCulture;
        }
        catch{culture=CultureInfo.InvariantCulture;}
        return value.ToString(format,culture);
    }
    private static readonly ConditionalWeakTable<IModHelper, HashSet<string>> Reported = new();
    private static Action<string>? Diagnostic;
    // Use the player's language on each call, never the host's language or OS locale.
    internal static string Get(IModHelper helper, string key, object? tokens = null)
    {
        try
        {
            var text = helper.Translation.Get(key, tokens);
            if (text != null && text.HasValue() && Usable(text.ToString(), key)) return text.ToString();
        }
        catch (Exception ex) { Warn(helper, key, "translation lookup failed: " + ex.GetType().Name); }
        // SMAPI owns normal locale dispatch. Only invalid/empty translations reach
        // this defensive default lookup; never choose an arbitrary third language.
        try
        {
            if (helper.Translation.GetInAllLocales(key).TryGetValue("default", out var fallback))
            {
                string value = fallback.Tokens(tokens).ToString();
                if (fallback.HasValue() && Usable(value, key)) return value;
            }
        }
        catch (Exception ex) { Warn(helper, key, "default lookup failed: " + ex.GetType().Name); }
        Warn(helper, key, "default missing or invalid");
        return key == "rescue.give-up" ? "Give Up" : "Text unavailable";
    }
    private static bool Usable(string text, string key) => !string.IsNullOrWhiteSpace(text) && text != key
        && !text.TrimStart().StartsWith("(no translation", StringComparison.OrdinalIgnoreCase)
        && !text.Trim().Equals("TODO", StringComparison.OrdinalIgnoreCase);
    private static void Warn(IModHelper helper, string key, string reason)
    {
        if (!Reported.GetOrCreateValue(helper).Add(key + ":" + reason)) return;
        try { Diagnostic?.Invoke("BSV translation " + key + ": " + reason); }
        catch { /* Diagnostics must never prevent the emergency display fallback. */ }
    }

    internal static void Register(IModHelper helper, Action<string>? diagnostic = null)
    {
        Diagnostic = diagnostic;
        helper.Events.Content.LocaleChanged += (_, _) =>
        {
            foreach (string asset in new[] { "Data/Objects", "Data/BigCraftables", "Data/CraftingRecipes", "Data/CookingRecipes", "Data/Events/Sunroom", "Data/mail" })
                helper.GameContent.InvalidateCache(asset);
            if (Context.IsWorldReady)
            {
                CraftingRecipe.craftingRecipes = DataLoader.CraftingRecipes(Game1.content);
                CraftingRecipe.cookingRecipes = DataLoader.CookingRecipes(Game1.content);
            }
        };
    }
}

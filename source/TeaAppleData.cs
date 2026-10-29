using StardewValley.GameData.Machines;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;

namespace BetterStardewValley;

internal static class TeaAppleData
{
    internal const string TextureRoot = "Mods/" + TeaAppleRules.Prefix + "/TeaApple/";
    internal static readonly Dictionary<string, string> Textures = new()
    {
        ["Cup"] = "assets/items/a_cup_of_green_tea.png", ["Cake"] = "assets/items/tea_cake.png",
        ["Apple"] = "assets/items/golden_apple.png", ["Tenacity"] = "assets/buffs/tenacity.png"
    };

    internal static void Objects(IDictionary<string, ObjectData> data, Func<string, string> tr)
    {
        // Preserve every other field and the identities of vanilla tea items (including Green Tea).
        if (data.TryGetValue("815", out var leaves)) leaves.Price = TeaAppleRules.LeavesPrice;
        // BSV's own Tea Sapling: a copy of the game's (same name and picture) with its own ID, so other mods that use or change
        // the game's Tea Sapling (251) or tea-sized bushes never meet BSV's tea rules.
        var own = data.TryGetValue("251", out var native) ? WineData.Copy(native)
            : new ObjectData { Name = "Tea Sapling", DisplayName = "[LocalizedText Strings\\Objects:TeaSapling_Name]", Type = "Basic",
                Category = -74, Price = 500, Texture = null, SpriteIndex = 251 };
        own.Name = "BSV Tea Sapling"; own.DisplayName = "BSV " + own.DisplayName;   // the game's localized name with a BSV prefix own.Description = tr("tea.sapling.description"); own.ExcludeFromRandomSale = true;
        data[TeaAppleRules.SaplingId] = own;
        Add(TeaAppleRules.CupId, "A Cup of Green Tea", "tea.cup", "Cup", TeaAppleRules.CupPrice, -7, 13, true);
        Add(TeaAppleRules.CakeId, "Tea Cake", "tea.cake", "Cake", TeaAppleRules.CakePrice, -26, -300, false);
        Add(TeaAppleRules.AppleId, "Golden Apple", "apple", "Apple", TeaAppleRules.ApplePrice, -7, 15, false);
        void Add(string id, string name, string key, string texture, int price, int category, int edibility, bool drink)
        {
            data[id] = new ObjectData { Name = name, DisplayName = tr(key + ".name"), Description = tr(key + ".description"),
                Type = "Basic", Category = category, Price = price, Edibility = edibility, IsDrink = drink,
                Texture = TextureRoot + texture, SpriteIndex = 0, ExcludeFromRandomSale = true };
        }
    }

    internal static void Shops(IDictionary<string, ShopData> shops)
    {
        if (!shops.TryGetValue("SeedShop", out var shop)) return;
        shop.Items ??= new();
        shop.Items.RemoveAll(i => i.ItemId is "251" or TeaAppleRules.VanillaSapling or TeaAppleRules.SaplingId or TeaAppleRules.Sapling);
        shop.Items.Add(new ShopItemData { Id = TeaAppleRules.Prefix + "/TeaSapling", ItemId = TeaAppleRules.Sapling,
            Price = TeaAppleRules.SaplingPrice, UseObjectDataPrice = false, ApplyProfitMargins = false,
            IgnoreShopPriceModifiers = true, AvailableStock = -1,
            // Pierre's Missing Stocklist bypasses vanilla SEASON queries. A separate BSV check
            // keeps the requested spring/summer availability even after that vanilla upgrade.
            Condition = "SEASON spring summer, " + TeaAppleRules.SeasonQuery + ", PLAYER_HAS_MAIL Current " + TeaAppleRules.TeaUnlock });
    }

    internal static void Crafting(IDictionary<string, string> data, Func<string, string> tr)
    {
        data.Remove("Tea Sapling");
        data[TeaAppleRules.CupId] = $"815 1/Home/{TeaAppleRules.CupId} 1/false/null/{tr("tea.cup.name")}";
    }
    internal static void Cooking(IDictionary<string, string> data, Func<string, string> tr) =>
        data[TeaAppleRules.AppleId] = $"613 1 336 {TeaAppleRules.AppleGoldBars}/Home/{TeaAppleRules.AppleId}/null/{tr("apple.name")}";

    internal static void Machines(IDictionary<string, MachineData> machines)
    {
        if (!machines.TryGetValue(TeaAppleRules.Dehydrator, out var machine))
            throw new InvalidOperationException("Native Dehydrator machine data is missing.");
        machine.OutputRules ??= new();
        string id = TeaAppleRules.Prefix + "/TeaCake";
        machine.OutputRules.RemoveAll(r => r.Id == id);
        machine.OutputRules.Insert(0, new MachineOutputRule { Id = id, DaysUntilReady = TeaAppleRules.CakeDays,
            Triggers = new() { new() { Id = "TeaLeaves", RequiredItemId = TeaAppleRules.Leaves,
                RequiredCount = TeaAppleRules.CakeInput,
                Condition = "PLAYER_HAS_MAIL Current " + TeaAppleRules.TeaUnlock } },
            OutputItem = new() { new() { Id = "TeaCake", ItemId = TeaAppleRules.Cake,
                MinStack = 1, MaxStack = 1, Quality = 0, CopyQuality = false } }, RecalculateOnCollect = false });
    }

    internal static void Events(IDictionary<string, string> data, Func<string, string> tr)
    {
        foreach (string key in data.Keys.Where(k => k.Split('/')[0] == TeaAppleRules.CarolineEvent).ToArray())
        {
            string script = data[key];
            // Append a localized line to the native event, retaining all preconditions and commands.
            int end = script.LastIndexOf("/end", StringComparison.Ordinal);
            if (end < 0) throw new InvalidOperationException("Caroline tea event has no end command.");
            string line = tr("tea.event.line").Replace('"', '\'').Replace('/', ' ');
            if (script.Contains("/speak Caroline \"" + line + "\"", StringComparison.Ordinal)) continue;
            data[key] = script.Insert(end, "/speak Caroline \"" + line + "\"");
        }
    }
    internal static void Mail(IDictionary<string, string> data, Func<string, string> tr)
    {
        // Only the native tea-unlock letter belongs to this existing BSV change.
        // Other authors may mention or award the same recipe in unrelated story mail.
        // Keep even the native letter's current text, title and unrelated attachments.
        // The game's key is "CarolineTea"; match it case-insensitively so a differently cased copy still counts.
        string? key = data.Keys.FirstOrDefault(k => string.Equals(k, "CarolineTea", StringComparison.OrdinalIgnoreCase));
        if (key == null || !data.TryGetValue(key, out string? letter)) return;
        data[key] = System.Text.RegularExpressions.Regex.Replace(letter,
            @"%item\s+craftingRecipe\s+Tea[ _]Sapling\s*%%", _ => tr("tea.mail.body"),
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}

using Newtonsoft.Json;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.GameData.Machines;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;

namespace BetterStardewValley;

internal static class WineData
{
    internal static T Copy<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;

    /// <summary>Adds BSV's own grape crop, copied from the game's grape (<paramref name="template"/>, read before other mods
    /// change it). The game's Grape Starter crop (301) is left exactly as the game and other mods have it.</summary>
    internal static bool EditCrops(IDictionary<string, CropData> crops, CropData? template)
    {
        if (template == null && !crops.TryGetValue(WineRules.GrapeSeedId, out template)) return false;
        var grape = Copy(template);
        grape.Seasons = new() { Season.Spring, Season.Summer, Season.Fall };
        // Five native visual phases, totaling 15 days; mature sentinel is added by Crop.
        grape.DaysInPhase = new() { 2, 3, 3, 3, WineRules.GrapeDaysToMature - 11 };
        grape.RegrowDays = WineRules.GrapeRegrowDays;
        grape.HarvestItemId = WineRules.GrapeId;   // always the game's grape: wine, bundles and recipes keep working
        crops[WineRules.StarterId] = grape;
        return true;
    }

    /// <summary>Pierre sells BSV's Grape Starter all year (it grows spring to fall) instead of the game's.</summary>
    internal static void EditShops(IDictionary<string, ShopData> shops)
    {
        if (!shops.TryGetValue("SeedShop", out var pierre)) return;
        pierre.Items ??= new();
        pierre.Items.RemoveAll(p => p.ItemId is "301" or "(O)301" or WineRules.StarterId or WineRules.Starter);
        var item = new ShopItemData { Id = WineRules.Prefix + "/GrapeStarter", ItemId = WineRules.Starter };
        FixSeedPrice(item);
        pierre.Items.Add(item);
    }

    private static void FixSeedPrice(ShopItemData item)
    {
        item.Price = WineRules.GrapeSeedPrice;
        item.UseObjectDataPrice = false;
        item.ApplyProfitMargins = false;
        item.IgnoreShopPriceModifiers = true;
        item.PriceModifiers = null;
    }

    internal static void EditObjects(IDictionary<string, ObjectData> objects, Func<string, string> translate,
        string grapeTexture, string bloodTexture)
    {
        if (!objects.TryGetValue("348", out var wine))
            throw new InvalidOperationException("Vanilla wine object 348 is missing.");
        // BSV's own Grape Starter: a copy of the game's (same name and picture) with its own ID.
        var starter = objects.TryGetValue(WineRules.GrapeSeedId, out var native) ? Copy(native)
            : new ObjectData { Name = "Grape Starter", DisplayName = "[LocalizedText Strings\\Objects:GrapeStarter_Name]", Type = "Seeds",
                Category = -74, Price = 30, Texture = null, SpriteIndex = 301 };
        starter.Name = "BSV Grape Starter"; starter.DisplayName = "BSV " + starter.DisplayName;   // the game's localized name with a BSV prefix starter.Description = translate("grape-starter.description"); starter.ExcludeFromRandomSale = true;
        objects[WineRules.StarterId] = starter;
        Add(WineRules.GrapeWineId, "Farmhouse Special Grape Wine", "grape-wine", WineRules.GrapeWinePrice, grapeTexture);
        objects[WineRules.GrapeWineId].Edibility = WineRules.GrapeWineEdibility;   // native wine buffs (Tipsy) kept
        Add(WineRules.BloodOfJesusId, "Ruby Reserve", "blood-of-jesus", WineRules.BloodOfJesusPrice, bloodTexture);
        var blood = objects[WineRules.BloodOfJesusId];
        blood.Edibility = WineRules.BloodOfJesusEdibility;
        blood.Buffs = new() { new() { Id = WineRules.BloodOfJesusId, Duration = WineRules.BloodBuffMinutes,
            CustomAttributes = new() { LuckLevel = WineRules.BloodLuck } } };
        void Add(string id, string name, string key, int price, string texture)
        {
            var data = Copy(wine);
            data.Name = name;
            data.DisplayName = translate(key + ".name");
            data.Description = translate(key + ".description");
            data.Category = -26; // Native artisan profession and native quality multipliers.
            data.Price = price;
            data.Texture = texture;
            data.SpriteIndex = 0;
            data.ColorOverlayFromNextIndex = false;
            data.ExcludeFromRandomSale = true;
            objects[id] = data;
        }
    }

    internal static void EditMachines(IDictionary<string, MachineData> machines)
    {
        var keg = machines[WineRules.Keg];
        var cask = machines[WineRules.Cask];
        // Reuse the loaded wine timing, including changes to wine timing by another mod.
        var kegTemplate = keg.OutputRules.FirstOrDefault(r => r.Id != WineRules.KegRuleId
            && r.OutputItem?.Any(o => o.ItemId?.StartsWith("FLAVORED_ITEM Wine ", StringComparison.OrdinalIgnoreCase) == true) == true)
            ?? throw new InvalidOperationException("Keg's native flavored-wine rule is missing.");
        var caskTemplate = cask.OutputRules.FirstOrDefault(r => r.Triggers?.Any(t => t.RequiredItemId is "348" or "(O)348"
                || t.RequiredTags?.Contains("item_wine") == true) == true
            || (r.Id == "Wine" && r.OutputItem?.Any(o => o.OutputMethod?.EndsWith(": OutputCask", StringComparison.Ordinal) == true) == true))
            ?? throw new InvalidOperationException("Cask's native wine rule is missing.");
        var grape = Copy(kegTemplate);
        grape.Id = WineRules.KegRuleId;
        grape.Triggers = new() { new() { Id = "Grape", RequiredItemId = "(O)" + WineRules.GrapeId } };
        grape.OutputItem = new() { new() {
            Id = "Wine", OutputMethod = "BetterStardewValley.GrapeWineFeature, BetterStardewValley: OutputGrapeWine"
        } };
        grape.RecalculateOnCollect = false;
        var agingRules = new List<MachineOutputRule>();
        foreach (string id in new[] { WineRules.GrapeWine, WineRules.BloodOfJesus })
        {
            var aging = Copy(caskTemplate);
            aging.Id = WineRules.Prefix + "/Age/" + id;
            // Keep native quality/location validation and aging multiplier in OutputCask.
            aging.Triggers = new() { new() { Id = "Wine", RequiredItemId = id } };
            aging.RecalculateOnCollect = false;
            agingRules.Add(aging);
        }
        // Commit only after every template has been validated and cloned.
        // Vanilla Keg is read-only. A separate deep clone owns the BSV-only grape rule.
        var bsvKeg = Copy(keg);
        bsvKeg.OutputRules.RemoveAll(r => r.Id == grape.Id);
        bsvKeg.OutputRules.Insert(0, grape);
        machines[WineRules.BsvKeg] = bsvKeg;
        foreach (var aging in agingRules)
        {
            cask.OutputRules.RemoveAll(r => r.Id == aging.Id);
            cask.OutputRules.Insert(0, aging);
        }
    }
}

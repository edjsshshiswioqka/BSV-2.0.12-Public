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

    internal static void EditCrops(IDictionary<string, CropData> crops)
    {
        if (!crops.TryGetValue(WineRules.GrapeSeedId, out var grape))
            throw new InvalidOperationException("Grape starter crop 301 is missing.");
        grape.Seasons = new() { Season.Spring, Season.Summer, Season.Fall };
        // Five native visual phases, totaling 15 days; mature sentinel is added by Crop.
        grape.DaysInPhase = new() { 2, 3, 3, 3, WineRules.GrapeDaysToMature - 11 };
        grape.RegrowDays = WineRules.GrapeRegrowDays;
    }

    internal static void EditShops(IDictionary<string, ShopData> shops)
    {
        foreach (var shop in shops.Values)
            foreach (var item in shop.Items ?? new())
                if (item.ItemId is "301" or "(O)301") FixSeedPrice(item);
        if (shops.TryGetValue("SeedShop", out var pierre))
        {
            pierre.Items ??= new();
            // One permanent listing prevents seasonal/stocklist duplicates. Spring is available
            // without changing any other seed listing or the seed's resale price.
            pierre.Items.RemoveAll(p => p.ItemId is "301" or "(O)301");
            var item = new ShopItemData { Id = WineRules.Prefix + "/GrapeStarter", ItemId = "(O)301" };
            FixSeedPrice(item);
            pierre.Items.Add(item);
        }
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
        if (objects.TryGetValue(WineRules.GrapeSeedId, out var starter))
            starter.Description = translate("grape-starter.description");
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

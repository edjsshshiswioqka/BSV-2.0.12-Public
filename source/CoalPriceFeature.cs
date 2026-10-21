using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData.Shops;

namespace BetterStardewValley;

internal static class CoalPriceFeature
{
    internal static void Register(IModHelper helper)
    {
        helper.Events.Content.AssetRequested += (_, e) =>
        {
            if (!e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
                return;

            e.Edit(asset =>
            {
                var shops = asset.AsDictionary<string, ShopData>().Data;
                if (!shops.TryGetValue("Blacksmith", out var shop) || shop.Items is null)
                    return;

                // Update all coal entries, including any year-specific entries.
                foreach (var item in shop.Items)
                {
                    if (item.ItemId is not ("382" or "(O)382"))
                        continue;
                    item.Price = 100;
                    item.UseObjectDataPrice = false;
                    item.ApplyProfitMargins = false;
                    item.IgnoreShopPriceModifiers = true;
                    item.PriceModifiers = null;
                }
            }, AssetEditPriority.Late);
        };
    }
}

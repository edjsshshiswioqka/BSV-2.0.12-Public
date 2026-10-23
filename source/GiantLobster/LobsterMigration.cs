using Eddie.FishingMachine.Core;
using StardewValley;
namespace BetterStardewValley.GiantLobster;

internal static class LobsterMigration
{
    // 1.1.39 explicitly saved zero price/edibility test placeholders on native Objects.
    // Repair only those values on the same three stable IDs; never replace the Item.
    internal static bool RepairPreview(Item item)
    {
        if(item is not StardewValley.Object o)return true;
        // Lobster King and Mutant Lobster King made before the price change keep the old price in the item itself.
        if(o.QualifiedItemId==LobsterKingRules.Fish&&o.Price==LobsterKingRules.OldPrice){o.Price=LobsterKingRules.Price;return true;}
        if(o.QualifiedItemId==MutantLobsterRules.Item&&o.Price==MutantLobsterRules.OldPrice){o.Price=MutantLobsterRules.Price;return true;}
        int price=o.QualifiedItemId switch
        {
            "(O)"+GiantLobsterRules.Id=>LobsterBalance.FishPrice(LobsterBalance.VanillaLobsterPrice()),
            GiantLobsterRules.Bait=>LobsterBalance.BaitPrice(LobsterBalance.VanillaLobsterPrice()),
            "(O)"+GiantLobsterRules.DinnerId=>LobsterBalance.DinnerPrice,
            _=>0
        };
        if(price==0)return true;
        if(o.Price==0)o.Price=price;
        // Giant Lobsters made before the price change keep the old 450g in the item itself.
        if(o.QualifiedItemId=="(O)"+GiantLobsterRules.Id&&o.Price==LobsterBalance.OldFishPrice)o.Price=price;
        if(o.QualifiedItemId==GiantLobsterRules.Bait&&o.Price==LobsterBalance.OldBaitPrice)o.Price=price;
        if(o.QualifiedItemId=="(O)"+GiantLobsterRules.DinnerId&&o.Edibility==0)o.Edibility=LobsterBalance.FoodEdibility;
        return true;
    }
}

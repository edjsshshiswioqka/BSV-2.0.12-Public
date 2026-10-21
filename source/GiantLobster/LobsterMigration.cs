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
        int price=o.QualifiedItemId switch
        {
            "(O)"+GiantLobsterRules.Id=>LobsterBalance.FishPrice,
            GiantLobsterRules.Bait=>LobsterBalance.BaitPrice,
            "(O)"+GiantLobsterRules.DinnerId=>LobsterBalance.DinnerPrice,
            _=>0
        };
        if(price==0)return true;
        if(o.Price==0)o.Price=price;
        if(o.QualifiedItemId=="(O)"+GiantLobsterRules.DinnerId&&o.Edibility==0)o.Edibility=LobsterBalance.FoodEdibility;
        return true;
    }
}

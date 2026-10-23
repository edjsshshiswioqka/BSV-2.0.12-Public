using StardewValley;
using SObject = StardewValley.Object;

namespace BetterStardewValley.AfkFishing;

internal static class AfkFood
{
    private static AfkFoodRules? Catalog;
    private static object? ObjectData;
    internal static void Invalidate() { Catalog = null; ObjectData = null; }
    internal static bool Allowed(Item? item)
    {
        if (item is not SObject o || o.GetType() != typeof(SObject) || o.bigCraftable.Value
            || !AfkRules.AcceptFood(o.Edibility != -300, o.staminaRecoveredOnConsumption(), o.questItem.Value,
                o.QualifiedItemId is "(O)434" or "(O)StardropTea" || o.IsRecipe)
            || o.heldObject.Value != null) return false;
        try
        {
            if (Catalog == null || !ReferenceEquals(ObjectData, Game1.objectData))
            {
                var crops = DataLoader.Crops(Game1.content).Values.Select(c => AfkFoodRules.Plain(c.HarvestItemId ?? ""))
                    .ToHashSet(StringComparer.Ordinal);
                Catalog = new(Game1.objectData.ToDictionary(p => p.Key,
                    p => new AfkIngredient(p.Key, p.Value.Category, crops.Contains(p.Key))),
                    DataLoader.Fish(Game1.content).Keys, DataLoader.CookingRecipes(Game1.content).Values);
                ObjectData = Game1.objectData;
            }
            return Catalog.Allows(o.ItemId);
        }
        catch { Invalidate(); return false; } // unavailable/malformed ingredient evidence is never permission to eat.
    }
}

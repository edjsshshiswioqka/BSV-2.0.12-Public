using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Objects;

namespace Eddie.FishingMachine;

internal sealed partial class ProductionService
{
    // Native Item.canStackWith checks identity/name/quality/type, but not modData
    // or all saved object variants. Never erase provenance when depositing output.
    internal static bool OutputCompatible(Item a, Item b) => a.canStackWith(b)
        && a.modData.Count() == b.modData.Count() && a.modData.Keys.All(k => b.modData.TryGetValue(k, out string value) && value == a.modData[k])
        && a.specialItem == b.specialItem && a.isLostItem == b.isLostItem && a.IsRecipe == b.IsRecipe
        && (a is not StardewValley.Object x || b is not StardewValley.Object y
            || x.preservedParentSheetIndex.Value == y.preservedParentSheetIndex.Value && x.preserve.Value == y.preserve.Value
            && x.questItem.Value == y.questItem.Value && x.questId.Value == y.questId.Value
            && x.orderData.Value == y.orderData.Value && x.Price == y.Price && x.Edibility == y.Edibility);
    internal bool MachineLive(Chest c) => Mod.Ready && !Mod.Saving && c.Location != null
        && c.Location.objects.TryGetValue(c.TileLocation, out var live) && ReferenceEquals(c, live)
        && CompleteFootprint(c);
    internal static bool InternalInputProtected(Item item, ProductionState state) => state.Kind==MachineKinds.Crab
        ? item is not StardewValley.Object crab || crab.questItem.Value || CrabBaitItem(item) || CrabRules.IsUpgradeMaterial(item.QualifiedItemId)
        : item is not StardewValley.Object obj
        || obj.questItem.Value || IsFishingBait(item.QualifiedItemId)
        || MachineUpgrade.IsMaterial(item.QualifiedItemId, state.Level);
}

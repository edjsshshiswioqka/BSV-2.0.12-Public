using System.Reflection.Emit;
using HarmonyLib;
using StardewValley;

namespace BetterStardewValley;

public sealed partial class ModEntry
{
    [ThreadStatic] private static HarvestState? ActiveHarvest;

    // Replace only Crop.harvest's own getOne calls, never global inventory operations.
    // Original item stacks and capacity checks remain untouched until harvest succeeds.
    private static IEnumerable<CodeInstruction> CaptureHarvestOutputs(IEnumerable<CodeInstruction> instructions)
    {
        var target = AccessTools.Method(typeof(Item), nameof(Item.getOne));
        var capture = AccessTools.Method(typeof(ModEntry), nameof(CopyHarvestItem));
        bool found = false;
        foreach (var code in instructions)
        {
            if (code.Calls(target)) { code.opcode = OpCodes.Call; code.operand = capture; found = true; }
            yield return code;
        }
        if (found) TranspilerGuard.Succeeded("Crop.harvest");
        else TranspilerGuard.Failed("Crop.harvest", "Crop.harvest output boundary was not found.");
    }

    private static Item CopyHarvestItem(Item source)
    {
        Item item = source.getOne();
        var scope = ActiveHarvest;
        if (scope != null && item.QualifiedItemId == scope.HarvestId)
            item.Quality = Math.Max(item.Quality, scope.MinimumQuality);
        if (scope != null && item.QualifiedItemId == scope.HarvestId)
        {
            var copy = item.getOne(); copy.Stack = item.Stack;
            scope.Crops.Add(copy);
        }
        return item;
    }

    private static void FinishHarvestScope(HarvestState? __state)
        => ActiveHarvest = __state?.Parent;

    private static void GiveHarvestBonus(HarvestState state, Item item)
    {
        if (state.Junimo != null) { state.Junimo.tryToAddItemToHut(item); return; }
        if (!state.DropOnGround)
        {
            // This API returns the remainder after a partial deposit; never drop a full
            // replacement stack after some of it has already entered the inventory.
            item = state.Farmer.addItemToInventory(item);
            if (item == null) return;
        }
        Game1.createItemDebris(item, state.Position, -1, state.Location);
    }
}

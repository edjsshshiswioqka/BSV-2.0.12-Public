using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using SObject = StardewValley.Object;

namespace BetterStardewValley.Brewing;

/// <summary>Normalize a malformed portable keg in place; native placement remains authoritative.</summary>
internal static class BsvKegInstanceRepair
{
    internal static void Install(IModHelper helper)
    {
        var harmony = new Harmony(WineRules.Prefix + ".KegIdentity");
        // Do not return a placement result, bypass collision, place an entity, or spend a stack.
        harmony.Patch(DrawPatchTargets.Require(typeof(SObject), nameof(SObject.isPlaceable), typeof(bool)),
            prefix: new HarmonyMethod(typeof(BsvKegInstanceRepair), nameof(BeforePlacementQuery)));
        harmony.Patch(DrawPatchTargets.Require(typeof(SObject), nameof(SObject.canBePlacedHere), typeof(bool),
                typeof(GameLocation), typeof(Vector2), typeof(CollisionMask), typeof(bool)),
            prefix: new HarmonyMethod(typeof(BsvKegInstanceRepair), nameof(BeforePlacementQuery)));
        helper.Events.GameLoop.SaveLoaded += (_, _) => RepairLocalInventory();
        helper.Events.Player.InventoryChanged += (_, e) =>
        {
            if (!e.IsLocalPlayer) return;
            foreach (var item in e.Added) Repair(item as SObject);
        };
    }

    private static void BeforePlacementQuery(SObject __instance) => Repair(__instance);

    internal static void RepairLocalInventory()
    {
        if (!Context.IsWorldReady || Game1.player == null) return;
        foreach (var item in Game1.player.Items) Repair(item as SObject);
    }

    internal static bool Repair(SObject? item)
    {
        // Exact raw stable ID also recognizes the wrong (O) instance. Never guess from
        // display name/sprite index; don't reinterpret furniture, chests or recipe scrolls.
        if (item == null || item.GetType() != typeof(SObject) || item.ItemId != WineRules.BsvKegId
            || item.IsRecipe || Game1.bigCraftableData == null
            || !Game1.bigCraftableData.TryGetValue(WineRules.BsvKegId, out var data)) return false;
        bool malformed = !item.bigCraftable.Value || item.Type != "Crafting" || item.Category != -9
            || item.Edibility != -300 || !item.CanBeSetDown
            || item.setIndoors.Value != data.CanBePlacedIndoors || item.setOutdoors.Value != data.CanBePlacedOutdoors;
        if (!malformed) return false;
        // Change only type/placement metadata on the same instance. In particular, keep
        // Stack, owner, modData, quality, held output, timer and batch entropy untouched.
        item.bigCraftable.Value = true;
        item.Type = "Crafting";
        item.Category = -9;
        item.Edibility = -300;
        item.CanBeSetDown = true;
        item.setIndoors.Value = data.CanBePlacedIndoors;
        item.setOutdoors.Value = data.CanBePlacedOutdoors;
        item.ResetParentSheetIndex();
        item.MarkContextTagsDirty();
        return true;
    }
}

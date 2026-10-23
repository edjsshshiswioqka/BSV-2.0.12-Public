using Eddie.FishingMachine.Core;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Machines;
using SObject = StardewValley.Object;
namespace BetterStardewValley.KingCrab;

/// <summary>A raw King Crab can't be eaten and can't be smoked: its shell is too hard.
/// Trying either shows a short message instead of the game silently doing nothing.</summary>
internal static class KingCrabHardShell
{
    private const string FishSmoker = "(BC)FishSmoker";
    internal static void Install(Harmony h, IModHelper helper)
    {
        h.Patch(DrawPatchTargets.Require(typeof(SObject), "PlaceInMachine", typeof(bool), typeof(MachineData), typeof(Item), typeof(bool), typeof(Farmer), typeof(bool), typeof(bool)),
            prefix: new HarmonyMethod(typeof(KingCrabHardShell), nameof(Smoker)));
        helper.Events.Input.ButtonPressed += (_, e) => TryEat(helper, e);
    }
    private static bool Smoker(SObject __instance, Item inputItem, bool probe, Farmer who, bool showMessages, ref bool __result)
    {
        if (__instance.QualifiedItemId != FishSmoker || !KingCrabRules.IsFish(inputItem?.QualifiedItemId)) return true;
        __result = false;
        if (!probe && showMessages && who?.IsLocalPlayer == true) Game1.showRedMessage(KingCrabService.Text("kingcrab.too-hard"));
        return false;
    }
    // The game offers "eat" when the action button is used with food in hand and nothing else
    // to interact with. A raw King Crab shows why it can't be eaten in that same situation.
    private static void TryEat(IModHelper helper, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsPlayerFree || !e.Button.IsActionButton() || helper.Input.IsSuppressed(e.Button)) return;
        var who = Game1.player; var location = Game1.currentLocation;
        if (location == null || !KingCrabRules.IsFish(who.ActiveObject?.QualifiedItemId) || who.isRidingHorse()) return;
        var tile = Game1.options.gamepadControls ? e.Cursor.GrabTile : e.Cursor.Tile;
        var at = new xTile.Dimensions.Location((int)tile.X, (int)tile.Y);
        if (location.isActionableTile((int)tile.X, (int)tile.Y, who) || location.isCharacterAtTile(tile) != null
            || location.objects.ContainsKey(tile) || location.GetFurnitureAt(tile) != null
            || location.doesTileHaveProperty(at.X, at.Y, "Action", "Buildings") != null) return;
        Game1.showRedMessage(KingCrabService.Text("kingcrab.too-hard"));
    }
}

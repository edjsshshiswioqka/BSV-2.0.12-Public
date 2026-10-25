using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace BetterStardewValley;

/// <summary>
/// Two more backpack upgrades at Pierre's backpack sign after the two native ones: 36 -> 48 for 50,000g and
/// 48 -> 60 for 100,000g. Each farmer upgrades their own backpack (native MaxItems / increaseBackpackSize).
/// The native grids keep three rows; the extra rows are reached by scrolling (<see cref="InventoryScroll"/>).
/// </summary>
internal static class BackpackUpgrade
{
    internal const int Step = 12, Max = 60;
    internal static int Price(int maxItems) => maxItems switch { 36 => 50_000, 48 => 100_000, _ => 0 };
    private static IModHelper? Helper;

    internal static void Install(IModHelper helper)
    {
        Helper = helper;
        new Harmony(helper.ModRegistry.ModID + ".Backpack").Patch(
            AccessTools.Method(typeof(GameLocation), nameof(GameLocation.performAction), new[] { typeof(string[]), typeof(Farmer), typeof(xTile.Dimensions.Location) })
                ?? throw new MissingMethodException(nameof(GameLocation), nameof(GameLocation.performAction)),
            prefix: new HarmonyMethod(typeof(BackpackUpgrade), nameof(BeforeAction)));
    }
    private static string T(string key, object? tokens = null) => BsvI18n.Get(Helper!, key, tokens);

    private static bool BeforeAction(GameLocation __instance, string[] action, Farmer who, ref bool __result)
    {
        // The native sign sells the first two upgrades (up to 36); BSV only takes over once those are bought.
        if (action == null || action.Length == 0 || action[0] != "BuyBackpack" || who == null || !who.IsLocalPlayer || who.MaxItems < 36) return true;
        __result = true;
        int price = Price(who.MaxItems);
        if (who.MaxItems >= Max || price == 0) { Game1.drawObjectDialogue(T("backpack.max")); return false; }
        int next = who.MaxItems + Step;
        __instance.createQuestionDialogue(T("backpack.question", new { count = next, price = BsvI18n.Number(Helper!, price, "N0") }),
            new[] { new Response("Buy", T("backpack.yes", new { price = BsvI18n.Number(Helper!, price, "N0") })), new Response("No", T("backpack.no")) },
            (farmer, answer) =>
            {
                if (answer != "Buy" || farmer.MaxItems != next - Step) return;
                if (farmer.Money < price) { Game1.drawObjectDialogue(T("backpack.no-money")); return; }
                farmer.Money -= price;
                farmer.increaseBackpackSize(Step);
                Game1.playSound("backpackIN");
                Game1.drawObjectDialogue(T("backpack.bought", new { count = farmer.MaxItems }));
            });
        return false;
    }
}

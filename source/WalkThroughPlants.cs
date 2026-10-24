using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.TerrainFeatures;

namespace BetterStardewValley;

/// <summary>
/// Players (and horses) can walk through tea bushes and every trellis crop (hops, grapes, green beans and
/// trellis crops from other mods). Other characters, placement checks and pathing keep the native rules.
/// </summary>
internal static class WalkThroughPlants
{
    internal static void Install(IModHelper helper, IMonitor monitor)
    {
        var h = new Harmony(helper.ModRegistry.ModID + ".WalkThroughPlants");
        foreach (var type in new[] { typeof(HoeDirt), typeof(Bush) })
            h.Patch(AccessTools.Method(type, nameof(TerrainFeature.isPassable)), postfix: new HarmonyMethod(typeof(WalkThroughPlants), nameof(Passable)));
    }
    private static void Passable(TerrainFeature __instance, [HarmonyArgument(0)] Character? who, ref bool __result)
    {
        if (__result || who is not (Farmer or Horse)) return;
        if (__instance is HoeDirt { crop: { } crop } && crop.raisedSeeds.Value) __result = true;
        else if (__instance is Bush bush && bush.size.Value == Bush.greenTeaBush) __result = true;
    }
}

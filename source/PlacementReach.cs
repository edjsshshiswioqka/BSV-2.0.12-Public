using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace BetterStardewValley;

/// <summary>
/// BSV's large pieces (totems, fishing machine, crab catcher, BSV keg) can be placed up to <see cref="Range"/> tiles
/// from the farmer instead of only the adjacent ring. Native and other mods' items keep the native reach, and every
/// other placement rule (collision, the machines' own footprint checks, totem spacing) still decides.
/// The Order Terminal has its own placement path with the same 3-tile reach.
/// </summary>
internal static class PlacementReach
{
    internal const int Range = 3;
    internal static void Install(IModHelper helper)
    {
        var h = new Harmony(helper.ModRegistry.ModID + ".PlacementReach");
        h.Patch(AccessTools.Method(typeof(Utility), nameof(Utility.isWithinTileWithLeeway))
                ?? throw new MissingMethodException(nameof(Utility), nameof(Utility.isWithinTileWithLeeway)),
            postfix: new HarmonyMethod(typeof(PlacementReach), nameof(WithinReach)));
    }
    internal static bool Large(Item? item) => item?.QualifiedItemId is { } id
        && (Totems.TotemRules.TryKind(id, out _) || id == Eddie.FishingMachine.ModEntry.QualifiedId
            || id == Eddie.FishingMachine.ModEntry.CrabQualifiedId || id == WineRules.BsvKeg);
    internal static bool InReach(Farmer who, int tileX, int tileY)
        => Math.Max(Math.Abs(tileX - who.TilePoint.X), Math.Abs(tileY - who.TilePoint.Y)) <= Range;
    private static void WithinReach(int x, int y, Item item, Farmer f, ref bool __result)
    {
        if (!__result && f != null && Large(item) && InReach(f, x / 64, y / 64)) __result = true;
    }
}

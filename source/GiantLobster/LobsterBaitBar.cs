using HarmonyLib;
using StardewValley.Menus;
using Eddie.FishingMachine.Core;
namespace BetterStardewValley.GiantLobster;

internal static class LobsterBaitBar
{
    internal static void Install(Harmony h)
    {
        var ctor=AccessTools.Constructor(typeof(BobberBar),new[]{typeof(string),typeof(float),typeof(bool),typeof(List<string>),typeof(string),typeof(bool),typeof(string),typeof(bool)})
            ??throw new MissingMethodException("Native BobberBar constructor unavailable.");
        h.Patch(ctor,postfix:new HarmonyMethod(typeof(LobsterBaitBar),nameof(Apply)){priority=Priority.Last});
    }
    // Native constructor has already applied skill/food/Cork Bobbers. Giant bait
    // receives no native +12; add the Deluxe Bait reference exactly once, then scale.
    private static void Apply(string baitID,ref int ___bobberBarHeight,ref float ___bobberBarPos)
    {
        if(baitID!=GiantLobsterRules.Bait)return;
        ___bobberBarHeight=GiantLobsterRules.BarHeight(___bobberBarHeight);
        ___bobberBarPos=Math.Clamp(___bobberBarPos,0,568-___bobberBarHeight);
    }
}

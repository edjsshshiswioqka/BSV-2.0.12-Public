[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SpiritNativeChecks")]
namespace BetterStardewValley.SpiritFish;

internal static class SpiritRules
{
    internal const string Id = "Eddie.BetterStardewValley_SpiritFish";
    internal const string Fish = "(O)" + Id;
    internal const string SoupId = "Eddie.BetterStardewValley_SpiritFishSoup";
    internal const string Soup = "(O)" + SoupId;
    internal const string Tag = "bsv_spirit_fish";
    internal const string LuckKey = "Eddie.BetterStardewValley/SpiritSoup/LuckDay";
    internal const string TextureRoot = "Mods/Eddie.BetterStardewValley/Spirit/";
    internal const double Chance = Eddie.FishingMachine.Core.GiantLobsterRules.SpiritChance;
    internal const int Price = 6000;
    internal static bool IsFish(string? id) => id is Id or Fish;
    internal static bool Eligible(int level, int time, string? location) => level >= 10 && time >= 1800 && time < 2600 && location == "Mountain";
    internal static int Quantity(int value) => Math.Clamp(value, 1, 3);
    internal const string Truffle = "430", Morel = "257";
    internal const string LegendFish = "163";
    internal const int LegendEdibilityFallback = 200;   // vanilla Legend: 500 energy / 225 health
    internal const string LuckBuff = "Eddie.BetterStardewValley/SpiritBlessing";
    /// <summary>Soup sells for the fish plus its two ingredients (6000 + 625 + 150 = 6775 in vanilla).</summary>
    internal static int SoupPrice(int fish, int truffle, int morel) => checked(fish + truffle + morel);
    internal const double DailyLuck = .125;
    internal const int LuckLevel = 13;
    internal const int SlotLuckLevel = 14;
}

using System.Globalization;

namespace BetterStardewValley;

// Balance values, IDs and persistence formats live here, independently of the renderer.
internal static class TeaAppleRules
{
    internal const string Prefix = "Eddie.BetterStardewValley";
    internal const string Sapling = "(O)251", Leaves = "(O)815", Apple = "(O)613", GoldBar = "(O)336";
    internal const string CupId = Prefix + "_ACupOfGreenTea", CakeId = Prefix + "_TeaCake", AppleId = Prefix + "_GoldenApple";
    internal const string Cup = "(O)" + CupId, Cake = "(O)" + CakeId, GoldenApple = "(O)" + AppleId;
    internal const string Tenacity = Prefix + "/Tenacity", TeaUnlock = Prefix + "/TeaKnowledge";
    internal const string CarolineEvent = "719926", Dehydrator = "(BC)Dehydrator";
    internal const string SeasonQuery = Prefix + "_TeaGrowingSeason";
    internal const int SaplingPrice = 400, LeavesPrice = 80, CupPrice = 80, CakePrice = 750, ApplePrice = 500;
    internal const int GrowthDays = 15, RegrowDays = 2, LeavesPerHarvest = 1, CakeInput = 5, CakeDays = 2;
    internal const int CupEnergy = 28, CupHealth = 14, AppleEnergy = 38, AppleHealth = 18, AppleGoldBars = 3;
    internal const int ExpiryTime = 2600, InvincibleMilliseconds = 2500;
    internal const string GrowthKey = Prefix + "/Tea/Growth", RegrowKey = Prefix + "/Tea/Regrow";
    internal const string DeadKey = Prefix + "/Tea/Dead", WaterKey = Prefix + "/Tea/WateredDay";
    internal const string PreviousWaterKey = Prefix + "/Tea/PreviousWateredDay";
    internal const string DayKey = Prefix + "/Tea/AdvancedDay", VersionKey = Prefix + "/Tea/Version";
    internal const string EatenKey = Prefix + "/GoldenApple/LastEatenDay", ArmedKey = Prefix + "/GoldenApple/ArmedDay";
    internal const string TriggerMessage = "TenacityTriggered/v1";

    internal static int Number(string? value, int fallback = -1) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;
    internal static string Text(int n) => n.ToString(CultureInfo.InvariantCulture);
    internal static bool GrowingSeason(int season) => season is 0 or 1;
    internal static int MigratedGrowth(int age) => Math.Clamp(age, 0, 20) * GrowthDays / 20;
    internal static bool CanEat(int lastEaten, int day) => lastEaten != day;
    internal static bool IsArmed(int lastEaten, int armedDay, int day, int time) =>
        lastEaten == day && armedDay == day && time < ExpiryTime;
    internal static bool Lethal(int oldHealth, int newHealth, bool armed) => armed && oldHealth > 0 && newHealth <= 0;
    internal static bool OwnKitchen(bool cooking, bool ownHouse, int upgrade, bool usesFridge) =>
        cooking && ownHouse && upgrade >= 1 && usesFridge;

    internal readonly record struct Plant(int Growth, int Regrow, bool Ready, bool Dead, int LastDay);
    internal static Plant Advance(Plant p, int day, int season, int wateredDay)
    {
        if (!GrowingSeason(season)) return p with { Ready = false, Dead = true, LastDay = day };
        if (p.Dead || p.LastDay >= day) return p;
        p = p with { LastDay = day };
        if (wateredDay != day - 1 || p.Ready) return p;
        if (p.Growth < GrowthDays)
        {
            int growth = Math.Min(GrowthDays, p.Growth + 1);
            return p with { Growth = growth, Ready = growth == GrowthDays, Regrow = 0 };
        }
        int remaining = Math.Max(0, p.Regrow - 1);
        return p with { Regrow = remaining, Ready = remaining == 0 };
    }
    internal static Plant Harvest(Plant p) => p.Ready && !p.Dead ? p with { Ready = false, Regrow = RegrowDays } : p;
}

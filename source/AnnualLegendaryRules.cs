using System;
using System.Globalization;

namespace BetterStardewValley;

internal readonly record struct LegendaryCatchRecord(int Year, int LifetimeCount)
{
    internal string Serialize() => Year.ToString(CultureInfo.InvariantCulture) + ":" + LifetimeCount.ToString(CultureInfo.InvariantCulture);
}

internal static class AnnualLegendaryRules
{
    internal static readonly string[] FishIds = { "159", "160", "163", "682", "775" };
    internal const string KeyPrefix = "Eddie.BetterStardewValley/AnnualLegendaryFish/";

    internal static string? NormalizeId(string? itemId)
    {
        if (itemId is null) return null;
        string id = itemId.StartsWith("(O)", StringComparison.Ordinal) ? itemId[3..] : itemId;
        return id is "159" or "160" or "163" or "682" or "775" or "Eddie.BetterStardewValley_SpiritFish" ? id : null;
    }

    internal static LegendaryCatchRecord ReadRecord(string? stored, int lifetimeCount, int currentYear)
    {
        lifetimeCount = Math.Max(0, lifetimeCount);
        string[]? parts = stored?.Split(':');
        if (parts?.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int year)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int knownCount)
            && year >= 0 && knownCount >= 0 && (year > 0 || knownCount == 0))
        {
            if (lifetimeCount <= knownCount) return new LegendaryCatchRecord(year, knownCount);
            // A catch made while BSV was absent/disabled has no reliable date. Treat
            // that untracked increase as this year's catch, without erasing history.
            return new LegendaryCatchRecord(Math.Max(year, currentYear), lifetimeCount);
        }
        // Vanilla stores count and maximum size, but no catch year. Import old
        // catches conservatively instead of granting a possible second catch this year.
        return new LegendaryCatchRecord(lifetimeCount > 0 ? currentYear : 0, lifetimeCount);
    }

    internal static bool IsAvailable(LegendaryCatchRecord record, int currentYear) =>
        currentYear > 0 && record.Year < currentYear;

    internal static bool IsSuccessfulCatch(bool fromPond, int numberCaught, int before, int after) =>
        !fromPond && numberCaught > 0 && after > before;
}

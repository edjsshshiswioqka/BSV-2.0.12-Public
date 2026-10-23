using System.Collections.Generic;
using System.Linq;
namespace BetterStardewValley.Totems;

// A — original BSV rules. No game objects, RNG, inventories or third-party implementation.
internal enum TotemKind { Harvest, Growth, Processing, Thunderstorm }
internal sealed record TotemState(int Schema = 1, int Harvest = -1, int Growth = -1, int Processing = -1, int StormDay = -1)
{
    internal bool Valid(int season, int day) => Schema == 1 && Harvest >= -1 && Harvest <= season
        && Growth >= -1 && Growth <= season && Processing >= -1 && Processing <= season && StormDay >= -1 && StormDay <= day;
    internal int Remaining(TotemKind kind, int season, int day) => kind switch
    {
        TotemKind.Harvest => Harvest == season ? 1 : 0,
        TotemKind.Growth => Growth == season ? 1 : 0,
        TotemKind.Processing => Processing == season ? 1 : 0,
        TotemKind.Thunderstorm => StormDay < 0 ? 0 : Math.Max(0, 12 - (day - StormDay)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    internal TotemState Used(TotemKind kind, int season, int day) => kind switch
    {
        TotemKind.Harvest => this with { Harvest = season },
        TotemKind.Growth => this with { Growth = season },
        TotemKind.Processing => this with { Processing = season },
        TotemKind.Thunderstorm => this with { StormDay = day },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
internal sealed record CropBlessing(int Schema, string Identity, string Seed)
{
    internal bool Matches(string seed) => Schema == 1 && Guid.TryParseExact(Identity, "N", out _) && !string.IsNullOrWhiteSpace(Seed) && Seed == seed;
}
internal static class TotemRules
{
    internal const string Prefix = "Eddie.BetterStardewValley/";
    internal const string SharedKey = Prefix + "Totems.v1";
    internal const string BlessingKey = Prefix + "HarvestBlessing.v1";
    internal static string Id(TotemKind kind) => "Eddie.BetterStardewValley_" + kind + "Totem";
    // Called from per-frame draw and placement hooks: one dictionary lookup, no allocation.
    private static readonly Dictionary<string, TotemKind> Kinds = Enum.GetValues<TotemKind>()
        .SelectMany(k => new[] { (Id(k), k), ("(BC)" + Id(k), k) })
        .ToDictionary(p => p.Item1, p => p.Item2, StringComparer.Ordinal);
    internal static bool TryKind(string? id, out TotemKind kind)
    {
        kind = default;
        return id != null && Kinds.TryGetValue(id, out kind);
    }
    // Ceil(70%) leaves at least one remaining unit and never accelerates by more than 30%.
    // Integer quotient avoids overflow and floating-point drift, even for very long machine times.
    internal static long Shorten(long remaining)
        => remaining <= 0 ? 0 : remaining / 10 * 7 + (remaining % 10 * 7 + 9) / 10;
    internal static int QualityFloor(bool blessed, int qualityFertilizer) => !blessed ? 0 : qualityFertilizer > 0 ? 4 : 2;
    internal static long DueAt(long now, long due, int reservedYear)
    {
        if (due <= now) return due;
        long result = checked(now + Shorten(due - now));
        // Never deliver a fish reserved against next year's quota in the previous year.
        if (reservedYear > 0) result = Math.Max(result, checked((reservedYear - 1L) * 112 * 1440 + 360));
        return Math.Min(due, result);
    }
    internal static (int Phase, int Day)? Growth(IReadOnlyList<int> phases, int phase, int day, bool regrowing)
    {
        if (phases.Count < 2 || phase < 0 || phase >= phases.Count || day < 0 || phases.Any(x => x < 0)) return null;
        if (regrowing) return phase == phases.Count - 1 ? (phase, (int)Shorten(day)) : null;
        if (phase == phases.Count - 1) return (phase, day); // already harvestable
        if (day > phases[phase]) return null;
        long left = phases[phase] - day;
        for (int i = phase + 1; i < phases.Count - 1; i++) left += phases[i];
        long skip = left - Shorten(left);
        if (skip <= 0) return (phase, day);
        while (phase < phases.Count - 1)
        {
            int available = phases[phase] - day;
            if (skip < available) { day += (int)skip; break; }
            skip -= available; phase++; day = 0;
            if (skip == 0 && (phase == phases.Count - 1 || phases[phase] > 0)) break;
        }
        return (phase, day);
    }
}

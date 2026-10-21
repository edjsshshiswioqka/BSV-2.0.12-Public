using System;

namespace BetterStardewValley;

public sealed class HarvestBonusOptions
{
    public bool Enabled { get; set; } = true;
    public bool EnableFertilizerYieldBonus { get; set; } = true;
    public double DoubleChance { get; set; } = .20;
    public double TripleChance { get; set; } = .05;
    public double BasicDoubleChance { get; set; } = .25;
    public double BasicTripleChance { get; set; } = .05;
    public double QualityDoubleChance { get; set; } = .35;
    public double QualityTripleChance { get; set; } = .08;
    public double DeluxeDoubleChance { get; set; } = .45;
    public double DeluxeTripleChance { get; set; } = .10;
    internal (double Double,double Triple) Chances(int fertilizer) => fertilizer switch
    {
        1 => (BasicDoubleChance,BasicTripleChance), 2 => (QualityDoubleChance,QualityTripleChance),
        >=3 => (DeluxeDoubleChance,DeluxeTripleChance), _ => (DoubleChance,TripleChance)
    };
}

internal static class HarvestRules
{
    // One exclusive roll: triple, double, then ordinary yield.
    internal static (double Double, double Triple) YieldChances(int fertilizerQuality) => fertilizerQuality switch
    {
        1 => (.25, .05), 2 => (.35, .08), >= 3 => (.45, .10), _ => (.20, .05)
    };
    internal static int YieldMultiplier(int fertilizerQuality, double roll)
    {
        var p = YieldChances(fertilizerQuality);
        return roll < p.Triple ? 3 : roll < p.Triple + p.Double ? 2 : 1;
    }
    internal static int YieldMultiplier(int fertilizerQuality, double roll, HarvestBonusOptions options)
    {
        if (!options.Enabled) return 1;
        var p=options.Chances(options.EnableFertilizerYieldBonus?fertilizerQuality:0);
        if(!ValidPair(p.Double,p.Triple))p=YieldChances(options.EnableFertilizerYieldBonus?fertilizerQuality:0);
        double triple=p.Triple, twice=p.Double;
        return roll<triple?3:roll<triple+twice?2:1;
    }

    internal static bool ValidPair(double twice,double triple) => double.IsFinite(twice)&&double.IsFinite(triple)&&twice>=0&&triple>=0&&twice+triple<=1;

    internal static double NormalizeChance(double chance) =>
        double.IsFinite(chance) ? Math.Clamp(chance, 0.0, 1.0) : 0.5;

    internal static bool IsReady(bool dead, bool forage, int phaseCount,
        int phase, bool fullyGrown, int daysUntilRegrowth) =>
        !dead && !forage && phaseCount > 0 && phase >= phaseCount - 1
        && (!fullyGrown || daysUntilRegrowth <= 0);

    // Crop.harvest returns whether to REMOVE the crop, not whether it was harvested.
    internal static bool WasHarvested(bool wasReady, bool originalRan,
        bool removeCrop, int regrowDays, bool fullyGrownBefore, bool fullyGrownAfter,
        int daysBefore, int daysAfter) =>
        wasReady && originalRan && (removeCrop ||
            (regrowDays > 0 && fullyGrownAfter && daysAfter == regrowDays
                && (!fullyGrownBefore || daysAfter > daysBefore)));

    internal static bool Wins(double roll, double chance) => roll < NormalizeChance(chance);
}

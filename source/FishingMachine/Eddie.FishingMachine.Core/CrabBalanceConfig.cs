namespace Eddie.FishingMachine.Core;

public class CrabBalanceConfig
{
    private int cycleGameMinutes=MachineCycleRules.DefaultMinutes;
    private bool legacyUniformAssigned;
    public int CycleGameMinutes { get => cycleGameMinutes; set { cycleGameMinutes=value;legacyUniformAssigned=true; } }

    // LevelDurationVersion is config-only: it must not invalidate saved paid-cycle schemas.
    public int LevelDurationVersion { get; set; }
    public bool UniformCycleWasCustomized { get; set; }
    private int? levelTwoCycleGameMinutes;
    public int LevelTwoCycleGameMinutes { get => levelTwoCycleGameMinutes ?? (legacyUniformAssigned&&LevelDurationVersion==0?cycleGameMinutes:5760); set => levelTwoCycleGameMinutes=value; }

    public int MinutesForLevel(int level) => level switch
    {
        1 => CycleGameMinutes, 2 => LevelTwoCycleGameMinutes,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
    public int CycleRulesVersion { get; set; }
    public int? PreviousCycleGameMinutes { get; set; }
    public bool MigrateCycle()
    {
        if (CycleRulesVersion >= MachineCycleRules.Version) return false;
        PreviousCycleGameMinutes = CycleGameMinutes;
        // Keep explicit saved durations. Missing values already receive the two-day initializer.
        CycleRulesVersion = MachineCycleRules.Version;
        return true;
    }
    public int LevelOneBait { get; set; } = 150;
    public int LevelTwoBait { get; set; } = 350;
    public double LevelOneCriticalChance { get; set; } = .20;
    public double LevelTwoCriticalChance { get; set; } = .55;
    public int Required(int level, bool luremaster) => (level == 2 ? LevelTwoBait : LevelOneBait) / (luremaster ? 2 : 1);
    public void Validate()
    {
        if (CycleGameMinutes < 10 || CycleGameMinutes > 40320
            || LevelTwoCycleGameMinutes < 10 || LevelTwoCycleGameMinutes > 40320
            || LevelOneBait < 2 || LevelOneBait > 100000 || LevelOneBait % 2 != 0
            || LevelTwoBait < 2 || LevelTwoBait > 100000 || LevelTwoBait % 2 != 0
            || !double.IsFinite(LevelOneCriticalChance) || LevelOneCriticalChance < 0 || LevelOneCriticalChance > 1
            || !double.IsFinite(LevelTwoCriticalChance) || LevelTwoCriticalChance < 0 || LevelTwoCriticalChance > 1)
            throw new ArgumentException("Invalid crab balance: duration 10..40320; even bait 2..100000; chance 0..1.");
    }
}

namespace Eddie.FishingMachine.Core;

public sealed class BaitRates
{
    public double Fish { get; set; }
    public double OtherItem { get; set; }
    public double TargetShareOfFish { get; set; }
    public double Nothing => 0;
    public double FishShare => Fish / (Fish + OtherItem);
    public double OtherShare => OtherItem / (Fish + OtherItem);

    public void Validate(string name)
    {
        foreach (double value in new[] { Fish, OtherItem, TargetShareOfFish })
            if (!double.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentException($"{name}: probabilities must be finite and between 0 and 1.");
        if (Fish + OtherItem <= 0)
            throw new ArgumentException($"{name}: at least one catch weight must be positive.");
        if (Fish + OtherItem > 1)
            throw new ArgumentException($"{name}: fish plus other-item probability exceeds 1.");
    }
}

public sealed class BalanceConfig
{
    /// <summary>0 legacy, 1 BSV 2.0, 2 BSV 2.0.14 bait rules (wild, magnet, magic bait; per-level challenge and targeted bait).</summary>
    public int FishingBalanceVersion { get; set; } = 2;
    public double LevelTwoTripleChance { get; set; } = .10;
    public BalanceConfig ForNewCycle()
    {
        if(FishingBalanceVersion>=2)return this;
        var c=System.Text.Json.JsonSerializer.Deserialize<BalanceConfig>(System.Text.Json.JsonSerializer.Serialize(this))!;
        if(c.FishingBalanceVersion<1)
        {
            c.BaitPerCycle=200;c.LevelTwoBaitPerCycle=300;c.LevelThreeBaitPerCycle=400;
            c.FishCriticalChance=.45;c.LevelTwoCriticalChance=.55;c.LevelTwoTripleChance=.10;c.LevelThreeCriticalChance=.66;c.LevelThreeTripleChance=.24;
            c.RulesVersion=1;
        }
        // Every new cycle uses the current bait rules; a running cycle keeps the rules it was paid with.
        c.FishingBalanceVersion=2;return c;
    }
    public bool DebugProduction {get;set;}
    public int BaitPerCycle { get; set; } = 200;
    public int LevelTwoBaitPerCycle { get; set; } = 300;
    public int LevelThreeBaitPerCycle { get; set; } = 400;
    private int cycleGameMinutes=MachineCycleRules.DefaultMinutes;
    private bool legacyUniformAssigned;
    public int CycleGameMinutes { get => cycleGameMinutes; set { cycleGameMinutes=value;legacyUniformAssigned=true; } }

    // LevelDurationVersion is config-only: it must not invalidate saved paid-cycle schemas.
    public int LevelDurationVersion { get; set; }
    public bool UniformCycleWasCustomized { get; set; }
    private int? levelTwoCycleGameMinutes;
    public int LevelTwoCycleGameMinutes { get => levelTwoCycleGameMinutes ?? (legacyUniformAssigned&&LevelDurationVersion==0?cycleGameMinutes:4320); set => levelTwoCycleGameMinutes=value; }
    private int? levelThreeCycleGameMinutes;
    public int LevelThreeCycleGameMinutes { get => levelThreeCycleGameMinutes ?? (legacyUniformAssigned&&LevelDurationVersion==0?cycleGameMinutes:5760); set => levelThreeCycleGameMinutes=value; }

    public int MinutesForLevel(int level) => level switch
    {
        1 => CycleGameMinutes, 2 => LevelTwoCycleGameMinutes,
        3 => LevelThreeCycleGameMinutes,
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
    public double FishCriticalChance { get; set; } = .45;
    public double LevelTwoCriticalChance { get; set; } = .55;
    public double LevelThreeCriticalChance { get; set; } = .66;
    public double LevelThreeTripleChance { get; set; } = .24;
    public int RulesVersion { get; set; }
    public bool Migrate()
    {
        if(RulesVersion>=1)return false;
        // 0.8.0 wrote the old default into fishing-machine.json. Upgrade it once.
        if(Math.Abs(LevelThreeTripleChance-.15)<1e-12)LevelThreeTripleChance=.20;
        RulesVersion=1;
        return true;
    }
    public BaitRates Normal { get; set; } = new() { Fish = 0.20, OtherItem = 0.15 };
    public BaitRates Deluxe { get; set; } = new() { Fish = 0.30, OtherItem = 0.05 };
    public BaitRates Targeted { get; set; } = new() { Fish = 0.20, OtherItem = 0, TargetShareOfFish = 0.70 };
    public VisualOptions Visual { get; set; } = new();

    public void Validate()
    {
        if (LevelTwoBaitPerCycle < 1 || LevelTwoBaitPerCycle > 100000 || LevelThreeBaitPerCycle < 1 || LevelThreeBaitPerCycle > 100000
            || CycleGameMinutes < 10 || CycleGameMinutes > 40320
            || LevelTwoCycleGameMinutes < 10 || LevelTwoCycleGameMinutes > 40320
            || LevelThreeCycleGameMinutes < 10 || LevelThreeCycleGameMinutes > 40320) throw new ArgumentException("Invalid machine bait/duration settings.");
        if (BaitPerCycle <= 0 || BaitPerCycle > 100000)
            throw new ArgumentException("BaitPerCycle must be between 1 and 100000.");
        if (CycleGameMinutes <= 0)
            throw new ArgumentException("CycleGameMinutes must be positive.");
        if(!double.IsFinite(FishCriticalChance) || FishCriticalChance<0 || FishCriticalChance>1)
            throw new ArgumentException("FishCriticalChance must be between 0 and 1.");
        if (new[]{LevelTwoCriticalChance,LevelThreeCriticalChance,LevelThreeTripleChance}.Any(p=>!double.IsFinite(p)||p<0||p>1)
            || LevelThreeCriticalChance+LevelThreeTripleChance>1)throw new ArgumentException("Invalid level critical probabilities.");
        if(FishingBalanceVersion is <0 or >2 || !double.IsFinite(LevelTwoTripleChance) || LevelTwoTripleChance<0 || LevelTwoCriticalChance+LevelTwoTripleChance>1)throw new ArgumentException("Invalid fishing balance snapshot.");
        if (Normal is null || Deluxe is null || Targeted is null)
            throw new ArgumentException("All three bait rate groups are required.");
        Normal.Validate(nameof(Normal));
        Deluxe.Validate(nameof(Deluxe));
        Targeted.Validate(nameof(Targeted));
        if (Normal.TargetShareOfFish != 0 || Deluxe.TargetShareOfFish != 0)
            throw new ArgumentException("Only targeted bait can specify TargetShareOfFish.");
        if(Visual is null)throw new ArgumentException("Visual settings must not be null.");
        Visual.Validate();
    }

    public BaitRates GetRates(BaitKind kind, int level=1)
    {
        var rates=BaseRates(kind);
        // The per-level share replaces only the default 70%; a share the player set in fishing-machine.json is kept.
        double target=kind==BaitKind.Targeted&&FishingBalanceVersion>=2&&Math.Abs(rates.TargetShareOfFish-.70)<1e-9?BaitRules.TargetShare(level):rates.TargetShareOfFish;
        if(level<3)return target==rates.TargetShareOfFish?rates:new(){Fish=rates.Fish,OtherItem=rates.OtherItem,TargetShareOfFish=target};
        double other=rates.OtherShare/2;
        return new(){Fish=1-other,OtherItem=other,TargetShareOfFish=target};
    }
    private BaitRates BaseRates(BaitKind kind) => kind switch
    {
        BaitKind.Normal or BaitKind.GiantLobster or BaitKind.Wild or BaitKind.Magnet or BaitKind.Magic => Normal,
        BaitKind.Challenge => Normal,
        BaitKind.Deluxe => Deluxe,
        BaitKind.Targeted => Targeted,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

namespace Eddie.FishingMachine.Core;

public enum BaitKind { Normal = 0, Deluxe = 1, Targeted = 2, Challenge = 3, GiantLobster = 4 }
public enum CatchOutcome { OrdinaryFish, TargetFish, OtherItem, Nothing }

public sealed record BatchSummary(int OrdinaryFish, int TargetFish, int OtherItems, int Nothing, int CriticalFish=0,int CriticalTargetFish=0,int ChallengeExtraFish=0)
{
    public int Fish => OrdinaryFish + TargetFish;
    public int Total => Fish + OtherItems + Nothing;
    public int ProducedFish => Fish + ChallengeExtraFish + CriticalFish;
    public int ProducedTargetFish => TargetFish + CriticalTargetFish;
}

/// <summary>
/// Pure outcome probabilities only. Does not create game items, consume inventory,
/// decide species/quality, or check location eligibility. An adapter must do those.
/// </summary>
public static class CatchRoller
{
    public static CatchOutcome Decide(BaitKind kind, BaitRates rates, double outcomeRoll, double targetRoll)
    {
        if (!Enum.IsDefined(typeof(BaitKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        rates.Validate(nameof(rates));
        ValidateRoll(outcomeRoll);
        ValidateRoll(targetRoll);
        if (outcomeRoll < rates.FishShare)
            return kind == BaitKind.Targeted && targetRoll < rates.TargetShareOfFish
                ? CatchOutcome.TargetFish : CatchOutcome.OrdinaryFish;
        return CatchOutcome.OtherItem; // Every eligible bait produces fish or ordinary items.
    }

    public static BatchSummary Simulate(BalanceConfig config, BaitKind kind, int seed, int level=0)
    {
        config.Validate();
        if(level==0)level=kind==BaitKind.Challenge?2:1;
        if(level is <1 or >3 || level==1 && kind==BaitKind.Challenge)throw new ArgumentOutOfRangeException(nameof(level));
        var random = new Random(seed);
        var criticalRandom=FishingCritical.RandomFor(seed);
        var rates = config.GetRates(kind,level);
        int fish = 0, targeted = 0, other = 0, nothing = 0;
        int critical=0,criticalTargeted=0,extra=0;
        int required=MachineUpgrade.RequiredBait(new ProductionState{Level=level},config);
        // One draw per bait for the machine as a whole. Rod count is NOT a multiplier.
        for (int i = 0; i < required; i++)
        {
            var outcome = Decide(kind, rates, random.NextDouble(), random.NextDouble());
            if(outcome is CatchOutcome.OrdinaryFish or CatchOutcome.TargetFish)
            {
                int multiplicity=kind==BaitKind.Challenge?3:1;
                extra+=multiplicity-1;
                int bonus=multiplicity*MachineLevelRules.ExtraMultiplier(config,level,criticalRandom.NextDouble());
                critical+=bonus;if(outcome==CatchOutcome.TargetFish)criticalTargeted+=bonus;
            }
            switch (outcome)
            {
                case CatchOutcome.OrdinaryFish: fish++; break;
                case CatchOutcome.TargetFish: targeted++; break;
                case CatchOutcome.OtherItem: other++; break;
                default: nothing++; break;
            }
        }
        return new(fish, targeted, other, nothing,critical,criticalTargeted,extra);
    }

    private static void ValidateRoll(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value >= 1)
            throw new ArgumentOutOfRangeException(nameof(value), "Rolls must be in [0,1).");
    }
}

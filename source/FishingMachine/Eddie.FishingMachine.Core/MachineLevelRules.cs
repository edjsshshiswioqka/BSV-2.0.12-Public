namespace Eddie.FishingMachine.Core;

public static class MachineLevelRules
{
    private static void Check(double r)
    { if(!double.IsFinite(r)||r<0||r>=1)throw new ArgumentOutOfRangeException(nameof(r)); }
    /// <summary>Bait tier for quality: 0 plain (bait, giant lobster bait), 1 targeted / challenge, 2 deluxe.</summary>
    public static int BaitTier(BaitKind kind)=>kind switch{BaitKind.Deluxe=>2,BaitKind.Targeted or BaitKind.Challenge=>1,_=>0};
    /// <summary>Quality shares normal / silver / gold / iridium. Each bait in a mixed load is judged by its own tier.</summary>
    public static double[] QualityShares(int level,int version=1,BaitKind kind=BaitKind.Normal)
    {
        if(version==0)return level switch{1=>new[]{.75,.25,0,0},2=>new[]{.35,.40,.25,0},3=>new[]{0,0,.20,.80},_=>throw new ArgumentOutOfRangeException(nameof(level))};
        int tier=BaitTier(kind);
        return (level,tier) switch
        {
            (1,0)=>new[]{.75,.25,0,0},   (1,1)=>new[]{.65,.30,.05,0},   (1,2)=>new[]{.50,.40,.10,0},
            (2,0)=>new[]{0,.50,.35,.15}, (2,1)=>new[]{0,.40,.40,.20},   (2,2)=>new[]{0,.30,.45,.25},
            (3,0)=>new[]{0,0,.25,.75},   (3,1)=>new[]{0,0,.18,.82},     (3,2)=>new[]{0,0,.10,.90},
            _=>throw new ArgumentOutOfRangeException(nameof(level))
        };
    }
    public static int Quality(int level,double roll,int version=1,BaitKind kind=BaitKind.Normal)
    {
        Check(roll);
        var p=QualityShares(level,version,kind);
        if(version==0)return level switch {1=>roll<p[1]?1:0,2=>roll<p[1]?1:roll<p[1]+p[2]?2:0,3=>roll<p[3]?4:2,_=>throw new ArgumentOutOfRangeException(nameof(level))};
        int[] q={0,1,2,4};double acc=0;int last=0;
        for(int i=0;i<4;i++){if(p[i]<=0)continue;acc+=p[i];last=q[i];if(roll<acc)return q[i];}
        return last;
    }
    public static double[] TreasureShares(int level,int version=1)=>level switch
    {1=>version==0?new[]{0d,0}:new[]{.03,.005},2=>new[]{.06,.03},3=>version==0?new[]{.10,.15}:new[]{.10,.06},_=>throw new ArgumentOutOfRangeException(nameof(level))};
    // Mutually exclusive: Lv3 66% double, 24% triple, 10% ordinary.
    public static int ExtraMultiplier(BalanceConfig c,int level,double roll)
    {
        Check(roll);
        return level switch {1=>FishingCritical.Bonus(c.FishCriticalChance,roll),2=>roll<c.LevelTwoCriticalChance?1:c.FishingBalanceVersion>0&&roll<c.LevelTwoCriticalChance+c.LevelTwoTripleChance?2:0,
            3=>roll<c.LevelThreeCriticalChance?1:roll<c.LevelThreeCriticalChance+c.LevelThreeTripleChance?2:0,
            _=>throw new ArgumentOutOfRangeException(nameof(level))};
    }
    public static int Treasure(int level,double roll,int version=1)
    { Check(roll);var p=TreasureShares(level,version);return roll<p[0]?1:roll<p[0]+p[1]?2:0; }
}

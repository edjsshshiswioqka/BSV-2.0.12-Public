namespace Eddie.FishingMachine.Core;

public static class MachineLevelRules
{
    private static void Check(double r)
    { if(!double.IsFinite(r)||r<0||r>=1)throw new ArgumentOutOfRangeException(nameof(r)); }
    public static double[] QualityShares(int level,int version=1)=>level switch
    {1=>new[]{.75,.25,0,0},2=>version==0?new[]{.35,.40,.25,0}:new[]{0,.50,.35,.15},3=>version==0?new[]{0,0,.20,.80}:new[]{0,0,.25,.75},_=>throw new ArgumentOutOfRangeException(nameof(level))};
    public static double[] TreasureShares(int level,int version=1)=>level switch
    {1=>version==0?new[]{0d,0}:new[]{.03,.005},2=>new[]{.06,.03},3=>version==0?new[]{.10,.15}:new[]{.10,.06},_=>throw new ArgumentOutOfRangeException(nameof(level))};
    public static int Quality(int level,double roll,int version=1)
    {
        Check(roll);
        var p=QualityShares(level,version);
        return level switch {1=>roll<p[1]?1:0,2=>roll<p[1]?1:roll<p[1]+p[2]?2:version==0?0:4,3=>roll<p[3]?4:2,_=>throw new ArgumentOutOfRangeException(nameof(level))};
    }
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

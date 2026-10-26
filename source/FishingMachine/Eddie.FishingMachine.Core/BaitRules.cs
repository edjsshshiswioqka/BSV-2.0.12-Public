namespace Eddie.FishingMachine.Core;

/// <summary>
/// BSV 2.0.14 (balance version 2): the machine lets every bait do what it does on a rod, and the effect grows with
/// the machine's level. Whatever a bait adds is then multiplied by the machine's own double/triple catch and quality.
/// </summary>
public static class BaitRules
{
    /// <summary>Targeted bait: share of fish that are the chosen species.</summary>
    public static double TargetShare(int level)=>level switch{1=>.72,2=>.80,3=>.88,_=>throw new ArgumentOutOfRangeException(nameof(level))};
    /// <summary>Wild bait: chance that one catch is two fish.</summary>
    public static double WildDouble(int level)=>level switch{1=>.25,2=>.40,3=>.50,_=>throw new ArgumentOutOfRangeException(nameof(level))};
    /// <summary>Challenge bait (level 2+): chance that one catch is three fish (otherwise one).</summary>
    public static double ChallengeTriple(int level)=>level switch{2=>.20,3=>.40,_=>0};
    /// <summary>Magnet: treasure / golden treasure chance per fish caught.</summary>
    public static double[] MagnetTreasure(int level)=>level switch{1=>new[]{.10,.02},2=>new[]{.20,.08},3=>new[]{.30,.15},_=>throw new ArgumentOutOfRangeException(nameof(level))};
    /// <summary>Magic bait and BSV giant lobster bait ignore season, time of day and weather (the vanilla magic bait rule).</summary>
    public static bool IgnoresSeasonTimeWeather(BaitKind kind)=>kind is BaitKind.Magic or BaitKind.GiantLobster;
    /// <summary>Fish in one catch before the machine's double/triple: wild 1-2, challenge 1 or 3, giant lobster always 3.</summary>
    public static int CatchSize(BaitKind kind,int level,double roll)=>kind switch
    {
        BaitKind.GiantLobster=>3,
        BaitKind.Challenge=>roll<ChallengeTriple(level)?3:1,
        BaitKind.Wild=>roll<WildDouble(level)?2:1,
        _=>1
    };
    public static int Treasure(BaitKind kind,int level,double roll,int version)
    {
        if(kind!=BaitKind.Magnet||version<2)return MachineLevelRules.Treasure(level,roll,version);
        if(!double.IsFinite(roll)||roll<0||roll>=1)throw new ArgumentOutOfRangeException(nameof(roll));
        var p=MagnetTreasure(level);return roll<p[0]?1:roll<p[0]+p[1]?2:0;
    }
}

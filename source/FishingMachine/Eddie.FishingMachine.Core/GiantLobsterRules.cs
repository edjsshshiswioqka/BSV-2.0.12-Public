namespace Eddie.FishingMachine.Core;

/// <summary>Shared identity and selection rules. No inventory, game RNG, or save mutations.</summary>
public static class GiantLobsterRules
{
    public const string Id="Eddie.BetterStardewValley_GiantLobster";
    public const string BaitId="Eddie.BetterStardewValley_GiantLobsterBait";
    public const string DinnerId="Eddie.BetterStardewValley_LobsterDinner";
    public const string Bait="(O)"+BaitId;
    public const double CrabChance=.05;
    public const double SpiritChance=.01;
    public const int HighValuePrice=300,DeluxeBarBonus=12;
    public const double ValueFactor=1.25,BarFactor=1.20;
    public static double ValueWeight(int basePrice)=>basePrice>=HighValuePrice?ValueFactor:1;
    public static int BarHeight(int nonBaitHeight)=>Math.Clamp((int)Math.Floor((nonBaitHeight+DeluxeBarBonus)*BarFactor),1,568);
    public static bool SelectCrab(bool ocean, double roll)
    {
        if(!double.IsFinite(roll)||roll<0||roll>=1)throw new ArgumentOutOfRangeException(nameof(roll));
        return ocean&&roll<CrabChance;
    }
    public static bool SelectSpirit(bool environmentAndQuota, bool normalFishOpportunity, BaitKind bait, double roll)
    {
        if(!double.IsFinite(roll)||roll<0||roll>=1)throw new ArgumentOutOfRangeException(nameof(roll));
        return environmentAndQuota&&normalFishOpportunity&&bait==BaitKind.GiantLobster&&roll<SpiritChance;
    }
    /// <summary>The dedicated 3% roll is the sole entry. Keep every ordinary row and its order,
    /// excluding this species even if another asset edit changes its marker probability.</summary>
    public static Dictionary<string,string> OrdinaryTrapData(IEnumerable<KeyValuePair<string,string>> data)
    {
        var result=new Dictionary<string,string>();
        foreach(var pair in data)if(pair.Key!=Id&&pair.Key!="(O)"+Id&&!LobsterKingRules.IsFish(pair.Key)&&!KingCrabRules.IsFish(pair.Key)&&!MutantLobsterRules.IsItem(pair.Key))result.Add(pair.Key,pair.Value);
        return result;
    }
}

namespace Eddie.FishingMachine.Core;

/// <summary>One uncapped Sewer base opportunity. Not a fish/legendary quota or a batch cap.</summary>
public static class MutantLobsterRules
{
    public const string Id="Eddie.BetterStardewValley_MutantLobsterKing";
    public const string Item="(O)"+Id;
    public const int Price=50000;
    public const double Chance=.0005;
    public static bool IsItem(string? id)=>id==Id||id==Item;
    public static bool Select(bool sewer,double roll)
    {
        if(!double.IsFinite(roll)||roll<0||roll>=1)throw new ArgumentOutOfRangeException(nameof(roll));
        return sewer&&roll<Chance;
    }
    public static Random RandomFor(int seed)=>new(unchecked(seed^0x373AC129));
}

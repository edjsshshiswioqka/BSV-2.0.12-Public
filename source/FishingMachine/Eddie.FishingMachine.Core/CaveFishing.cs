namespace Eddie.FishingMachine.Core;

/// <summary>Read-only equivalents of mine special-fish branches; never calls getFish or awards quest drops.</summary>
public static class CaveFishing
{
    public static FishChoice? SpecialFish(int area,int fishingLevel,int waterDepth)
    {
        double factor=1+.4*Math.Max(0,fishingLevel)+.1*Math.Max(0,waterDepth);
        return area switch
        {
            0 or 10 => new("(O)158",Math.Min(1,.02+.01*factor)),
            40 => new("(O)161",Math.Min(1,.015+.009*factor)),
            80 => new("(O)162",Math.Min(1,.01+.008*factor)),
            _ => null
        };
    }
    // Native CaveJelly comes from the lava mine area; upper mine floors don't produce river jelly.
    public static string? Jelly(int? mineArea,bool ocean)
        => mineArea.HasValue ? (mineArea==80 ? "(O)CaveJelly" : null) : (ocean?"(O)SeaJelly":"(O)RiverJelly");
}

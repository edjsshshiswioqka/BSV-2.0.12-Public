namespace Eddie.FishingMachine.Core;

public sealed record UpgradeSlot(int Index, string Id, int Count, bool SafeTool = false);
public sealed record UpgradeSpend(int Index, int Count);
public static class MachineUpgrade
{
    public const int IronRequired=25, GoldRequired=15, LevelTwoBait=300, LevelThreeBait=400;
    public static int RequiredBait(ProductionState s, BalanceConfig config) => s.Level switch { 3=>config.LevelThreeBaitPerCycle, 2=>config.LevelTwoBaitPerCycle, _=>config.BaitPerCycle };
    // Native standard/BigChest layouts expose 36/70 slots; legacy overflow is withdrawal-compatible.
    public static int Capacity(int level) => level switch { 1 => 70, 2 => 105, _ => 140 };
    public static (string Id,int Count)[] Requirements(int level)=>level switch
    {
        1=>new[]{("(O)335",25),("(O)336",15),("(O)787",3),("(O)709",10)},
        2=>new[]{("(O)337",15),("(O)336",25),("(O)787",8),("(T)IridiumRod",6)},
        _=>Array.Empty<(string,int)>()
    };
    public static long Progress(ProductionState s)=>s.Level==1?s.CompletedCycles:s.LevelCycles;
    public static bool IsMaterial(string id, int level=1) => Requirements(level).Any(r=>r.Id==id);
    public static bool IsTrash(string id) => id is "(O)167" or "(O)168" or "(O)169" or "(O)170" or "(O)171" or "(O)172";
    public static IReadOnlyList<UpgradeSpend>? Plan(ProductionState s, IEnumerable<UpgradeSlot> items)
    {
        if(s.StageProgressUnverified || s.Kind!=MachineKinds.Fishing || s.Level is not (1 or 2) || Progress(s) < MachineCycleRules.RequiredCycles(s) || s.Phase == "working") return null;
        var slots=items.ToArray();
        if(slots.Any(x=>x.Index<0 || x.Count<=0) || slots.Select(x=>x.Index).Distinct().Count()!=slots.Length)
            throw new ArgumentException("Invalid upgrade inventory.");
        var plan=new List<UpgradeSpend>();
        foreach(var requirement in Requirements(s.Level))
        {
            int left=requirement.Count;
            foreach(var slot in slots.Where(x=>x.Id==requirement.Id && (!x.Id.StartsWith("(T)") || x.SafeTool && x.Count==1)).OrderBy(x=>x.Index))
            {
                int take=Math.Min(left,slot.Count); if(take==0)break;
                plan.Add(new(slot.Index,take)); left-=take;
            }
            if(left>0)return null;
        }
        return plan;
    }
    // Only idle, empty machines travel in inventory. No production result can be carried twice.
    public static bool ValidPortable(string? raw)
    {
        if(raw==null)return true;
        try { var s=ProductionState.Load(raw); return s.Phase=="idle" && !s.BlocksRemoval; }
        catch { return false; }
    }
}

/// <summary>BSV treasure tables, independent from vanilla rod menus, quests and player statistics.</summary>
public static class LevelTwoCatch
{
    private static void Check(double r)
    { if(!double.IsFinite(r) || r<0 || r>=1)throw new ArgumentOutOfRangeException(nameof(r)); }
    public static int Quality(double roll)
    { Check(roll); return roll<.40 ? 1 : roll<.65 ? 2 : 0; }
    public static int Treasure(double roll)
    { Check(roll); return roll<.06 ? 1 : roll<.09 ? 2 : 0; }
    public static StoredCatch Reward(int tier, double roll)
    {
        Check(roll);
        if(tier==1)
        {
            var table=new[]{new StoredCatch("(O)378",5),new("(O)380",5),new("(O)384",3),new("(O)382",5),
                new("(O)535",2),new("(O)536",2),new("(O)60",1),new("(O)62",1)};
            return table[(int)(roll*table.Length)];
        }
        if(tier!=2)throw new ArgumentOutOfRangeException(nameof(tier));
        return roll switch
        {
            < .25 => new("(O)336",3), < .50 => new("(O)386",5), < .70 => new("(O)749",5),
            < .85 => new("(O)72",2), < .95 => new("(O)337",1), _ => new("(O)74",1)
        };
    }
}

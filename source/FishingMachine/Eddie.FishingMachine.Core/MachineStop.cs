namespace Eddie.FishingMachine.Core;

/// <summary>Switching a machine off in the middle of a cycle: what was already cast / checked stays, the rest of
/// the paid bait goes back. Crab machine with Luremaster: one bait covers two trap checks, so a half-used bait
/// still gets its second check before the cycle ends.</summary>
public static class MachineStop
{
    private static int Per(ProductionState s)=>s.Kind==MachineKinds.Crab&&s.Crab?.Luremaster==true?2:1;
    private static HashSet<int> Keep(ProductionState s)
    {
        var t=s.Timeline!;var keep=new HashSet<int>(Enumerable.Range(0,t.Cursor));
        if(Per(s)==2)
            foreach(var g in t.Plan.Take(t.Cursor).GroupBy(o=>o.Bait).Where(g=>g.Count()%2==1))
                keep.Add(t.Plan.FindIndex(t.Cursor,o=>o.Bait==g.Key));
        return keep;
    }
    public static bool WouldCut(ProductionState s)
        =>s.Phase=="working"&&s.Timeline is {} t&&t.Cursor<t.Plan.Count&&(s.Kind==MachineKinds.Fishing||s.Kind==MachineKinds.Crab&&s.Crab!=null)&&Keep(s).Count<t.Plan.Count;
    /// <summary>Returns the new state and the bait to hand back (index into the bait list as it was before the cut),
    /// or null when there is nothing to cut or it cannot be cut safely.</summary>
    public static (ProductionState State,List<(int Index,int Count)> Refund)? Cut(ProductionState original,long now)
    {
        if(!WouldCut(original))return null;
        var s=original.Snapshot();var t=s.Timeline!;bool crab=s.Kind==MachineKinds.Crab;int per=Per(s);
        int kinds=crab?t.CrabBait.Count:t.FishingBait.Count;
        var keep=Keep(s);
        var refund=Enumerable.Range(0,kinds).Select(b=>(Index:b,Count:(t.Plan.Count(o=>o.Bait==b)-keep.Count(i=>t.Plan[i].Bait==b))/per)).Where(r=>r.Count>0).ToList();
        if(t.Cursor==0||now<=s.StartedAt)
        {
            if(s.Pending.Count>0)return null; // nothing done yet, so nothing can be pending; never lose output
            return (s.IdleCopy(),refund);
        }
        var kept=keep.OrderBy(i=>i).Select(i=>t.Plan[i]).ToList();
        var ordinal=kept.Select((o,i)=>(o.Ordinal,i)).ToDictionary(p=>p.Ordinal,p=>p.i);
        var baitIndex=kept.Select(o=>o.Bait).Distinct().OrderBy(b=>b).Select((b,i)=>(b,i)).ToDictionary(p=>p.b,p=>p.i);
        if(crab)
        {
            t.CrabBait=baitIndex.OrderBy(p=>p.Value).Select(p=>t.CrabBait[p.Key] with{Count=kept.Count(o=>o.Bait==p.Key)/per}).ToList();
            s.Crab!.Consumed=t.CrabBait.ToList();s.Crab.Rolls=kept.Count;
        }
        else t.FishingBait=baitIndex.OrderBy(p=>p.Value).Select(p=>t.FishingBait[p.Key] with{Count=kept.Count(o=>o.Bait==p.Key)}).ToList();
        t.Plan=kept.Select(o=>o with{Ordinal=ordinal[o.Ordinal],Bait=baitIndex[o.Bait]}).ToList();
        t.LegendaryOpportunities=t.LegendaryOpportunities.Where(o=>ordinal.ContainsKey(o.Ordinal)).Select(o=>o with{Ordinal=ordinal[o.Ordinal]}).ToList();
        t.Prepared=null;t.PausedReason=null;
        s.BaitUsed=kept.Count/per;s.DueAt=Math.Max(s.StartedAt+1,Math.Max(now,kept[^1].At+1));
        return MachineTimeline.Valid(s)?(s,refund):null;
    }
}

namespace Eddie.FishingMachine.Core;

/// <summary>
/// The calendar can be moved backwards (cheat menus, date editors). Saved machine times then lie in the future and the
/// machine would wait for that date before casting again. Moving every saved time back by the same whole number of days
/// keeps the cycle exactly as paid (same casts, same times of day, same bait) and lets it run from today.
/// </summary>
public static class MachineRewind
{
    /// <summary>Latest moment the machine has already lived through. Never later than "now" unless the calendar moved back.</summary>
    public static long HighWater(ProductionState s)=>Math.Max(s.Continuity?.ObservedAt??0,s.Phase=="idle"?0:s.StartedAt);
    public static bool Needed(ProductionState s,long now)=>HighWater(s)>now;
    /// <summary>Whole days (in minutes, negative) that bring the machine back to `now` or earlier.</summary>
    public static long Delta(ProductionState s,long now)=>Needed(s,now)?-((HighWater(s)-now+1439)/1440*1440):0;
    public static void Shift(ProductionState s,long delta)
    {
        if(delta==0)return;
        if(delta%1440!=0)throw new ArgumentException("Only whole days keep the time-of-day rules of a cycle.");
        int days=checked((int)(delta/1440));
        if(s.Phase!="idle"){s.StartedAt+=delta;s.DueAt+=delta;}
        var t=s.Timeline;
        if(t!=null)
        {
            t.Plan=t.Plan.Select(o=>o with{At=o.At+delta}).ToList();
            t.OriginalDue+=delta;
            t.Claims=t.Claims.Select(c=>c with{Day=c.Day+days}).ToList();
            t.LegendaryOpportunities=t.LegendaryOpportunities.Select(o=>o with{Day=o.Day+days}).ToList();
            if(t.Prepared is {} p&&p.Phase!="idle"){p.StartedAt+=delta;p.DueAt+=delta;}
        }
        var c=s.Continuity;
        if(c!=null)
        {
            long M(long at)=>Math.Max(360,at+delta);
            c.ObservedAt=M(c.ObservedAt);c.NextAt=M(c.NextAt);
            c.Fuel=c.Fuel.Select(f=>f with{At=Math.Min(M(f.At),c.ObservedAt)}).ToList();
            foreach(var p in c.Policies)p.At=Math.Min(M(p.At),c.ObservedAt);
        }
    }
}

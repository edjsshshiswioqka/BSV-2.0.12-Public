namespace Eddie.FishingMachine.Core;
public sealed record CycleLegendaryOpportunity(int Day,int Ordinal,string OrdinaryId,int Count,int Quality,List<FishChoice> Species);
public sealed class LegendaryCycleStats
{
    public long Total {get;set;}
    public long Eligible {get;set;}
    public long Hits {get;set;}
    public long CompetitionFallbacks {get;set;}
    public long Submitted {get;set;}
    public LegendaryCycleStats Copy()=>(LegendaryCycleStats)MemberwiseClone();
    public bool Valid()=>Total>=Eligible&&Eligible>=Hits&&Hits>=0&&CompetitionFallbacks>=0&&Submitted>=0&&CompetitionFallbacks<=Hits-Submitted;
}
public sealed class CycleLegendaryDecision
{
    public bool StatsRecorded {get;set;}
    public bool Evaluated {get;set;}
    public int EligibleOpportunities {get;set;}
    public bool Hit {get;set;}
    public bool Committed {get;set;}
    public bool CompetitionFallback {get;set;}
    public string? Species {get;set;}
    public int Ordinal {get;set;}=-1;
}
public static class CycleLegendaryRules
{
    public static bool IsSpecies(string id)=>id is "(O)159" or "(O)160" or "(O)163" or "(O)682" or "(O)775";
    public static double Chance(int level)=>level==2?.05:level==3?.15:0;
    public static bool Hit(int level,double roll)
    {if(!double.IsFinite(roll)||roll<0||roll>=1)throw new ArgumentOutOfRangeException(nameof(roll));return roll<Chance(level);}
    public static CycleLegendaryDecision Decide(int level,int seed,IReadOnlyList<CycleLegendaryOpportunity> opportunities)
    {
        var d=new CycleLegendaryDecision{Evaluated=true,EligibleOpportunities=opportunities.Count};
        if(level<2||opportunities.Count==0)return d;
        var random=new Random(MachineTimeline.SeedFor(seed,0,0x671CC12));
        if(!(d.Hit=Hit(level,random.NextDouble())))return d;
        // Preserve existing species weights; more legal windows don't duplicate a species' weight.
        var species=opportunities.SelectMany(p=>p.Species).GroupBy(p=>p.Id).Select(g=>g.MaxBy(p=>p.Weight)!).ToArray();
        double u=random.NextDouble()*species.Sum(f=>f.Weight);var chosen=species[^1];
        foreach(var f in species){u-=f.Weight;if(u<0){chosen=f;break;}}
        var legal=opportunities.Where(p=>p.Species.Any(f=>f.Id==chosen.Id)).ToArray();
        d.Species=chosen.Id;d.Ordinal=legal[random.Next(legal.Length)].Ordinal;return d;
    }
}

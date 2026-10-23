using System.Text.Json;
namespace Eddie.FishingMachine.Core;

public sealed record MachineOpportunity(long At,int Ordinal,int Bait);
public sealed record DatedMachineClaim(int Day,string Id,string Token);
/// <summary>Paid budget only, never a second inventory. Outcomes are stored in the parent Pending.</summary>
public sealed class MachineTimeline
{
    public int Version {get;set;}=1;
    public List<CycleLegendaryOpportunity> LegendaryOpportunities {get;set;}=new();
    public CycleLegendaryDecision LegendaryDecision {get;set;}=new();
    public List<MachineOpportunity> Plan {get;set;}=new();
    public List<BaitSpend> FishingBait {get;set;}=new();
    public List<CrabBait> CrabBait {get;set;}=new();
    public BalanceConfig? FishingConfig {get;set;}
    public CrabBalanceConfig? CrabConfig {get;set;}
    public int Cursor {get;set;}
    public ProductionState? Prepared {get;set;}
    public List<DatedMachineClaim> Claims {get;set;}=new();
    public string? PausedReason {get;set;}
    public long OriginalDue {get;set;}
    public MachineTimeline Copy()
    {
        var copy=(MachineTimeline)MemberwiseClone();copy.Plan=Plan.ToList();copy.FishingBait=FishingBait.ToList();copy.CrabBait=CrabBait.ToList();
        copy.LegendaryOpportunities=LegendaryOpportunities.Select(o=>o with{Species=o.Species.ToList()}).ToList();copy.LegendaryDecision=JsonSerializer.Deserialize<CycleLegendaryDecision>(JsonSerializer.Serialize(LegendaryDecision))!;copy.Claims=Claims.ToList();copy.Prepared=Prepared?.Snapshot();return copy;
        // Captured configs are immutable; live configuration is detached by StartFish/StartCrab.
    }
    public static int Day(long at)=>checked((int)((at-360)/1440));
    public static int Year(int day)=>day/112+1;
    public static int Season(int day)=>day%112/28;
    public static int Time(long at){long m=at-(long)Day(at)*1440;return checked((int)(m/60*100+m%60));}
    public static int SeedFor(int seed,int ordinal,int domain)
    {
        unchecked {uint x=(uint)seed ^ (uint)ordinal*0x9E3779B9u ^ (uint)domain;
            x^=x>>16;x*=0x7FEB352Du;x^=x>>15;x*=0x846CA68Bu;x^=x>>16;return (int)x;}
    }
    /// <summary>First moment after `start` by which `legalMinutes` of fishing time (6:00-2:00) have passed.</summary>
    public static long DueAfterLegal(long start,long legalMinutes)
    {
        if(legalMinutes<1)throw new ArgumentOutOfRangeException(nameof(legalMinutes));
        for(int day=Day(start);;day++)
        {
            long from=Math.Max(start,(long)day*1440+360),to=(long)day*1440+1560;
            if(to<=from)continue;
            if(to-from>=legalMinutes)return from+legalMinutes;
            legalMinutes-=to-from;
        }
    }
    private static List<(long From,long Length)> Windows(long start,long due)
    {
        var windows=new List<(long From,long Length)>();
        for(int day=Day(start);day<=Day(due);day++)
        {
            long from=Math.Max(start,(long)day*1440+360),to=Math.Min(due,(long)day*1440+1560);
            if(to>from)windows.Add((from,to-from));
        }
        return windows;
    }
    private static long AtOffset(List<(long From,long Length)> windows,long offset)
    {
        foreach(var w in windows){if(offset<w.Length)return w.From+offset;offset-=w.Length;}
        throw new ArgumentOutOfRangeException(nameof(offset));
    }
    /// <summary>Processing Totem compresses only unelapsed opportunities. No reroll, fuel charge or historical rewrite.</summary>
    public static void Shorten(ProductionState state,long now,long previousDue)
    {
        var t=state.Timeline;if(t==null||state.DueAt>=previousDue)return;
        if(now>=state.DueAt)throw new ArgumentException("Timeline acceleration must leave a positive remainder.");
        var oldWindows=Windows(now,previousDue);var newWindows=Windows(now,state.DueAt);
        long before=oldWindows.Sum(w=>w.Length),after=newWindows.Sum(w=>w.Length);
        if(after==0)throw new InvalidOperationException("No legal opportunity window remains after acceleration.");
        for(int i=t.Cursor;i<t.Plan.Count;i++)
        {
            var op=t.Plan[i];if(op.At<now)continue;
            long offset=oldWindows.Sum(w=>Math.Clamp(op.At-w.From,0,w.Length));
            t.Plan[i]=op with{At=AtOffset(newWindows,Math.Min(after-1,checked(offset*after)/before))};
        }
        t.Plan=t.Plan.Take(t.Cursor).Concat(t.Plan.Skip(t.Cursor).OrderBy(p=>p.At).ThenBy(p=>p.Ordinal)).ToList();
    }
    public static List<MachineOpportunity> Schedule(long start,long due,int seed,IReadOnlyList<int> bait)
    {
        if(start<360||due<=start||due-start>40320||bait.Count<1||bait.Count>100000)throw new ArgumentException("Invalid timeline budget.");
        var windows=Windows(start,due);
        long length=windows.Sum(w=>w.Length);if(length==0)throw new ArgumentException("Cycle contains no legal fishing time.");
        var random=new Random(SeedFor(seed,0,0x14a710));var result=new List<MachineOpportunity>(bait.Count);
        // Each consumed unit is independently placed before species eligibility is consulted.
        for(int i=0;i<bait.Count;i++)
        {
            result.Add(new(AtOffset(windows,random.NextInt64(length)),i,bait[i]));
        }
        return result.OrderBy(p=>p.At).ThenBy(p=>p.Ordinal).ToList();
    }
    public static ProductionState StartFish(ProductionState previous,BalanceConfig config,IReadOnlyList<BaitSpend> bait,long owner,long now,int seed)
    {
        config.Validate();int required=MachineUpgrade.RequiredBait(previous,config);
        long used=bait.Sum(p=>(long)p.Count);
        if(previous.Phase!="idle"||previous.BlocksRemoval||used<1||used>required)throw new ArgumentException("Invalid paid fish plan.");
        var s=previous.IdleCopy();s.Schema=8;s.CycleRulesVersion=1;s.OwnerId=owner;s.Phase="working";s.StartedAt=now;s.Seed=seed;s.BaitUsed=(int)used;s.CycleToken=Guid.NewGuid().ToString("N");
        s.CycleBait=required;
        // A full load keeps the level's cycle length. A partial load fishes for the same time per bait,
        // counted in legal fishing hours (6:00-2:00), so each bait is one cast at its own time of day.
        s.DueAt=used==required?now+config.MinutesForLevel(previous.Level)
            :DueAfterLegal(now,Math.Max(10,(long)Math.Ceiling(config.MinutesForLevel(previous.Level)*20.0/24.0*used/required)));
        s.FishingBalanceVersion=config.FishingBalanceVersion;
        var t=s.Timeline=new(){Version=2,OriginalDue=s.DueAt,FishingConfig=JsonSerializer.Deserialize<BalanceConfig>(JsonSerializer.Serialize(config)),FishingBait=bait.ToList()};
        t.Plan=Schedule(now,s.DueAt,seed,bait.SelectMany((b,i)=>Enumerable.Repeat(i,b.Count)).ToArray());return s;
    }
    public static ProductionState StartCrab(ProductionState previous,CrabBalanceConfig config,IReadOnlyList<CrabBait> bait,long owner,bool lure,bool book,long now,int seed)
    {
        config.Validate();int required=config.Required(previous.Level,lure);
        if(previous.Kind!=MachineKinds.Crab||previous.Phase!="idle"||previous.BlocksRemoval||bait.Sum(b=>(long)b.Count)!=required)throw new ArgumentException("Invalid paid crab plan.");
        var s=previous.IdleCopy();s.Schema=8;s.CycleRulesVersion=1;s.OwnerId=owner;s.Phase="working";s.StartedAt=now;s.DueAt=now+config.MinutesForLevel(previous.Level);s.Seed=seed;s.BaitUsed=required;s.CycleToken=Guid.NewGuid().ToString("N");
        s.Crab=new(){Luremaster=lure,Book=book,Consumed=bait.ToList(),Rolls=required*(lure?2:1)};
        var t=s.Timeline=new(){Version=2,OriginalDue=s.DueAt,CrabConfig=JsonSerializer.Deserialize<CrabBalanceConfig>(JsonSerializer.Serialize(config)),CrabBait=bait.ToList()};
        t.Plan=Schedule(now,s.DueAt,seed,bait.SelectMany((b,i)=>Enumerable.Repeat(i,b.Count*(lure?2:1))).ToArray());return s;
    }
    public static void Merge(ProductionState into,ProductionState unit)
    {
        into.FishCount+=unit.FishCount;into.OtherCount+=unit.OtherCount;into.EmptyCount+=unit.EmptyCount;
        into.ChallengeExtraFish+=unit.ChallengeExtraFish;into.CriticalFishCount+=unit.CriticalFishCount;
        into.NormalTreasures+=unit.NormalTreasures;into.AdvancedTreasures+=unit.AdvancedTreasures;into.TreasureItemCount+=unit.TreasureItemCount;
        if(into.Crab!=null&&unit.Crab!=null){into.Crab.MachineCrits+=unit.Crab.MachineCrits;into.Crab.BookDoubles+=unit.Crab.BookDoubles;into.Crab.BookBonus+=unit.Crab.BookBonus;}
        foreach(var p in unit.Pending)
        {int i=into.Pending.FindIndex(x=>x.Key==p.Key);if(i<0)into.Pending.Add(p);else into.Pending[i]=into.Pending[i] with{Count=checked(into.Pending[i].Count+p.Count)};}
        into.Timeline!.Cursor++;into.Timeline.PausedReason=null;into.Timeline.Prepared=null;
    }
    public static bool Valid(ProductionState s)
    {
        var t=s.Timeline;if(s.Schema!=8||t==null||t.Version is not(1 or 2)||s.CycleRulesVersion!=1||s.LegacyPrecomputed||s.OriginalCycleMinutes!=null
            ||s.Phase is not("working" or "ready")||s.Level<1||s.Level>(s.Kind==MachineKinds.Crab?2:3)||s.StartedAt<360||s.DueAt<=s.StartedAt
            ||t.OriginalDue<s.DueAt||t.OriginalDue-s.StartedAt>40320||!Guid.TryParseExact(s.CycleToken,"N",out _)||s.BaitUsed<1
            ||s.CompletedCycles<0||s.LevelCycles<0||s.LevelCycles>s.CompletedCycles||s.LegendaryPlans==null||s.LegendaryPlans.Count!=0
            ||t.Plan==null||t.Plan.Count<1||t.Plan.Count>100000||t.Cursor<0||t.Cursor>t.Plan.Count||s.Phase=="ready"&&t.Cursor!=t.Plan.Count
            ||t.Prepared!=null&&(t.Prepared.Timeline!=null||t.Prepared.Continuity!=null||t.Prepared.Level!=s.Level
                ||t.Prepared.Kind!=s.Kind||t.Prepared.BaitUsed!=1||(long)t.Prepared.FishCount+t.Prepared.OtherCount+t.Prepared.EmptyCount!=1
                ||t.Prepared.Pending==null||t.Prepared.Pending.Any(p=>p.Count<=0||string.IsNullOrWhiteSpace(p.Id)||p.Quality is not(0 or 1 or 2 or 4))
                ||new[]{t.Prepared.FishCount,t.Prepared.OtherCount,t.Prepared.EmptyCount,t.Prepared.CriticalFishCount,t.Prepared.ChallengeExtraFish,t.Prepared.TreasureItemCount}.Any(n=>n<0)
                ||t.Prepared.Pending.Sum(p=>(long)p.Count)>(long)t.Prepared.FishCount+t.Prepared.OtherCount+t.Prepared.CriticalFishCount+t.Prepared.ChallengeExtraFish+t.Prepared.TreasureItemCount+(t.Prepared.Crab?.BookBonus??0))
            ||t.Claims==null||t.Claims.Any(c=>c.Day<Day(s.StartedAt)||c.Day>Day(s.DueAt)||string.IsNullOrWhiteSpace(c.Id)||string.IsNullOrWhiteSpace(c.Token))
            ||t.Claims.Select(c=>(s.Kind==MachineKinds.Crab&&!MutantLobsterRules.IsItem(c.Id)?c.Day/28:Year(c.Day),c.Id)).Distinct().Count()!=t.Claims.Count
            ||t.Plan.Any(o=>o.At<s.StartedAt||o.At>=s.DueAt||Time(o.At)<600||Time(o.At)>=2600||o.Bait<0||o.Ordinal<0||o.Ordinal>=t.Plan.Count)
            ||!t.Plan.SequenceEqual(t.Plan.OrderBy(p=>p.At).ThenBy(p=>p.Ordinal))||t.Plan.Select(o=>o.Ordinal).Distinct().Count()!=t.Plan.Count
            ||s.Pending==null||s.Pending.Any(p=>p.Count<=0||string.IsNullOrWhiteSpace(p.Id)||p.Quality is not(0 or 1 or 2 or 4))
            ||new[]{s.FishCount,s.OtherCount,s.EmptyCount,s.CriticalFishCount,s.ChallengeExtraFish,s.TreasureItemCount,s.NormalTreasures,s.AdvancedTreasures}.Any(n=>n<0)
            ||(long)s.FishCount+s.OtherCount+s.EmptyCount!=t.Cursor)return false;
        if(t.LegendaryOpportunities==null||t.LegendaryDecision==null||t.LegendaryOpportunities.Count>t.Cursor
            ||t.LegendaryOpportunities.Any(o=>o.Ordinal<0||o.Ordinal>=t.Plan.Count||o.Count<1||o.Count>9||o.Quality is not(0 or 1 or 2 or 4)
                ||string.IsNullOrWhiteSpace(o.OrdinaryId)||o.Species==null||o.Species.Count==0||o.Species.Any(f=>!f.IsLegendary||f.Id==Production.SpiritId||f.Weight<=0||!double.IsFinite(f.Weight)))
            ||t.LegendaryOpportunities.Select(o=>o.Ordinal).Distinct().Count()!=t.LegendaryOpportunities.Count
            ||t.LegendaryDecision.EligibleOpportunities<0||t.LegendaryDecision.EligibleOpportunities>t.LegendaryOpportunities.Count
            ||t.LegendaryOpportunities.Any(o=>!t.Plan.Take(t.Cursor).Any(p=>p.Ordinal==o.Ordinal&&Day(p.At)==o.Day)||o.Species.Any(f=>!CycleLegendaryRules.IsSpecies(f.Id)))
            ||t.LegendaryDecision.Hit&&(!t.LegendaryDecision.Evaluated||s.Level<2||t.LegendaryDecision.EligibleOpportunities==0
                ||!t.LegendaryOpportunities.Any(o=>o.Ordinal==t.LegendaryDecision.Ordinal&&o.Species.Any(f=>f.Id==t.LegendaryDecision.Species)))
            ||t.LegendaryDecision.Committed&&(!t.LegendaryDecision.Evaluated||!t.LegendaryDecision.Hit||t.LegendaryDecision.CompetitionFallback)
            ||t.LegendaryDecision.CompetitionFallback&&!t.LegendaryDecision.Hit
            ||t.LegendaryDecision.Evaluated&&t.Cursor!=t.Plan.Count
            ||t.LegendaryDecision.StatsRecorded&&(!t.LegendaryDecision.Evaluated||t.LegendaryDecision.Hit&&!t.LegendaryDecision.Committed&&!t.LegendaryDecision.CompetitionFallback)
            ||!t.LegendaryDecision.Hit&&(t.LegendaryDecision.Species!=null||t.LegendaryDecision.Ordinal!=-1)
            ||t.LegendaryDecision.Committed&&!t.Claims.Any(c=>c.Id==t.LegendaryDecision.Species&&t.LegendaryOpportunities.Any(o=>o.Ordinal==t.LegendaryDecision.Ordinal&&o.Day==c.Day))
            ||t.Version>=2&&s.Kind==MachineKinds.Fishing&&t.Claims.Count(c=>c.Id!=Production.SpiritId)>1)return false;
        try
        {
            if(s.Kind==MachineKinds.Fishing)
            {
                if(t.FishingConfig==null||t.CrabConfig!=null||s.Crab!=null||t.FishingBait==null||t.FishingBait.Any(b=>b.Count<=0||!Enum.IsDefined(typeof(BaitKind),b.Kind)))return false;
                if(s.FishingBalanceVersion is <0 or >1||s.FishingBalanceVersion!=t.FishingConfig.FishingBalanceVersion
                    ||s.ChallengeExtraFish>2L*s.FishCount||s.CriticalFishCount>(s.Level==3||s.Level==2&&s.FishingBalanceVersion==1?2L:1L)*(s.FishCount+s.ChallengeExtraFish)
                    ||(long)s.NormalTreasures+s.AdvancedTreasures>s.FishCount||s.TreasureItemCount>5L*(s.NormalTreasures+s.AdvancedTreasures))return false;
                t.FishingConfig.Validate();if(t.Plan.Count!=s.BaitUsed||t.FishingBait.Sum(b=>(long)b.Count)!=s.BaitUsed||t.Plan.Any(p=>p.Bait>=t.FishingBait.Count))return false;
                for(int i=0;i<t.FishingBait.Count;i++)if(t.Plan.Count(p=>p.Bait==i)!=t.FishingBait[i].Count)return false;
            }
            else if(s.Kind==MachineKinds.Crab)
            {
                var b=s.Crab;if(b==null||t.CrabConfig==null||t.FishingConfig!=null||t.CrabBait==null||t.CrabBait.Any(x=>x.Count<=0)||t.CrabBait.Sum(x=>(long)x.Count)!=s.BaitUsed)return false;
                t.CrabConfig.Validate();if(b.Rolls!=t.Plan.Count||s.BaitUsed*(b.Luremaster?2L:1L)!=b.Rolls||t.Plan.Any(p=>p.Bait>=t.CrabBait.Count)||s.CriticalFishCount!=b.MachineCrits||b.BookBonus<0)return false;
                if(!b.Consumed.SequenceEqual(t.CrabBait)||b.MachineCrits<0||b.BookDoubles<0||b.BookDoubles>s.FishCount+s.OtherCount||!b.Book&&b.BookBonus!=0)return false;
                for(int i=0;i<t.CrabBait.Count;i++)if(t.Plan.Count(p=>p.Bait==i)!=t.CrabBait[i].Count*(b.Luremaster?2:1))return false;
                if(s.Pending.Any(p=>(LobsterKingRules.IsFish(p.Id)||t.Version>=2&&MutantLobsterRules.IsItem(p.Id))&&(p.Count!=1||p.WildClaim==null||!t.Claims.Any(c=>c.Id==p.Id&&c.Token==p.WildClaim))))return false;
            }
            else return false;
            return s.Pending.Sum(p=>(long)p.Count)<=s.FishCount+s.OtherCount+(long)s.ChallengeExtraFish+s.CriticalFishCount+s.TreasureItemCount+(s.Crab?.BookBonus??0);
        }catch{return false;}
    }
}

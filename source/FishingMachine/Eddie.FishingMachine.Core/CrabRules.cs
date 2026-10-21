using System.Globalization;
namespace Eddie.FishingMachine.Core;

public static class MachineKinds
{
    public const string Fishing="FishingMachine", Crab="AutomaticCrabCatcher";
    public const string FishingId="Eddie.FishingMachine_Machine", CrabId="Eddie.BetterStardewValley_CrabCatcher";
    public static bool ValidId(string id)=>id is FishingId or CrabId;
    public static bool Matches(string id,string? raw)
    {
        if(!ValidId(id)||!MachineUpgrade.ValidPortable(raw))return false;
        return raw==null || (ProductionState.Load(raw).Kind==Crab)==(id==CrabId);
    }
}
public sealed record CrabBait(int Index,string Id,int Count,bool Deluxe,int Quality=0,string Metadata="");
public sealed record CrabAquatic(string Id,double Weight);
public sealed class CrabBatch
{
    public bool Luremaster {get;set;}
    public bool Book {get;set;}
    public int Rolls {get;set;}
    public int MachineCrits {get;set;}
    public int BookDoubles {get;set;}
    public int BookBonus {get;set;}
    public List<CrabBait> Consumed {get;set;}=new();
    public CrabBatch Copy()=>new(){Luremaster=Luremaster,Book=Book,Rolls=Rolls,MachineCrits=MachineCrits,BookDoubles=BookDoubles,BookBonus=BookBonus,Consumed=Consumed.ToList()};
}

/// <summary>Crab-pot-only production. No rod pool, legendary quota, treasure or challenge-bait dependency.</summary>
public static class CrabRules
{
    public const int Capacity=70, CycleMinutes=MachineCycleRules.DefaultMinutes;
    public static int UpgradeCycles => MachineCycleRules.RequiredCycles(MachineKinds.Crab, 1);
    public static int Rolls(int level)=>level switch{1=>150,2=>350,_=>throw new ArgumentOutOfRangeException(nameof(level))};
    public static int Required(int level,bool luremaster)=>Rolls(level)/(luremaster?2:1);
    public static int Cost(int count,bool trapper)=>count;
    public static (string Id,int Count)[] Craft(bool trapper)=>new[]{("(O)710",10),("(O)334",20),("(O)335",15),("(O)709",10),("(O)388",200)};
    public static (string Id,int Count)[] Upgrade(bool trapper)=>new[]{("(O)335",25),("(O)336",25),("(O)337",10),("(O)787",8),("(O)709",20)};
    public static bool IsUpgradeMaterial(string id)=>Upgrade(false).Any(r=>r.Id==id);
    public static double JunkChance(int level,bool deluxe)=>level==2?.05:deluxe?.15:.25;
    public static int Quality(int level,bool deluxe,double roll)=>level==2?(deluxe?(roll<.60?2:4):1):(deluxe?(roll<.80?1:2):0);
    public static int Multiplier(int level,bool aquatic,bool book,double critRoll,double bookRoll)
        =>(aquatic && critRoll<(level==2?.55:.20)?2:1)*(book && bookRoll<.25?2:1);
    public static IReadOnlyList<UpgradeSpend>? Plan(IEnumerable<UpgradeSlot> inventory,IReadOnlyList<(string Id,int Count)> requirements)
    {
        var slots=inventory.OrderBy(i=>i.Index).ToArray();
        if(slots.Any(i=>i.Index<0||i.Count<=0)||slots.Select(i=>i.Index).Distinct().Count()!=slots.Length)throw new ArgumentException("Invalid inventory.");
        var result=new List<UpgradeSpend>();
        foreach(var (id,count) in requirements)
        {
            int left=count;
            foreach(var item in slots.Where(i=>i.Id==id))
            {int take=Math.Min(left,item.Count);if(take>0)result.Add(new(item.Index,take));left-=take;if(left==0)break;}
            if(left!=0)return null;
        }
        return result;
    }
    public static List<CrabBait>? PlanBait(IEnumerable<CrabBait> slots,int required)
    {
        if(required<=0)throw new ArgumentOutOfRangeException(nameof(required));
        var source=slots.OrderBy(x=>x.Index).ToArray();
        if(source.Any(i=>i.Index<0||i.Count<=0)||source.Select(i=>i.Index).Distinct().Count()!=source.Length)throw new ArgumentException("Invalid bait slots.");
        var result=new List<CrabBait>();
        foreach(var bait in source){int n=Math.Min(required,bait.Count);result.Add(bait with{Count=n});required-=n;if(required==0)return result;}
        return null;
    }
    // Vanilla tries matching trap records in data order, stopping at first success.
    // Preserve that conditional aquatic distribution, normalize away its native junk fallback.
    public static IReadOnlyList<CrabAquatic> NativePool(IEnumerable<KeyValuePair<string,string>> data,ISet<string> habitats)
    {
        var result=new List<CrabAquatic>();double remaining=1;
        foreach(var pair in data)
        {
            if(pair.Key==GiantLobsterRules.Id||pair.Key=="(O)"+GiantLobsterRules.Id||LobsterKingRules.IsFish(pair.Key)||KingCrabRules.IsFish(pair.Key)||MutantLobsterRules.IsItem(pair.Key))continue;
            var f=pair.Value.Split('/');
            if(f.Length<5 || f[1]!="trap" || !f[4].Split(' ',StringSplitOptions.RemoveEmptyEntries).Any(habitats.Contains)
                || !double.TryParse(f[2],NumberStyles.Float,CultureInfo.InvariantCulture,out double p) || !double.IsFinite(p) || p<=0)continue;
            p=Math.Min(1,p);double weight=remaining*p;remaining*=1-p;
            if(weight>0)result.Add(new(pair.Key.StartsWith("(O)")?pair.Key:"(O)"+pair.Key,weight));
        }
        double sum=result.Sum(x=>x.Weight);
        return result.Select(x=>x with{Weight=x.Weight/sum}).ToArray();
    }
    public static ProductionState Begin(ProductionState previous,IReadOnlyList<CrabBait> bait,IReadOnlyList<CrabAquatic> pool,
        long owner,bool luremaster,bool book,long start,int seed,CrabBalanceConfig? config=null,bool ocean=false,bool kingEligible=false,bool kingCrabEnabled=true,bool sewer=false,bool singleOpportunity=false,bool mutantEligible=true)
    {
        config ??= new(); config.Validate();
        int required=singleOpportunity?1:config.Required(previous.Level,luremaster);
        if(previous.Kind!=MachineKinds.Crab||previous.Phase!="idle"||previous.BlocksRemoval||bait.Count==0
            ||bait.Any(b=>b.Count<=0||b.Index<0||string.IsNullOrWhiteSpace(b.Id))||bait.Select(b=>b.Index).Distinct().Count()!=bait.Count
            ||bait.Sum(b=>(long)b.Count)!=required||pool.Count==0||pool.Any(p=>p.Weight<=0||!double.IsFinite(p.Weight)
                ||p.Id==GiantLobsterRules.Id||p.Id=="(O)"+GiantLobsterRules.Id||LobsterKingRules.IsFish(p.Id)||KingCrabRules.IsFish(p.Id)||MutantLobsterRules.IsItem(p.Id))||sewer&&ocean)throw new ArgumentException("Incomplete crab cycle, contradictory habitat or duplicate rare species entry.");
        var s=previous.IdleCopy();s.OwnerId=owner;s.Phase="working";s.StartedAt=start;s.DueAt=CycleClock.DueAt(start,config.MinutesForLevel(previous.Level));
        s.Schema=7;s.CycleRulesVersion=MachineCycleRules.Version;
        s.Seed=seed;s.BaitUsed=required;s.CycleToken=Guid.NewGuid().ToString("N");
        var batch=s.Crab=new(){Luremaster=luremaster,Book=book,Rolls=required*(luremaster&&!singleOpportunity?2:1),Consumed=bait.ToList()};
        var choice=new Random(seed);var quality=new Random(seed^0x2a48511);var critical=new Random(seed^0x114488);var bookRandom=new Random(seed^0x54aa922);
        var species=new Random(seed^0x12740abc); // Same persisted cycle seed; no clock/global RNG or load-time reroll.
        var mutantRandom=sewer?MutantLobsterRules.RandomFor(seed):null; // Leaves every non-Sewer stream unchanged.
        var counts=new Dictionary<(string Id,int Quality),int>();double sum=pool.Sum(f=>f.Weight);bool kingSelected=false;
        foreach(var unit in bait)for(int i=0;i<unit.Count*(luremaster&&!singleOpportunity?2:1);i++)
        {
            bool mutant=sewer&&mutantEligible&&MutantLobsterRules.Select(true,mutantRandom!.NextDouble());
            var rare=ocean?LobsterKingRules.Select(true,kingEligible&&!kingSelected,species.NextDouble(),kingCrabEnabled):RareCrab.Ordinary;
            bool kingCrab=rare==RareCrab.KingCrab;
            bool king=rare==RareCrab.LobsterKing;kingSelected|=king;
            bool giant=rare==RareCrab.GiantLobster;
            bool aquatic=mutant||kingCrab||king||giant||choice.NextDouble()>=JunkChance(s.Level,unit.Deluxe);string id;int q=0;
            if(aquatic)
            {
                s.FishCount++;id=mutant?MutantLobsterRules.Item:kingCrab?KingCrabRules.Fish:king?LobsterKingRules.Fish:"(O)"+GiantLobsterRules.Id;
                if(!mutant&&!kingCrab&&!king&&!giant)
                {
                    double target=choice.NextDouble()*sum;id=pool[^1].Id;
                    foreach(var fish in pool){target-=fish.Weight;if(target<0){id=fish.Id;break;}}
                }
                q=Quality(s.Level,unit.Deluxe,quality.NextDouble());
            }
            else{s.OtherCount++;id="(O)"+choice.Next(168,173).ToString(CultureInfo.InvariantCulture);}
            bool crit=!mutant&&!king&&aquatic&&critical.NextDouble()<(s.Level==2?config.LevelTwoCriticalChance:config.LevelOneCriticalChance);int n=crit?2:1;
            if(crit){batch.MachineCrits++;s.CriticalFishCount++;}
            if(!mutant&&!king&&book&&bookRandom.NextDouble()<.25){batch.BookDoubles++;batch.BookBonus+=n;n*=2;}
            var key=(id,q);counts[key]=counts.GetValueOrDefault(key)+n;
        }
        s.Pending=counts.Select(p=>new StoredCatch(p.Key.Id,p.Value,p.Key.Quality)).ToList();return s;
    }
    public static bool Valid(ProductionState s)
    {
        if(s.Schema is not(6 or 7)||!MachineStateMigration.MetadataValid(s)||s.Kind!=MachineKinds.Crab||s.Level is not(1 or 2)||s.CompletedCycles<0||s.LevelCycles<0||s.LevelCycles>s.CompletedCycles
            ||s.Phase is not("idle" or "working" or "ready")||s.StartedAt<0||s.DueAt<s.StartedAt||s.BaitUsed<0||s.AutoTrash
            ||s.ChallengeExtraFish!=0||s.NormalTreasures!=0||s.AdvancedTreasures!=0||s.TreasureItemCount!=0||s.EmptyCount!=0
            ||s.LegendaryPlans==null||s.LegendaryPlans.Count!=0||s.Pending==null
            ||s.Pending.Where(p=>LobsterKingRules.IsFish(p.Id)).Sum(p=>(long)p.Count)>1
            ||s.Pending.Any(p=>p.WildClaim!=null&&((!LobsterKingRules.IsFish(p.Id)&&!MutantLobsterRules.IsItem(p.Id))||p.Count!=1||!Guid.TryParseExact(p.WildClaim,"N",out _)))
            ||s.Pending.Any(p=>p.Count<=0||string.IsNullOrWhiteSpace(p.Id)||p.Quality is not(0 or 1 or 2 or 4)))return false;
        if(s.Phase=="idle")return s.Pending.Count==0&&s.BaitUsed==0&&s.Crab==null&&s.FishCount==0&&s.OtherCount==0&&s.CriticalFishCount==0;
        var b=s.Crab;
        // Validate the persisted batch, not today's settings: an in-flight cycle is immutable.
        return b!=null&&b.Rolls>=2&&b.Rolls<=100000&&b.Rolls%2==0&&s.BaitUsed*(b.Luremaster?2L:1L)==b.Rolls&&s.DueAt>s.StartedAt&&s.DueAt-s.StartedAt<=40320
            &&Guid.TryParseExact(s.CycleToken,"N",out _)&&s.FishCount>=0&&s.OtherCount>=0&&(long)s.FishCount+s.OtherCount==b.Rolls
            &&s.CriticalFishCount==b.MachineCrits&&b.MachineCrits>=0&&b.MachineCrits<=s.FishCount
            &&b.BookDoubles>=0&&b.BookDoubles<=b.Rolls&&b.BookBonus>=b.BookDoubles&&b.BookBonus<=2*b.BookDoubles
            &&(b.Book||b.BookBonus==0&&b.BookDoubles==0)&&b.Consumed!=null&&b.Consumed.All(x=>x.Count>0&&x.Index>=0&&!string.IsNullOrWhiteSpace(x.Id))
            &&b.Consumed.Select(x=>x.Index).Distinct().Count()==b.Consumed.Count&&b.Consumed.Sum(x=>(long)x.Count)==s.BaitUsed
            &&s.Pending.Sum(x=>(long)x.Count)<=b.Rolls+b.MachineCrits+b.BookBonus;
    }
}

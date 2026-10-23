using System.Text.Json;

namespace Eddie.FishingMachine.Core;

public sealed record BaitSlot(int Index, int Count, BaitKind Kind, string? Target = null);
public sealed record BaitSpend(int Index, int Count, BaitKind Kind, string? Target);
public sealed record FishChoice(string Id, double Weight, bool IsLegendary = false, int BasePrice = 0);
public sealed record LegendaryCatchPlan(string Id,int Count,int Quality,string FallbackId);
public sealed record StoredCatch(string Id, int Count, int Quality = 0, string? WildClaim = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => (Quality == 0 ? Id : Id + "#quality=" + Quality) + (WildClaim==null?"":"#wild="+WildClaim);
}

/// <summary>Legacy batches contain fixed output; schema 8 fixes a paid opportunity plan and persists each elapsed outcome.</summary>
public sealed class ProductionState
{
    public LegendaryCycleStats LegendaryStats {get;set;}=new();
    public int FishingBalanceVersion { get; set; }
    public int Schema { get; set; } = 6;
    public int CycleRulesVersion { get; set; }
    public bool LegacyPrecomputed { get; set; }
    public int? OriginalCycleMinutes { get; set; }
    public MachineTimeline? Timeline {get;set;}
    public MachineContinuity? Continuity {get;set;}
    public string Kind { get; set; } = MachineKinds.Fishing;
    public string PersistentId { get; set; } = "";
    public CrabBatch? Crab { get; set; }
    public int Level { get; set; } = 1;
    public long CompletedCycles { get; set; }
    public long LevelCycles { get; set; }
    public bool StageProgressUnverified { get; set; }
    public long OwnerId { get; set; }
    public int LegendaryYear { get; set; }
    public string CycleToken { get; set; } = "";
    public List<LegendaryCatchPlan> LegendaryPlans { get; set; } = new();
    public bool AutoTrash { get; set; }
    public int ChallengeExtraFish { get; set; }
    public int? LastNormalTreasures {get;set;}
    public int? LastAdvancedTreasures {get;set;}
    public int NormalTreasures { get; set; }
    public int AdvancedTreasures { get; set; }
    public int TreasureItemCount { get; set; }
    public string Phase { get; set; } = "idle";
    public long StartedAt { get; set; }
    public long DueAt { get; set; }
    public int BaitUsed { get; set; }
    /// <summary>Bait a full cycle of this level takes. 0 = legacy full cycle. A cycle may start with less
    /// (every bait is one timed cast); partial cycles add their bait to BaitCredit and count as a completed
    /// cycle for upgrades only once a full cycle's worth of bait has been fished.</summary>
    public int CycleBait { get; set; }
    public long BaitCredit { get; set; }
    public int Seed { get; set; }
    public int FishCount { get; set; }
    public int CriticalFishCount { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int ProducedFishCount => FishCount + ChallengeExtraFish + CriticalFishCount;
    public int OtherCount { get; set; }
    public int EmptyCount { get; set; }
    public List<StoredCatch> Pending { get; set; } = new();

    public string Save() => JsonSerializer.Serialize(this);
    public ProductionState Snapshot()
    {
        var copy = (ProductionState)MemberwiseClone();
        copy.Pending = Pending.ToList();copy.LegendaryStats=LegendaryStats.Copy();
        copy.LegendaryPlans=LegendaryPlans.ToList();
        copy.Crab=Crab?.Copy();
        copy.Timeline=Timeline?.Copy();
        copy.Continuity=Continuity?.Copy();
        return copy;
    }
    public static ProductionState Load(string value)
    {
        var s = JsonSerializer.Deserialize<ProductionState>(value) ?? throw new ArgumentException("Missing cycle data.");
        if(s.LastNormalTreasures<0||s.LastAdvancedTreasures<0||s.LastNormalTreasures.HasValue!=s.LastAdvancedTreasures.HasValue)throw new ArgumentException("Invalid completed treasure summary.");
        if(s.LegendaryStats==null||!s.LegendaryStats.Valid())throw new ArgumentException("Invalid legendary cycle statistics; preserved.");
        using var document=JsonDocument.Parse(value);
        if(s.Level>1&&!document.RootElement.TryGetProperty(nameof(LevelCycles),out _))s.StageProgressUnverified=true;
        if(s.Continuity!=null&&document.RootElement.TryGetProperty("Continuity",out var continuityJson)
            &&continuityJson.TryGetProperty("Policies",out var policiesJson)&&policiesJson.ValueKind==JsonValueKind.Array)
        {
            int i=0;foreach(var policy in policiesJson.EnumerateArray())
            {
                if(policy.TryGetProperty("Fish",out var fishJson)&&!fishJson.TryGetProperty("FishingBalanceVersion",out _))
                    s.Continuity.Policies[i].Fish.FishingBalanceVersion=0;
                i++;
            }
        }
        if(s.Continuity!=null&&!s.Continuity.Valid())throw new ArgumentException("Invalid continuity evidence; paid state preserved.");
        if(s.Timeline?.FishingConfig is {} captured && document.RootElement.TryGetProperty("Timeline",out var timelineJson)
            && timelineJson.TryGetProperty("FishingConfig",out var configJson) && !configJson.TryGetProperty("FishingBalanceVersion",out _))
            captured.FishingBalanceVersion=0;
        if(s.Schema==8){if(!MachineTimeline.Valid(s))throw new ArgumentException("Invalid timeline; paid state preserved.");return s;}
        if(s.Timeline!=null)throw new ArgumentException("Timeline in legacy schema.");
        if(s.Kind==MachineKinds.Crab){if(!CrabRules.Valid(s))throw new ArgumentException("Invalid crab cycle; preserved.");return s;}
        if (s.Kind!=MachineKinds.Fishing || s.FishingBalanceVersion is <0 or >1 || s.Schema is not (3 or 4 or 5 or 6 or 7) || !MachineStateMigration.MetadataValid(s) || s.Level is not (1 or 2 or 3) || s.CompletedCycles < 0 || s.LevelCycles < 0 || s.LevelCycles > s.CompletedCycles
            || s.ChallengeExtraFish < 0 || s.ChallengeExtraFish > 2L*s.FishCount
            || s.NormalTreasures < 0 || s.AdvancedTreasures < 0 || s.TreasureItemCount < 0
            || (long)s.NormalTreasures+s.AdvancedTreasures > s.FishCount || s.Phase is not ("idle" or "working" or "ready")
            || s.StartedAt < 0 || s.DueAt < s.StartedAt || s.BaitUsed < 0
            || s.FishCount < 0 || s.OtherCount < 0 || s.EmptyCount < 0
            || s.CriticalFishCount<0 || s.CriticalFishCount>(s.Level==3||s.Level==2&&s.FishingBalanceVersion==1?2L:1L)*(s.FishCount+s.ChallengeExtraFish) || s.Schema==3 && s.CriticalFishCount!=0
            || s.Pending is null || s.Pending.Any(p => p.Count <= 0 || string.IsNullOrWhiteSpace(p.Id) || p.Quality is not (0 or 1 or 2 or 4))
            || s.LegendaryPlans is null || s.LegendaryPlans.Count>6
            || s.LegendaryPlans.Select(p=>p.Id).Distinct().Count()!=s.LegendaryPlans.Count
            || s.LegendaryPlans.Count>0 && ((s.Level<2||s.Level==2&&s.FishingBalanceVersion==0) && s.LegendaryPlans.Any(p=>p.Id!=Production.SpiritId) || s.Phase!="working")
            || s.LegendaryPlans.Any(p=>p.Count<1||p.Count>9||(p.Id==Production.SpiritId ? p.Quality is not (0 or 1 or 2 or 4) || p.Count>3 : s.Level==2&&s.FishingBalanceVersion==1 ? p.Quality is not (1 or 2 or 4) : p.Quality is not (2 or 4))
                || p.Id is not ("(O)159" or "(O)160" or "(O)163" or "(O)682" or "(O)775" or "(O)Eddie.BetterStardewValley_SpiritFish")
                || string.IsNullOrWhiteSpace(p.FallbackId) || s.LegendaryPlans.Any(k=>k.Id==p.FallbackId)
                || s.Pending.Where(c=>c.Id==p.Id && c.Quality==p.Quality).Sum(c=>(long)c.Count)!=p.Count)
            || (s.Phase == "working" && (s.BaitUsed == 0 || s.DueAt <= s.StartedAt))
            || (long)s.FishCount + s.OtherCount + s.EmptyCount != s.BaitUsed
            || s.Pending.Sum(p => (long)p.Count) > (long)s.FishCount + s.ChallengeExtraFish + s.CriticalFishCount + s.OtherCount + s.TreasureItemCount
            || (s.Phase == "idle" && (s.BaitUsed != 0 || s.Pending.Count > 0)))
            throw new ArgumentException("Invalid or unsupported saved cycle. Preserved without alteration.");
        return s;
    }
    public bool Complete(long now)
    {
        if (Phase != "working" || !CycleClock.IsComplete(now, DueAt) || Timeline!=null&&Timeline.Cursor!=Timeline.Plan.Count) return false;
        Phase = "ready";LastNormalTreasures=NormalTreasures;LastAdvancedTreasures=AdvancedTreasures;
        Schema = Timeline!=null ? 8 : CycleRulesVersion > 0 ? 7 : 6;
        if(CycleBait>0&&BaitUsed<CycleBait)
        {
            BaitCredit=checked(BaitCredit+BaitUsed);
            if(BaitCredit<CycleBait)return true;
            BaitCredit-=CycleBait;
        }
        CompletedCycles = checked(CompletedCycles + 1);
        LevelCycles=checked(LevelCycles+1);
        // Missing historical stage evidence never grants lifetime credit. Newly observed
        // completions are reliable and can establish eligibility without a permanent lock.
        if(StageProgressUnverified&&Level==2&&Kind==MachineKinds.Fishing&&LevelCycles>=MachineCycleRules.RequiredCycles(this))StageProgressUnverified=false;
        return true;
    }
    public ProductionState IdleCopy() => new() { LegendaryStats=LegendaryStats.Copy(), LastNormalTreasures=LastNormalTreasures, LastAdvancedTreasures=LastAdvancedTreasures, Schema=CycleRulesVersion>0?7:6, CycleRulesVersion=CycleRulesVersion, Kind=Kind, PersistentId=PersistentId, OwnerId=OwnerId, Level=Level, CompletedCycles=CompletedCycles, LevelCycles=LevelCycles, StageProgressUnverified=StageProgressUnverified, AutoTrash=AutoTrash, Seed=Seed, BaitCredit=BaitCredit, Continuity=Continuity?.Copy() };
    public bool BlocksRemoval => Phase == "working" || Pending.Count > 0;
}

public static class Production
{
    public const string SpiritId="(O)Eddie.BetterStardewValley_SpiritFish";
    public static IReadOnlyList<BaitSpend>? PlanBait(IEnumerable<BaitSlot> slots, int count, ISet<string> eligible)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        var input = slots.ToArray();
        if (input.Select(s => s.Index).Distinct().Count() != input.Length) throw new ArgumentException("Duplicate inventory slot.");
        var plan = new List<BaitSpend>();
        foreach (var s in input.OrderBy(s => s.Index))
        {
            if (s.Index < 0 || s.Count <= 0 || !Enum.IsDefined(typeof(BaitKind), s.Kind)) continue;
            if (s.Kind == BaitKind.Targeted && s.Target == null) continue;
            int take = Math.Min(count, s.Count);
            plan.Add(new(s.Index, take, s.Kind, s.Target));
            count -= take;
            if (count == 0) return plan;
        }
        return null;
    }

    public static ProductionState Begin(BalanceConfig config, IReadOnlyList<BaitSpend> bait,
        IReadOnlyList<FishChoice> fish, IReadOnlyList<string> other, long start, int seed, ProductionState? previous = null, string? jelly = null, bool singleOpportunity=false)
    {
        config.Validate();
        previous ??= new();
        fish=fish.Where(f=>!LobsterKingRules.IsFish(f.Id)&&!MutantLobsterRules.IsItem(f.Id)).ToArray();
        int originalOtherCount=other.Count;
        if(previous.Level>=2)other=other.Where(id=>!MachineUpgrade.IsTrash(id)).ToArray();
        double jellyShare=previous.Level>=2&&jelly!=null?.10:0;
        double retainedOther=previous.Level>=2?jellyShare+(1-jellyShare)*(originalOtherCount==0?0:(double)other.Count/originalOtherCount):1;
        double conditionalJelly=retainedOther>0?jellyShare/retainedOther:0;
        int required = singleOpportunity?1:MachineUpgrade.RequiredBait(previous, config);
        if (previous.BlocksRemoval || previous.Phase != "idle") throw new ArgumentException("Machine is not idle.");
        if (bait.Any(p => p.Count <= 0) || bait.Sum(p => (long)p.Count) != required
            || bait.Select(p => p.Index).Distinct().Count() != bait.Count || fish.Count == 0 && other.Count == 0 && jelly==null
            || fish.Any(p => p.Weight <= 0 || !double.IsFinite(p.Weight)||MutantLobsterRules.IsItem(p.Id))
            || other.Any(id=>MutantLobsterRules.IsItem(id)||LobsterKingRules.IsFish(id))||MutantLobsterRules.IsItem(jelly)||LobsterKingRules.IsFish(jelly)
            || bait.Any(p => p.Kind == BaitKind.Challenge && previous.Level < 2)
            || !singleOpportunity && bait.Any(p => p.Kind == BaitKind.Targeted && !fish.Any(f => f.Id == p.Target)))
            throw new ArgumentException("Incomplete or invalid batch plan.");
        var state = new ProductionState { FishingBalanceVersion=config.FishingBalanceVersion, Schema=7, CycleRulesVersion=MachineCycleRules.Version, PersistentId=previous.PersistentId, OwnerId=previous.OwnerId, Phase = "working", StartedAt = start,
            DueAt = CycleClock.DueAt(start, config.MinutesForLevel(previous.Level)), BaitUsed = required, Seed = seed,
            Level=previous.Level, LevelCycles=previous.LevelCycles, StageProgressUnverified=previous.StageProgressUnverified, CompletedCycles=previous.CompletedCycles, AutoTrash=previous.AutoTrash };
        if(config.FishingBalanceVersion>0)fish=fish.Where(f=>!f.IsLegendary||f.Id==SpiritId).ToArray();
        var ordinary=fish.Where(f=>!f.IsLegendary).ToArray();
        if(ordinary.Length==0&&other.Count==0&&jelly==null)throw new ArgumentException("No legal non-trash output; paid opportunity retained.");
        var selectedLegends=new HashSet<string>();
        bool spiritEligible=fish.Any(f=>f.Id==SpiritId);
        var spiritRandom=new Random(unchecked(seed ^ 0x671ACA21));
        var fallbackRandom=new Random(unchecked(seed ^ 0x3575AFF));
        var random = new Random(seed);
        var criticalRandom = FishingCritical.RandomFor(seed);
        var counts = new Dictionary<(string Id, int Quality), int>();
        var qualityRandom = new Random(unchecked(seed ^ 0x19572431));
        var treasureRandom = new Random(unchecked(seed ^ 0x52AB541F));
        var jellyRandom = new Random(unchecked(seed ^ 0x342101AE));
        void Add(string id, int count, int quality=0)
        { var key=(id,quality); counts[key]=counts.GetValueOrDefault(key)+count; }
        foreach (var s in bait)
        {
            // The 30% non-target share excludes the targeted species where alternatives exist.
            var choices = s.Kind == BaitKind.Targeted ? fish.Where(f => f.Id != s.Target && f.Id != SpiritId).ToArray() : fish.Where(f=>f.Id != SpiritId).ToArray();
            if (choices.Length == 0) choices = ordinary;
            if(config.FishingBalanceVersion>0&&s.Kind==BaitKind.GiantLobster)
                choices=choices.Select(f=>f with{Weight=f.Weight*GiantLobsterRules.ValueWeight(f.BasePrice)}).ToArray();
            var rates=config.GetRates(s.Kind,state.Level);
            if(state.Level>=2)rates=new(){Fish=rates.Fish,OtherItem=rates.OtherItem*retainedOther,TargetShareOfFish=rates.TargetShareOfFish};
            if(ordinary.Length==0)rates=new(){Fish=0,OtherItem=1};
            else if(other.Count==0&&jelly==null)rates=new(){Fish=1,OtherItem=0,TargetShareOfFish=rates.TargetShareOfFish};
            for (int n = 0; n < s.Count; n++)
            {
                var outcome = CatchRoller.Decide(s.Kind, rates, random.NextDouble(), random.NextDouble());
                if(config.FishingBalanceVersion>0||previous.Level>=2)
                {
                    if(outcome==CatchOutcome.OtherItem&&other.Count==0&&jelly==null)outcome=CatchOutcome.OrdinaryFish;
                    if((outcome is CatchOutcome.OrdinaryFish or CatchOutcome.TargetFish)&&ordinary.Length==0)outcome=CatchOutcome.OtherItem;
                    if(outcome==CatchOutcome.TargetFish&&!fish.Any(f=>f.Id==s.Target))outcome=CatchOutcome.OrdinaryFish;
                }
                string? id = null;
                switch (outcome)
                {
                    case CatchOutcome.TargetFish:
                        if(singleOpportunity&&!fish.Any(f=>f.Id==s.Target)){state.EmptyCount++;break;}
                        id = s.Target == SpiritId ? Pick(ordinary,random.NextDouble()) : s.Target; state.FishCount++; break;
                    case CatchOutcome.OrdinaryFish: id = Pick(choices, random.NextDouble()); state.FishCount++; break;
                    case CatchOutcome.OtherItem:
                        id = state.Level >= 2 && jelly != null && (other.Count==0||jellyRandom.NextDouble() < conditionalJelly)
                            ? jelly : other[random.Next(other.Count)];
                        state.OtherCount++; break;
                    default: state.EmptyCount++; break;
                }
                if (id != null)
                {
                    bool isFish = outcome is CatchOutcome.OrdinaryFish or CatchOutcome.TargetFish;
                    if(isFish && spiritEligible && !selectedLegends.Contains(SpiritId) && s.Kind==BaitKind.GiantLobster
                        && GiantLobsterRules.SelectSpirit(true,true,s.Kind,spiritRandom.NextDouble()))id=SpiritId;
                    if(isFish && fish.Any(f=>f.Id==id && f.IsLegendary) && !selectedLegends.Add(id))
                        id=Pick(ordinary,fallbackRandom.NextDouble());
                    int baseCount = isFish && s.Kind == BaitKind.Challenge ? 3 : 1;
                    int bonus = isFish ? baseCount * MachineLevelRules.ExtraMultiplier(config,state.Level,criticalRandom.NextDouble()) : 0;
                    if(id==SpiritId){bonus=0;baseCount=1;} // exactly one Spirit Fish per catch
                    if (isFish) state.ChallengeExtraFish += baseCount - 1;
                    state.CriticalFishCount += bonus;
                    int quality = isFish ? MachineLevelRules.Quality(state.Level,qualityRandom.NextDouble(),config.FishingBalanceVersion,s.Kind) : 0;
                    Add(id, baseCount + bonus, quality);
                    if(isFish && fish.Any(f=>f.Id==id && f.IsLegendary))
                        state.LegendaryPlans.Add(new(id,baseCount+bonus,quality,Pick(ordinary,fallbackRandom.NextDouble())));
                    if (isFish && (state.Level >= 2 || config.FishingBalanceVersion>0))
                    {
                        int treasure = MachineLevelRules.Treasure(state.Level,treasureRandom.NextDouble(),config.FishingBalanceVersion);
                        if (treasure != 0)
                        {
                            if (treasure == 1) state.NormalTreasures++; else state.AdvancedTreasures++;
                            var reward = LevelTwoCatch.Reward(treasure, treasureRandom.NextDouble());
                            Add(reward.Id, reward.Count); state.TreasureItemCount += reward.Count;
                        }
                    }
                }
            }
        }
        state.Pending = counts.Select(p => new StoredCatch(p.Key.Id, p.Value, p.Key.Quality)).ToList();
        return state;
    }

    private static string Pick(IReadOnlyList<FishChoice> choices, double roll)
    {
        double target = roll * choices.Sum(f => f.Weight);
        foreach (var f in choices) { target -= f.Weight; if (target < 0) return f.Id; }
        return choices[^1].Id;
    }
}

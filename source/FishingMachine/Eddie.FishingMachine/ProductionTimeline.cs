using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Objects;
[assembly:System.Runtime.CompilerServices.InternalsVisibleTo("TimelineNativeChecks")]
namespace Eddie.FishingMachine;
internal sealed partial class ProductionService
{
    /// <summary>Bounded, same machine mutex/transaction as ordinary production. Nothing is delivered before ready.</summary>
    private void AdvanceTimeline(Chest c)
    {
        for(int work=0;work<1;work++)
        {
            var original=Read(c);var t=original.Timeline;
            if(t==null||original.Phase!="working"||t.Cursor>=t.Plan.Count)return;
            var op=t.Plan[t.Cursor];if(op.At>=Now)return;
            try
            {
                TimelineEnvironment.Ensure(c,original);
                var env=TimelineEnvironment.At(original.CycleToken,op.At);
                if(env.Error!=null)throw new InvalidOperationException(env.Error);
                if(env.Location!=c.Location.NameOrUniqueName||env.X!=(int)c.TileLocation.X||env.Y!=(int)c.TileLocation.Y)
                    throw new InvalidOperationException("Paid machine moved; historical geometry does not match.");
                var owner=Game1.GetPlayer(original.OwnerId,onlyOnline:false)??throw new InvalidOperationException("Machine owner unavailable.");
                int day=MachineTimeline.Day(op.At),seed=MachineTimeline.SeedFor(original.Seed,op.Ordinal,0x38211);
                var next=original.Snapshot();ProductionState unit;List<FishChoice> legendaryCandidates=new();
                if(t.Prepared!=null)unit=t.Prepared.Snapshot();
                else if(original.Kind==MachineKinds.Crab)
                {
                    var bait=t.CrabBait[op.Bait] with{Count=1};
                    unit=CrabRules.Begin(original.IdleCopy(),new[]{bait},env.Crab,original.OwnerId,original.Crab!.Luremaster,original.Crab.Book,
                        op.At,seed,t.CrabConfig,env.Ocean,env.Ocean && !env.Sewer && BetterStardewValley.LobsterKing.KingWildQuota.AvailableAt(day),env.KingCrabEnabled,env.Sewer,true,env.Sewer&&BetterStardewValley.MutantLobsterKing.MutantWildQuota.AvailableAt(day));

                }
                else
                {
                    int time=MachineTimeline.Time(op.At),year=MachineTimeline.Year(day);
                    var bait=t.FishingBait[op.Bait] with{Count=1};
                    bool rules2=(t.FishingConfig?.FishingBalanceVersion??0)>=2;
                    // Magic bait / giant lobster bait: this water's fish at any season, time and weather.
                    bool magic=rules2&&BaitRules.IgnoresSeasonTimeWeather(bait.Kind);
                    var fish=(magic&&env.MagicFish is {Count:>0} magicPool?magicPool.ToList():null)??env.Fish.FirstOrDefault(p=>time>=p.From&&time<p.Until)?.Fish??new();
                    if(!rules2)fish=fish.Where(f=>!MachineLegendaryBridge.IsBoss(f.Id)).ToList();   // cycles paid before 2.0.14 never get boss fish
                    var diag=CycleDiag.GetValueOrDefault(original.CycleToken);
                    CycleDiag[original.CycleToken]=(diag.Casts+1,diag.NoFish+(fish.Any(f=>!f.IsLegendary)?0:1),diag.Unwatched+(env.UnknownWeather?1:0));
                    fish=fish.Where(f=>!f.IsLegendary||MachineLegendaryBridge.AvailableTimeline(owner,f.Id,year,TimelineEnvironment.ObservedQuota(original.CycleToken,day,f.Id))).ToList();
                    if(t.Version>=2)legendaryCandidates=fish.Where(f=>f.IsLegendary&&f.Id!=Production.SpiritId).ToList();
                    if(t.Version>=2)fish=fish.Where(f=>!f.IsLegendary||f.Id==Production.SpiritId).ToList();
                    var other=env.Other;
                    if(!fish.Any(f=>!f.IsLegendary)&&!other.Any(id=>original.Level<2||!MachineUpgrade.IsTrash(id))&&env.Jelly==null)
                        throw new InvalidOperationException("No legal non-trash output; paid opportunity retained.");
                    unit=Production.Begin(t.FishingConfig!,new[]{bait},fish,other,op.At,seed,original.IdleCopy(),env.Jelly,true);
                }
                if(original.Kind==MachineKinds.Fishing&&unit.Pending.Any(p=>LobsterKingRules.IsFish(p.Id)||MutantLobsterRules.IsItem(p.Id)))
                    throw new InvalidOperationException("Illegal rod-channel rare output; original paid state retained.");
                // A prepared single outcome owns no scheduling/fuel history of its own.
                unit.Continuity=null;
                if(t.Prepared==null)
                {
                    var prepared=original.Snapshot();prepared.Timeline!.Prepared=unit.Snapshot();
                    Commit(c,null,prepared);original=Read(c);next=original.Snapshot();
                }
                if(original.Kind==MachineKinds.Crab&&unit.Pending.Any(p=>LobsterKingRules.IsFish(p.Id)||MutantLobsterRules.IsItem(p.Id)))
                {
                    bool mutant=unit.Pending.Any(p=>MutantLobsterRules.IsItem(p.Id));
                    string rareId=mutant?MutantLobsterRules.Item:LobsterKingRules.Fish;
                    void Apply(string token)
                    {
                        unit.Pending=unit.Pending.Select(p=>p.Id==rareId?p with{WildClaim=token,Count=1}:p).ToList();
                        next.Timeline!.Claims.Add(new(day,rareId,token));
                        MachineTimeline.Merge(next,unit);foreach(var output in unit.Pending)Resolve(output);Commit(c,null,next);
                    }
                    string source=BetterStardewValley.LobsterKing.KingWildQuota.Source(c,"machine")+":"+original.CycleToken+":"+op.Ordinal;
                    bool legal=mutant?env.Sewer:env.Ocean&&!env.Sewer&&MachineTimeline.Season(day)!=3;
                    bool committed=legal&&(mutant
                        ?BetterStardewValley.MutantLobsterKing.MutantWildQuota.CommitAt(day,source,Apply,()=>Commit(c,null,original))
                        :BetterStardewValley.LobsterKing.KingWildQuota.CommitAt(day,source,Apply,()=>Commit(c,null,original)));
                    if(committed)continue;
                    // The same seed and paid bait, with the exhausted rare interval mapped to ordinary.
                    // No new rare draw, no waiting for a future year's/season's quota.
                    unit=CrabRules.Begin(original.IdleCopy(),new[]{t.CrabBait[op.Bait] with{Count=1}},env.Crab,original.OwnerId,
                        original.Crab!.Luremaster,original.Crab.Book,op.At,seed,t.CrabConfig,env.Ocean,false,env.KingCrabEnabled,env.Sewer,true,false);
                    unit.Continuity=null;
                }
                if(original.Kind==MachineKinds.Fishing&&unit.LegendaryPlans.Count>0)
                {
                    foreach(var plan in unit.LegendaryPlans.ToArray())
                    {
                        bool legal=MachineLegendaryBridge.AvailableTimeline(owner,plan.Id,MachineTimeline.Year(day),true);
                        if(plan.Id==Production.SpiritId)legal&=t.FishingBait[op.Bait].Kind==BaitKind.GiantLobster
                            &&BetterStardewValley.SpiritFish.SpiritRules.Eligible(owner.fishingLevel.Value,MachineTimeline.Time(op.At),env.Location);
                        if(legal)continue;
                        var old=unit.Pending.Single(p=>p.Id==plan.Id&&p.Quality==plan.Quality);
                        unit.Pending.Remove(old);unit.Pending.Add(new(plan.FallbackId,old.Count,old.Quality));unit.LegendaryPlans.Remove(plan);
                    }
                }
                if(original.Kind==MachineKinds.Fishing&&t.Version>=2&&unit.FishCount==1&&unit.LegendaryPlans.Count==0)
                {
                    if(legendaryCandidates.Count==0)
                    {
                        int time=MachineTimeline.Time(op.At),year=MachineTimeline.Year(day);
                        bool rules2=(t.FishingConfig?.FishingBalanceVersion??0)>=2;
                        bool magic=rules2&&BaitRules.IgnoresSeasonTimeWeather(t.FishingBait[op.Bait].Kind);
                        legendaryCandidates=((magic&&env.MagicFish is {Count:>0} magicPool2?magicPool2.ToList():null)??env.Fish.FirstOrDefault(p=>time>=p.From&&time<p.Until)?.Fish??new())
                            .Where(f=>f.IsLegendary&&f.Id!=Production.SpiritId&&(rules2||!MachineLegendaryBridge.IsBoss(f.Id))&&MachineLegendaryBridge.AvailableTimeline(owner,f.Id,year,TimelineEnvironment.ObservedQuota(original.CycleToken,day,f.Id))).ToList();
                    }
                    // Treasury shares may have the same ID; original fish quantity comes from counters.
                    var catchItem=unit.Pending.FirstOrDefault(p=>env.Fish.Any(w=>w.Fish.Any(f=>!f.IsLegendary&&f.Id==p.Id))||env.MagicFish?.Any(f=>!f.IsLegendary&&f.Id==p.Id)==true);
                    if(catchItem!=null&&legendaryCandidates.Count>0)
                        next.Timeline!.LegendaryOpportunities.Add(new(day,op.Ordinal,catchItem.Id,unit.ProducedFishCount,catchItem.Quality,legendaryCandidates));
                }
                MachineTimeline.Merge(next,unit);foreach(var output in unit.Pending)Resolve(output);
                MachineLegendaryBridge.CommitTimeline(owner,day,unit,next,()=>Commit(c,null,next));
            }
            catch(Exception ex)
            {
                var paused=Read(c).Snapshot();
                if(paused.Timeline!.PausedReason!=ex.Message)
                {paused.Timeline.PausedReason=ex.Message;Commit(c,null,paused);Mod.Monitor.Log("Machine timeline paused; paid opportunity retained: "+ex.Message,StardewModdingAPI.LogLevel.Warn);}
                return;
            }
        }
    }
    /// <summary>Per running cycle: casts made, casts with no fish available at that time/place, casts in unwatched time.</summary>
    private static readonly Dictionary<string,(int Casts,int NoFish,int Unwatched)> CycleDiag=new();
    private static void ClearCycleDiagnostics()=>CycleDiag.Clear();
    private void LogCycle(Chest c,ProductionState s)
    {
        var d=CycleDiag.GetValueOrDefault(s.CycleToken);CycleDiag.Remove(s.CycleToken);
        Mod.Monitor.Log($"[BSV fishing machine] {c.Location?.NameOrUniqueName}:{c.TileLocation} Lv{s.Level} cycle done: {s.BaitUsed} bait -> fish {s.FishCount} (+{s.CriticalFishCount+s.ChallengeExtraFish} bonus), other {s.OtherCount}, treasure {s.NormalTreasures}/{s.AdvancedTreasures}; "
            +$"casts checked this session {d.Casts}, with no fish available {d.NoFish}, in unwatched time {d.Unwatched}; upgrade progress {MachineUpgrade.Progress(s)}/{MachineCycleRules.RequiredCycles(s)} (bait credit {s.BaitCredit}/{s.CycleBait}).",StardewModdingAPI.LogLevel.Info);
    }
    private void SettleCycleLegendary(Chest c,ProductionState state)
    {
        var t=state.Timeline;
        if(t==null||t.Version<2||state.Kind!=MachineKinds.Fishing||t.Cursor!=t.Plan.Count||Now<state.DueAt)return;
        var owner=Game1.GetPlayer(state.OwnerId,onlyOnline:false)??throw new InvalidOperationException("Machine owner unavailable.");
        if(state.Level>=2&&t.LegendaryOpportunities.Count>0&&MachineLegendaryBridge.ManualCatchPending(state.OwnerId))throw new InvalidOperationException("Waiting for the owner's in-flight manual catch before settling legendary quota.");
        SettleChannel(c,state,owner,boss:false);
        // Balance version 2: boss fish have their own roll and quota, after the legendary one (never on the same catch).
        if((t.FishingConfig?.FishingBalanceVersion??0)>=2)SettleChannel(c,state,owner,boss:true);
    }
    private void SettleChannel(Chest c,ProductionState state,Farmer owner,bool boss)
    {
        var t=state.Timeline!;
        var d=boss?t.BossDecision:t.LegendaryDecision;
        if(d==null||!d.Evaluated)
        {
            var candidates=t.LegendaryOpportunities.Select(o=>o with{Species=o.Species.Where(f=>CycleLegendaryRules.IsBoss(f.Id)==boss
                    &&MachineLegendaryBridge.AvailableTimeline(owner,f.Id,MachineTimeline.Year(o.Day),true)).ToList()})
                .Where(o=>o.Species.Count>0&&!(boss&&t.LegendaryDecision.Hit&&o.Ordinal==t.LegendaryDecision.Ordinal)).ToList();
            d=CycleLegendaryRules.Decide(state.Level,state.Seed,candidates,boss?CycleLegendaryRules.BossSalt:CycleLegendaryRules.LegendarySalt);
            if(boss)t.BossDecision=d;else t.LegendaryDecision=d;
            Commit(c,null,state); // fixed result survives crash/reload before quota reservation
        }
        void Record()
        {
            if(d.StatsRecorded)return;
            if(!boss)
            {
                var stats=state.LegendaryStats;stats.Total=checked(stats.Total+1);
                if(d.EligibleOpportunities>0)stats.Eligible=checked(stats.Eligible+1);
                if(d.Hit)stats.Hits=checked(stats.Hits+1);
                if(d.CompetitionFallback)stats.CompetitionFallbacks=checked(stats.CompetitionFallbacks+1);
                if(d.Committed)stats.Submitted=checked(stats.Submitted+1);
            }
            d.StatsRecorded=true;Commit(c,null,state);
        }
        if(!d.Hit||d.Committed||d.CompetitionFallback){Record();return;}
        var selected=t.LegendaryOpportunities.Single(o=>o.Ordinal==d.Ordinal);
        if(!MachineLegendaryBridge.AvailableTimeline(owner,d.Species!,MachineTimeline.Year(selected.Day),true))
        {d.CompetitionFallback=true;Record();return;}
        var before=state.Snapshot();
        var entry=state.Pending.FindIndex(p=>p.Id==selected.OrdinaryId&&p.Quality==selected.Quality&&p.Count>=selected.Count);
        if(entry<0)throw new InvalidOperationException("Legendary replacement source missing; paid result retained.");
        var old=state.Pending[entry];state.Pending.RemoveAt(entry);
        if(old.Count>selected.Count)state.Pending.Add(old with{Count=old.Count-selected.Count});
        state.Pending.Add(new(d.Species!,selected.Count,selected.Quality));
        var unit=new ProductionState{LegendaryPlans=new(){new(d.Species!,selected.Count,selected.Quality,selected.OrdinaryId)}};
        d.Committed=true;
        try{MachineLegendaryBridge.CommitTimeline(owner,selected.Day,unit,state,()=>Commit(c,null,state));}
        catch{Commit(c,null,before);throw;}
        Record();
    }

}

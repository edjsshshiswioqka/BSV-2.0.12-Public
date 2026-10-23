using System.Text.Json;
using Eddie.FishingMachine.Core;

internal static class TimelineChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        const int seed=149202610;
        long start=CycleClock.ToAbsoluteMinutes(111,600),due=start+2880;
        var plan=MachineTimeline.Schedule(start,due,seed,Enumerable.Range(0,100000).Select(i=>i%2).ToArray());
        check(plan.Count==100000&&plan.All(p=>p.At>=start&&p.At<due&&MachineTimeline.Time(p.At) is >=600 and <2600),"100000 actual planned opportunities exclude 02:00..06:00 but include sleeping night");
        check(plan.SequenceEqual(MachineTimeline.Schedule(start,due,seed,Enumerable.Range(0,100000).Select(i=>i%2).ToArray())),"time/ordinal/bait schedule deterministic across reload");
        int night=plan.Count(p=>MachineTimeline.Time(p.At)>=1800),previousYear=plan.Count(p=>MachineTimeline.Year(MachineTimeline.Day(p.At))==1);
        check(Math.Abs(night-40000)<6*Math.Sqrt(100000*.4*.6)&&Math.Abs(previousYear-50000)<6*Math.Sqrt(25000),"100000 opportunities: night 40%, each legal day 50%, within prespecified six sigma");
        Console.WriteLine(JsonSerializer.Serialize(new{timeline_schedule=new{seed,trials=plan.Count,night,previousYear,nightExpected=.4,dayExpected=.5}}));
        check(MachineTimeline.Day(111L*1440+1559)==111&&MachineTimeline.Time(111L*1440+1559)==2559&&MachineTimeline.Year(111)==1&&MachineTimeline.Year(112)==2,"late 25:59 retains old-year quota, next 06:00 opens new-year key");
        check(MachineTimeline.Season(27)==0&&MachineTimeline.Season(28)==1&&MachineTimeline.Season(111)==3&&MachineTimeline.Season(112)==0,"season/year keys use opportunity date, not completion date");
        var cfg=new BalanceConfig{BaitPerCycle=200,Normal=new(){Fish=1},FishCriticalChance=0};
        var state=MachineTimeline.StartFish(new(),cfg,new[]{new BaitSpend(0,200,BaitKind.Normal,null)},35,start,seed);
        // Partial loads: any bait starts a cycle; each bait is one cast inside legal fishing hours.
        foreach(int n in new[]{1,7,50,199})
        {
            var part=MachineTimeline.StartFish(new(),cfg,new[]{new BaitSpend(0,n,BaitKind.Normal,null)},35,start,seed);
            check(MachineTimeline.Valid(part)&&part.BaitUsed==n&&part.Timeline!.Plan.Count==n&&part.CycleBait==200,"partial load of "+n+" bait plans exactly "+n+" casts");
            check(part.DueAt>start&&part.DueAt<=state.DueAt,"partial load of "+n+" bait finishes no later than a full cycle");
            check(part.Timeline.Plan.All(o=>MachineTimeline.Time(o.At) is >=600 and <2600),"partial casts stay inside 06:00-02:00");
        }
        check(MachineTimeline.DueAfterLegal(CycleClock.ToAbsoluteMinutes(5,2550),30)==CycleClock.ToAbsoluteMinutes(6,620),"legal time skips 02:00-06:00");
        reject(()=>MachineTimeline.StartFish(new(),cfg,new[]{new BaitSpend(0,201,BaitKind.Normal,null)},35,start,seed),"more than a full cycle of bait is refused");
        {
            var credit=new ProductionState();long t0=start;
            for(int k=0;k<4;k++)
            {
                var run=MachineTimeline.StartFish(credit,cfg,new[]{new BaitSpend(0,50,BaitKind.Normal,null)},35,t0,seed+k);
                run.Timeline!.Cursor=run.Timeline.Plan.Count;run.FishCount=run.BaitUsed;
                check(run.Complete(run.DueAt),"partial cycle completes");
                check(run.CompletedCycles==(k==3?1:0),"four 50-bait runs count as one full cycle for upgrades (run "+(k+1)+")");
                credit=run.IdleCopy();t0=run.DueAt;
            }
        }
        check(MachineTimeline.Valid(state)&&state.Pending.Count==0&&state.Timeline!.Cursor==0,"paid new batch contains no future result");
        cfg.FishCriticalChance=1;check(state.Timeline!.FishingConfig!.FishCriticalChance==0,"production balance frozen for paid cycle");
        var journal=new MachineEnvironmentJournal();
        MachineEnvironmentProfile Profile(int day,bool rain)=>new(){Location="Fixture",Day=day,Rain=rain,Fish=new(){new(600,1800,new(){new(rain?"(O)rain-day":"(O)sun-day",1)}),new(1800,2600,new(){new(rain?"(O)rain-night":"(O)sun-night",1)})},Other=new(){"(O)168"}};
        var sunny=Profile(111,false);journal.Record(state.CycleToken,start,sunny,false);sunny.Rain=true;
        check(!journal.Cycles[state.CycleToken][0].Profile.Rain,"journal owns a detached snapshot");
        journal.Record(state.CycleToken,111L*1440+720,Profile(111,true),false);
        journal.Record(state.CycleToken,111L*1440+800,Profile(111,true),true); // sleep 13:20; closes today's actual weather through 26:00
        journal.Record(state.CycleToken,112L*1440+360,Profile(112,false),false);
        journal.Record(state.CycleToken,112L*1440+900,Profile(112,false),true);
        check(!journal.At(state.CycleToken,start+1).Rain&&journal.At(state.CycleToken,111L*1440+721).Rain,"weather change splits observations; earlier sun remains sun");
        check(journal.At(state.CycleToken,111L*1440+1500).Rain&&!journal.At(state.CycleToken,112L*1440+1500).Rain,"sleep window uses that day's weather, never next-day weather");
        bool missing=false;try{journal.At(state.CycleToken,113L*1440+360);}catch(InvalidOperationException){missing=true;}check(missing,"unknown skipped days are not fabricated");
        var restored=JsonSerializer.Deserialize<MachineEnvironmentJournal>(JsonSerializer.Serialize(journal))!;
        check(restored.At(state.CycleToken,111L*1440+1500).Rain,"saved environmental windows survive JSON reload");
        for(int i=0;i<state.Timeline.Plan.Count;i++)
        {
            var op=state.Timeline.Plan[i];var env=restored.At(state.CycleToken,op.At);int clock=MachineTimeline.Time(op.At);
            var fishes=env.Fish.Single(f=>clock>=f.From&&clock<f.Until).Fish;
            var unit=Production.Begin(state.Timeline.FishingConfig!,new[]{state.Timeline.FishingBait[op.Bait] with{Count=1}},fishes,env.Other,op.At,MachineTimeline.SeedFor(seed,op.Ordinal,0x38211),state.IdleCopy(),singleOpportunity:true);
            MachineTimeline.Merge(state,unit);
            check(MachineTimeline.Valid(state),"partial paid state valid at cursor "+i);
            if(i==50){string saved=state.Save();state=ProductionState.Load(saved);check(saved==state.Save(),"partial generated outcomes and cursor resume byte-identically");}
        }
        check(state.FishCount==200&&state.Pending.Sum(p=>p.Count)==200&&state.Pending.Select(p=>p.Id).ToHashSet().SetEquals(new[]{"(O)sun-day","(O)sun-night","(O)rain-day","(O)rain-night"}),"actual shared production kernel selects only each opportunity's observed time/weather pool");
        check(state.Complete(due)&&!state.Complete(due+1)&&state.CompletedCycles==1,"whole budget finishes once, not once per segment");
        var bad=state.Snapshot();bad.Timeline!.Plan[0]=bad.Timeline.Plan[0] with{Ordinal=-1};check(!MachineTimeline.Valid(bad),"invalid ordinal rejected without resetting paid state");
        var accelerated=MachineTimeline.StartFish(new(),cfg,new[]{new BaitSpend(0,200,BaitKind.Normal,null)},35,start,seed);
        var original=accelerated.Timeline!.Plan.ToArray();long now=start+800;
        accelerated.DueAt=now+(due-now)*7/10;MachineTimeline.Shorten(accelerated,now,due);
        check(MachineTimeline.Valid(accelerated)&&accelerated.Timeline.Plan.All(p=>p.At<accelerated.DueAt),"totem compresses future opportunities inside shortened deadline, preserving legal hours");
        check(original.Where(p=>p.At<now).SequenceEqual(accelerated.Timeline.Plan.Where(p=>p.At<now)),"totem never rewrites unprocessed historical opportunities");
        check(original.Select(p=>(p.Ordinal,p.Bait)).OrderBy(p=>p.Ordinal).SequenceEqual(accelerated.Timeline.Plan.Select(p=>(p.Ordinal,p.Bait)).OrderBy(p=>p.Ordinal)),"totem preserves every paid bait and per-op seed identity");
        var crab=MachineTimeline.StartCrab(new(){Kind=MachineKinds.Crab},new(){LevelOneBait=20},new[]{new CrabBait(0,"(O)685",10,false)},35,true,true,start,seed);
        foreach(var op in crab.Timeline!.Plan)
        {
            var unit=CrabRules.Begin(crab.IdleCopy(),new[]{crab.Timeline.CrabBait[op.Bait] with{Count=1}},new[]{new CrabAquatic("(O)717",1)},35,true,true,op.At,MachineTimeline.SeedFor(seed,op.Ordinal,0x38211),crab.Timeline.CrabConfig,singleOpportunity:true);
            MachineTimeline.Merge(crab,unit);check(MachineTimeline.Valid(crab),"lure/book partial timeline state validates");
        }
        check(crab.BaitUsed==10&&crab.FishCount+crab.OtherCount==20&&crab.Pending.Sum(p=>p.Count)==20+crab.CriticalFishCount+crab.Crab!.BookBonus,"lure doubles opportunities once, crit/book apply once, complete conservation");
        var target=Production.Begin(new(){BaitPerCycle=1,Targeted=new(){Fish=1,TargetShareOfFish=1}},new[]{new BaitSpend(0,1,BaitKind.Targeted,"(O)absent")},new[]{new FishChoice("(O)128",1)},new[]{"(O)168"},start,1,singleOpportunity:true);
        check(target.EmptyCount==1&&target.Pending.Count==0,"expired target bait cannot silently become ordinary bait");
    }
}

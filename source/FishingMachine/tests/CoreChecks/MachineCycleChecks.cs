using Eddie.FishingMachine.Core;
using System.Text.Json;

internal static class MachineCycleChecks
{
    internal static void Run(Action<bool,string> check, Action<Action,string> reject)
    {
        var fishConfig=JsonSerializer.Deserialize<BalanceConfig>("{\"CycleGameMinutes\":930,\"BaitPerCycle\":400,\"FishCriticalChance\":0.31,\"RulesVersion\":1,\"LevelThreeTripleChance\":0.15}")!;
        check(fishConfig.MigrateCycle() && fishConfig.CycleGameMinutes==2880 && fishConfig.PreviousCycleGameMinutes==930,"fish config migrates once and records custom prior duration");
        check(fishConfig.BaitPerCycle==400 && fishConfig.FishCriticalChance==.31 && fishConfig.RulesVersion==1 && fishConfig.LevelThreeTripleChance==.15,"cycle migration does not overwrite other config or prior rules marker");
        fishConfig.CycleGameMinutes=720;
        var reloaded=JsonSerializer.Deserialize<BalanceConfig>(JsonSerializer.Serialize(fishConfig))!;
        check(!reloaded.MigrateCycle() && reloaded.CycleGameMinutes==720,"later user custom duration survives restart");
        var crabConfig=new CrabBalanceConfig{CycleGameMinutes=4000,LevelOneBait=200,LevelOneCriticalChance=.42};
        check(crabConfig.MigrateCycle() && crabConfig.CycleGameMinutes==2880 && crabConfig.PreviousCycleGameMinutes==4000 && crabConfig.LevelOneBait==200 && crabConfig.LevelOneCriticalChance==.42,"crab independent migration retains quantities and chances");
        crabConfig.CycleGameMinutes=10;check(!crabConfig.MigrateCycle() && crabConfig.CycleGameMinutes==10,"crab later duration retained");
        check(MachineCycleRules.RequiredCycles(MachineKinds.Fishing,1)==30 && MachineCycleRules.RequiredCycles(MachineKinds.Fishing,2)==40 && MachineCycleRules.RequiredCycles(MachineKinds.Fishing,3)==0,"fish stages 30,40,MAX");
        check(MachineCycleRules.RequiredCycles(MachineKinds.Crab,1)==30 && MachineCycleRules.RequiredCycles(MachineKinds.Crab,2)==0,"crab stages 30,MAX");
        reject(()=>MachineCycleRules.RequiredCycles(MachineKinds.Crab,3),"no invented crab Lv3");
        var lv1Materials=MachineUpgrade.Requirements(1).Select((m,i)=>new UpgradeSlot(i,m.Id,m.Count,true)).ToArray();
        var lv2Materials=MachineUpgrade.Requirements(2).SelectMany(m=>m.Id.StartsWith("(T)")?Enumerable.Repeat((m.Id,Count:1),m.Count):new[]{m}).Select((m,i)=>new UpgradeSlot(i,m.Id,m.Count,true)).ToArray();
        foreach(int n in new[]{29,30,31,35})check((MachineUpgrade.Plan(new(){CompletedCycles=n},lv1Materials)!=null)==(n>=30),"Lv1 exact progress boundary "+n);
        foreach(int n in new[]{39,40,41,45})check((MachineUpgrade.Plan(new(){Level=2,CompletedCycles=500,LevelCycles=n},lv2Materials)!=null)==(n>=40),"Lv2 stage rather than lifetime boundary "+n);
        foreach(int day in new[]{0,27,55,83,111})foreach(int time in new[]{600,1200,2350,2500})
        foreach(var machine in new[]{(MachineKinds.Fishing,1),(MachineKinds.Fishing,2),(MachineKinds.Fishing,3),(MachineKinds.Crab,1),(MachineKinds.Crab,2)})
        {
            var idle=new ProductionState{Kind=machine.Item1,Level=machine.Item2,CompletedCycles=57,LevelCycles=12,OwnerId=91,PersistentId="fixture"};
            long start=CycleClock.ToAbsoluteMinutes(day,time);
            ProductionState state;
            if(idle.Kind==MachineKinds.Crab)
                state=CrabRules.Begin(idle,new[]{new CrabBait(0,"(O)685",CrabRules.Required(idle.Level,false),false)},new[]{new CrabAquatic("(O)717",1)},91,false,false,start,123);
            else
                state=Production.Begin(new(),new[]{new BaitSpend(0,MachineUpgrade.RequiredBait(idle,new()),BaitKind.Normal,null)},new[]{new FishChoice("(O)128",1)},new[]{"(O)168"},start,123,idle);
            string original=state.Save();
            check(state.DueAt==start+2880 && !state.Complete(start+1440) && !state.Complete(start+2879),"all existing levels keep two-day deadline");
            check(state.Save()==original,"early polling does not mutate state");
            check(state.Complete(start+2880) && state.CompletedCycles==58 && state.LevelCycles==13 && !state.Complete(start+9999),"one cycle increment at exact deadline");
            var legacy=ProductionState.Load(original);legacy.Schema=6;legacy.CycleRulesVersion=0;legacy.DueAt=start+1440;
            string legacyRaw=legacy.Save();var migrated=MachineStateMigration.Prepare(legacy);
            check(legacy.Save()==legacyRaw,"migration prepares detached copy");
            check(migrated.LegacyPrecomputed && migrated.OriginalCycleMinutes==1440 && migrated.DueAt==start+2880 && migrated.Schema==7,"legacy working migration records prior period");
            check(migrated.Pending.SequenceEqual(legacy.Pending) && migrated.Seed==legacy.Seed && migrated.BaitUsed==legacy.BaitUsed && migrated.CycleToken==legacy.CycleToken && migrated.OwnerId==91 && migrated.PersistentId==legacy.PersistentId,"paid outputs, seed, quota token, owner and identity retained");
            check(MachineStateMigration.Prepare(ProductionState.Load(migrated.Save())).Save()==migrated.Save(),"migration and reload idempotent");
            migrated.Pending.Clear();check(legacy.Pending.Count>0,"migration pending list deep copied");
            var ready=ProductionState.Load(legacyRaw);ready.Complete(ready.DueAt);
            var readyNew=MachineStateMigration.Prepare(ready);
            check(readyNew.Phase=="ready" && readyNew.DueAt==ready.DueAt && readyNew.CompletedCycles==58 && readyNew.Pending.SequenceEqual(ready.Pending) && !readyNew.LegacyPrecomputed,"old ready results not delayed or counted twice");
            var portable=MachineStateMigration.Prepare(idle);
            check(MachineUpgrade.ValidPortable(portable.Save()) && portable.OwnerId==91 && portable.CompletedCycles==57 && portable.LevelCycles==12,"portable old progress and owner retained without inferred stages");
        }
        var customPaid=Production.Begin(new(){CycleGameMinutes=40320},new[]{new BaitSpend(0,350,BaitKind.Normal,null)},new[]{new FishChoice("(O)128",1)},new[]{"(O)168"},500,222);
        customPaid.Schema=6;customPaid.CycleRulesVersion=0;
        check(MachineStateMigration.Prepare(customPaid).OriginalCycleMinutes==40320 && MachineStateMigration.Prepare(customPaid).DueAt==3380,"formal migration records and replaces legacy custom duration");
        customPaid.DueAt=500;reject(()=>MachineStateMigration.Prepare(customPaid),"invalid legacy duration preserved and rejected");
        customPaid.DueAt=3380;customPaid.Schema=7;customPaid.CycleRulesVersion=1;customPaid.LegacyPrecomputed=true;
        reject(()=>ProductionState.Load(customPaid.Save()),"missing original-duration audit field rejected");
        customPaid.OriginalCycleMinutes=1440;customPaid.CycleRulesVersion=2;
        reject(()=>ProductionState.Load(customPaid.Save()),"unknown future cycle rule version rejected");
    }
}

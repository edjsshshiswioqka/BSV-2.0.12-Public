using Eddie.FishingMachine.Core;
internal static class Lv3Checks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var cfg=new BalanceConfig();
        check(cfg.LevelThreeTripleChance==.20,"new config triple critical is 20 percent");
        var oldConfig=System.Text.Json.JsonSerializer.Deserialize<BalanceConfig>("{\"LevelThreeTripleChance\":0.15,\"FishCriticalChance\":0.31,\"BaitPerCycle\":400}")!;
        check(oldConfig.Migrate() && oldConfig.LevelThreeTripleChance==.20 && oldConfig.FishCriticalChance==.31 && oldConfig.BaitPerCycle==400,"0.8.0 default migrates to 20 percent without overwriting unrelated custom settings");
        check(!oldConfig.Migrate(),"config migration happens only once");
        var custom=new BalanceConfig{LevelThreeTripleChance=.10};custom.Migrate();check(custom.LevelThreeTripleChance==.10,"explicit non-default custom critical chance is preserved");
        var edited=new BalanceConfig{RulesVersion=1,LevelThreeTripleChance=.15};check(!edited.Migrate() && edited.LevelThreeTripleChance==.15,"post-upgrade custom settings are not reset each startup");var s=new ProductionState{Level=2,CompletedCycles=500,LevelCycles=39};
        var materials=new[]{new UpgradeSlot(0,"(O)337",12),new(1,"(O)336",25),new(2,"(T)AdvancedIridiumRod",1,true),new(3,"(T)AdvancedIridiumRod",1,true),new(4,"(T)IridiumRod",1,true),new(5,"(T)IridiumRod",1,true)};
        check(MachineUpgrade.Plan(s,materials)==null,"lifetime cycles never substitute for forty Lv2 cycles");
        s.LevelCycles=40;var cost=MachineUpgrade.Plan(s,materials)!;
        check(cost.Count==6 && cost[0].Count==10 && cost[1].Count==20 && cost.Skip(2).All(p=>p.Count==1),"Lv3 exact six-slot upgrade cost");
        check(MachineUpgrade.Plan(s,materials.Select(p=>p.Index==2?p with{SafeTool=false}:p))==null,"attached or enchanted rod cannot be consumed");
        check(MachineUpgrade.Plan(s,materials.Take(5))==null,"two of EACH rod required atomically");
        var old=ProductionState.Load("{\"Schema\":5,\"Level\":2,\"CompletedCycles\":501}");
        check(old.LevelCycles==0 && old.CompletedCycles==501 && old.IdleCopy().Level==2,"old Lv2 stage starts at zero without losing lifetime progression");
        var lv3=new ProductionState{Level=3,CompletedCycles=100,LevelCycles=0};
        check(MachineUpgrade.Plan(lv3,materials)==null && MachineUpgrade.RequiredBait(lv3,cfg)==650 && MachineUpgrade.Capacity(3)==70,"Lv3 cannot upgrade again; fuel and capacity");
        check(MachineLevelRules.Quality(1,.24999)==1 && MachineLevelRules.Quality(1,.25)==0,"Lv1 quality threshold");
        check(MachineLevelRules.Quality(3,.79999)==4 && MachineLevelRules.Quality(3,.8)==2,"Lv3 quality threshold");
        check(MachineLevelRules.ExtraMultiplier(cfg,3,.659999)==1 && MachineLevelRules.ExtraMultiplier(cfg,3,.66)==2 && MachineLevelRules.ExtraMultiplier(cfg,3,cfg.LevelThreeCriticalChance+cfg.LevelThreeTripleChance)==0,"exclusive 66/20/14 critical thresholds");
        check(MachineLevelRules.Treasure(3,.09999)==1 && MachineLevelRules.Treasure(3,.10)==2 && MachineLevelRules.Treasure(3,.25)==0,"10/15/75 treasure thresholds");
        check(Math.Abs(cfg.GetRates(BaitKind.Normal,3).OtherShare-cfg.Normal.OtherShare/2)<1e-12,"Lv3 halves normalized ordinary-item share");
        reject(()=>new BalanceConfig{LevelThreeCriticalChance=.9,LevelThreeTripleChance=.2}.Validate(),"overlapping critical probabilities rejected");
        var fish=new[]{new FishChoice("(O)128",1),new FishChoice("(O)163",100,true)};
        var plan=new[]{new BaitSpend(0,650,BaitKind.Challenge,null)};
        var forced=new BalanceConfig{Normal=new(){Fish=1},LevelThreeCriticalChance=0,LevelThreeTripleChance=1};
        var nine=Production.Begin(forced,plan,fish,new[]{"(O)168"},0,42,lv3);
        check(nine.BaitUsed==650 && nine.FishCount==650 && nine.CompletedCycles==100,"650 bait means 650 primary rolls, not 650 completed cycles");
        check(CatchRoller.Simulate(forced,BaitKind.Challenge,42,3).ProducedFish==5850,"Lv3 simulation uses 650 trials, triple challenge can yield 5850 total fish");
        reject(()=>Production.Begin(cfg,new[]{new BaitSpend(0,450,BaitKind.Normal,null)},fish,new[]{"(O)168"},0,42,lv3),"new Lv3 batch refuses old 450-bait plan");
        check(nine.ProducedFishCount==5850 && nine.LegendaryPlans.Count==1 && nine.LegendaryPlans[0].Count==9,"challenge triple critical yields nine, one legendary event per species per batch");
        check(nine.Pending.Where(p=>p.Id=="(O)163").Sum(p=>p.Count)==9,"same-batch legendary duplication blocked without losing ordinary fish");
        check(nine.LegendaryPlans[0].FallbackId=="(O)128" && ProductionState.Load(nine.Save()).Save()==nine.Save(),"fixed ordinary fallback and iridium qualities survive serialization");
        long all=0,purple=0,gold=0,normal=0,advanced=0,baseFish=0,other=0;
        var normalFish=new[]{new FishChoice("(O)128",1)};
        for(int seed=0;seed<600;seed++)
        {
            var b=Production.Begin(cfg,plan,normalFish,new[]{"(O)168"},360,seed,lv3,"(O)CaveJelly");
            var outputs=b.Pending.Where(p=>p.Id=="(O)128").ToArray();
            check(outputs.All(p=>p.Quality is 2 or 4),"Lv3 never creates ordinary or silver fish");
            check(b.Pending.Sum(p=>(long)p.Count)==b.ProducedFishCount+b.OtherCount+b.TreasureItemCount,"Lv3 full output conservation");
            check(ProductionState.Load(b.Save()).Save()==b.Save(),"Lv3 fixed batch reload");
            all+=outputs.Sum(p=>(long)p.Count);purple+=outputs.Where(p=>p.Quality==4).Sum(p=>(long)p.Count);gold+=outputs.Where(p=>p.Quality==2).Sum(p=>(long)p.Count);
            normal+=b.NormalTreasures;advanced+=b.AdvancedTreasures;baseFish+=b.FishCount;other+=b.OtherCount;
            var one=Production.Begin(cfg,new[]{new BaitSpend(0,350,BaitKind.Normal,null)},normalFish,new[]{"(O)168"},0,seed);
            check(one.Pending.Where(p=>p.Id=="(O)128").All(p=>p.Quality is 0 or 1),"Lv1 never creates gold or iridium fish");
        }
        check(Math.Abs(purple/(double)all-.8)<.005 && Math.Abs(gold/(double)all-.2)<.005,"observed 80/20 Lv3 quality distribution");
        check(Math.Abs(normal/(double)baseFish-.10)<.004 && Math.Abs(advanced/(double)baseFish-.15)<.004,"observed 10/15 Lv3 treasure distribution");
        check(Math.Abs(other/390000d-cfg.Normal.OtherShare/2)<.004,"observed reduced item probability");
        long doubles=0,triples=0,singles=0;var random=FishingCritical.RandomFor(998);
        for(int n=0;n<300000;n++){int extra=MachineLevelRules.ExtraMultiplier(cfg,3,random.NextDouble());if(extra==1)doubles++;else if(extra==2)triples++;else singles++;}
        check(Math.Abs(doubles/300000d-.66)<.004 && Math.Abs(triples/300000d-.20)<.004 && Math.Abs(singles/300000d-.14)<.004,"observed mutually exclusive 66/20/14 critical distribution");
        check(MachineUpgrade.ValidPortable(lv3.IdleCopy().Save()),"Lv3 can be carried, placed and refunded through existing protocol");
    }
}

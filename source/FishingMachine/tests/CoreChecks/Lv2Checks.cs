using System.Text.Json.Nodes;
using Eddie.FishingMachine.Core;
internal static class Lv2Checks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var config=new BalanceConfig();
        var lv2=new ProductionState{Level=2,CompletedCycles=50,AutoTrash=true};
        var fish=new[]{new FishChoice("(O)128",1),new FishChoice("(O)129",1)};
        var trash=new[]{"(O)168","(O)169","(O)167"};
        var bars=new[]{new UpgradeSlot(0,"(O)335",7),new(1,"(O)335",17),new(2,"(O)336",11),new(3,"(O)685",450)};
        var state=new ProductionState{CompletedCycles=29};
        check(MachineUpgrade.Plan(state,bars)==null,"29 complete cycles cannot upgrade");
        state.CompletedCycles=30;
        var upgrade=MachineUpgrade.Plan(state,bars)!;
        check(upgrade.SequenceEqual(new[]{new UpgradeSpend(0,7),new(1,13),new(2,10)}),"exact upgrade spends across stacks, preserves surplus and bait");
        check(MachineUpgrade.Plan(state,bars.Where(s=>s.Id!="(O)336"))==null,"missing gold produces no partial spend");
        check(MachineUpgrade.Plan(state,new[]{new UpgradeSlot(0,"(O)335",19),new(1,"(O)336",10)})==null,"19 iron cannot upgrade");
        state.Phase="working";check(MachineUpgrade.Plan(state,bars)==null,"working batch cannot change level midway");
        check(MachineUpgrade.Plan(lv2,bars)==null,"Lv2 cannot consume upgrade cost a second time");
        check(MachineUpgrade.Capacity(1)==70 && MachineUpgrade.Capacity(2)==105,"Lv1 70 slots, Lv2 105 slots");
        check(MachineUpgrade.RequiredBait(lv2,new BalanceConfig{BaitPerCycle=2})==450,"Lv2 always requires 450 bait");
        check(Production.PlanBait(new[]{new BaitSlot(0,449,BaitKind.Challenge)},450,new HashSet<string>{"(O)128"})==null,"449 challenge bait cannot start");
        check(LevelTwoCatch.Quality(0)==1 && LevelTwoCatch.Quality(.399999)==1 && LevelTwoCatch.Quality(.4)==2 && LevelTwoCatch.Quality(.649999)==2 && LevelTwoCatch.Quality(.65)==0,"40/25/35 quality boundaries");
        check(LevelTwoCatch.Treasure(0)==1 && LevelTwoCatch.Treasure(.059999)==1 && LevelTwoCatch.Treasure(.06)==2 && LevelTwoCatch.Treasure(.089999)==2 && LevelTwoCatch.Treasure(.09)==0,"6/3/91 exclusive treasure boundaries");
        foreach(double bad in new[]{-1d,1d,double.NaN,double.PositiveInfinity})
        {reject(()=>LevelTwoCatch.Quality(bad),"invalid quality roll");reject(()=>LevelTwoCatch.Treasure(bad),"invalid treasure roll");}
        foreach(string id in new[]{"(O)167","(O)168","(O)169","(O)170","(O)171","(O)172"})check(MachineUpgrade.IsTrash(id),"specified trash is cleanable");
        foreach(string id in new[]{"(O)SeaJelly","(O)RiverJelly","(O)CaveJelly","(O)152","(O)153","(O)128","(O)335","(O)336","(O)74","(O)685"})check(!MachineUpgrade.IsTrash(id),"valuable/nontrash item preserved: "+id);
        var challenge=new[]{new BaitSpend(0,450,BaitKind.Challenge,null)};
        var allFish=new BalanceConfig{Normal=new(){Fish=1},LevelTwoCriticalChance=0};
        var three=Production.Begin(allFish,challenge,fish,trash,100,99,lv2,"(O)RiverJelly");
        check(three.ProducedFishCount==1350 && three.CriticalFishCount==0 && three.EmptyCount==0,"challenge guaranteed three fish per fish outcome");
        allFish.LevelTwoCriticalChance=1;
        var six=Production.Begin(allFish,challenge,fish,trash,100,99,lv2,"(O)RiverJelly");
        check(six.ProducedFishCount==2700 && six.CriticalFishCount==1350,"challenge critical is six, never four or recursive");
        check(six.NormalTreasures==three.NormalTreasures && six.AdvancedTreasures==three.AdvancedTreasures,"critical multiplier cannot reroll treasure");
        foreach(var output in three.Pending.Where(p=>p.Id is "(O)128" or "(O)129"))
            check(six.Pending.Single(p=>p.Key==output.Key).Count==output.Count*2,"critical preserves species and quality");
        var mixed=new[]{new BaitSpend(0,200,BaitKind.Normal,null),new(1,250,BaitKind.Challenge,null)};
        var mixedBatch=Production.Begin(allFish,mixed,fish,trash,0,11,lv2);
        check(mixedBatch.ProducedFishCount==1900,"mixed fuel only triples the challenge portion");
        check(CatchRoller.Simulate(allFish,BaitKind.Challenge,11).ProducedFish==2700,"console challenge simulation matches sixfold production");
        reject(()=>Production.Begin(config,new[]{new BaitSpend(0,350,BaitKind.Challenge,null)},fish,trash,0,1),"Lv1 cannot consume challenge bait");
        reject(()=>Production.Begin(config,new[]{new BaitSpend(0,350,BaitKind.Normal,null)},fish,trash,0,1,lv2),"Lv2 rejects a 350-bait plan");
        long bases=0,critical=0,normal=0,advanced=0,totalFish=0,silver=0,gold=0;
        long others=0,jellies=0;
        foreach(BaitKind kind in Enum.GetValues<BaitKind>())for(int seed=0;seed<500;seed++)
        {
            var plan=new[]{new BaitSpend(0,450,kind,kind==BaitKind.Targeted?"(O)128":null)};
            var b=Production.Begin(config,plan,fish,trash,1234,seed,lv2,"(O)SeaJelly");
            check(b.BaitUsed==450 && b.DueAt==4114 && b.EmptyCount==0,"Lv2 batch fuel, timing and no-empty conservation");
            check(b.Pending.Sum(p=>(long)p.Count)==(long)b.ProducedFishCount+b.OtherCount+b.TreasureItemCount,"fish+quality+treasure inventory conservation");
            check(ProductionState.Load(b.Save()).Save()==b.Save(),"Lv2 save preserves whole batch, quality, counters, settings");
            check(Production.Begin(config,plan,fish,trash,1234,seed,lv2,"(O)SeaJelly").Save()==b.Save(),"save/load cannot reroll Lv2 outputs");
            check(b.CompletedCycles==50 && !b.Complete(b.DueAt-1) && b.Complete(b.DueAt) && b.CompletedCycles==51,"count only one complete cycle at deadline");
            check(!b.Complete(b.DueAt+999) && ProductionState.Load(b.Save()).CompletedCycles==51,"repeat completion and reload cannot increment again");
            var idle=b.IdleCopy();check(idle.Level==2 && idle.CompletedCycles==51 && idle.AutoTrash && idle.Pending.Count==0,"next batch retains progression and per-machine option");
            bases+=b.FishCount;critical+=b.CriticalFishCount/(kind==BaitKind.Challenge?3:1);normal+=b.NormalTreasures;advanced+=b.AdvancedTreasures;
            foreach(var p in b.Pending.Where(p=>p.Id is "(O)128" or "(O)129"))
            {totalFish+=p.Count;if(p.Quality==1)silver+=p.Count;if(p.Quality==2)gold+=p.Count;}
            others+=b.OtherCount;jellies+=b.Pending.Where(p=>p.Id=="(O)SeaJelly").Sum(p=>p.Count);
        }
        check(Math.Abs(critical/(double)bases-.55)<.005,"55 percent critical on successful fish");
        check(Math.Abs(normal/(double)bases-.06)<.003 && Math.Abs(advanced/(double)bases-.03)<.003,"observed 6 and 3 percent treasure rates per fish");
        check(Math.Abs(silver/(double)totalFish-.40)<.005 && Math.Abs(gold/(double)totalFish-.25)<.005,"observed 40 and 25 percent qualities");
        check(Math.Abs(jellies/(double)others-.10)<.005,"10 percent of item outcomes become local jelly");
        var outputStacks=new[]{new StoredCatch("(O)128",1200,0),new("(O)128",1200,1),new("(O)128",1200,2)};
        var deposit=DepositPlanner.Plan(Array.Empty<BufferSlot>(),outputStacks,3,_=>999);
        check(deposit.Slots.Sum(p=>p.Count)+deposit.Pending.Sum(p=>p.Count)==3600,"full chest never discards mixed quality overflow");
        check(deposit.Slots.Any(p=>p.Key=="(O)128#quality=1") && deposit.Pending.Any(p=>p.Quality==2),"qualities occupy distinct slots and persist pending");
        var remaining=DepositPlanner.Plan(Array.Empty<BufferSlot>(),deposit.Pending,70,_=>999);
        check(remaining.Pending.Count==0 && remaining.Slots.Sum(p=>p.Count)+deposit.Slots.Sum(p=>p.Count)==3600,"70-slot inventory flushes all overflow without duplicates");
        var legacyBatch=Production.Begin(config,new[]{new BaitSpend(0,350,BaitKind.Normal,null)},fish,trash,0,44);
        var legacy=JsonNode.Parse(legacyBatch.Save())!.AsObject();legacy["Schema"]=4;legacy["CycleRulesVersion"]=0;legacy["DueAt"]=1440;
        foreach(var field in new[]{"Level","CompletedCycles","AutoTrash","ChallengeExtraFish","NormalTreasures","AdvancedTreasures","TreasureItemCount"})legacy.Remove(field);
        foreach(var p in legacy["Pending"]!.AsArray())p!.AsObject().Remove("Quality");
        var loaded=ProductionState.Load(legacy.ToJsonString());
        check(loaded.Level==1 && loaded.CompletedCycles==0 && loaded.Pending.SequenceEqual(legacyBatch.Pending.Select(p=>p with{Quality=0})),"0.6 old batches migrate without retroactive reroll or invented cycles");
        check(loaded.Complete(1440) && loaded.CompletedCycles==1,"old in-progress batch counts once when first completed after update");
        var portable=lv2.Save();
        var request=new MachineRequest{Id=Guid.NewGuid().ToString("N"),Sequence=1,Actor=1,Location="Beach",X=1,Y=2,MachineState=portable};
        check(MachineRequest.Load(request.Save()).MachineState==portable,"placement escrow preserves level, count and setting");
        var rejected=MachineReceipt.For(request,ReceiptResult.Rejected);
        check(rejected.MachineState==portable && ActionProtocol.Effect(request,rejected)==ReceiptEffect.ReturnMachine,"failed placement refunds the same upgraded machine");
        var remove=request with{Kind=ActionKind.Remove,MachineId=Guid.NewGuid().ToString("N"),MachineState=null};
        var receipt=MachineReceipt.For(remove,ReceiptResult.Accepted,machineState:portable);
        check(MachineReceipt.Load(receipt.Save()).MachineState==portable && ActionProtocol.Order(remove,receipt)==RequestOrder.Replay,"replayed removal receipt retains upgrade rather than returning fresh Lv1");
        check(!MachineUpgrade.ValidPortable(six.Save()) && MachineUpgrade.ValidPortable(portable),"working results cannot travel as a placeable item");
    }
}

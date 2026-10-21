using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Tools;
internal static class Lv3AdapterChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var mod=new ModEntry();var service=new ProductionService(mod);
        var c=new Chest();c.Location.objects[c.TileLocation]=c;mod.Machines.All[c]=c.Location;
        c.modData[ProductionService.StateKey]=new ProductionState{Level=2,CompletedCycles=499,LevelCycles=19}.Save();
        void Rod(string id)=>c.Items.Add(new FishingRod{QualifiedItemId=id,UpgradeLevel=3});
        c.Items.Add(ItemRegistry.Create("(O)337",17));c.Items.Add(ItemRegistry.Create("(O)336",27));c.Items.Add(ItemRegistry.Create("(O)787",8));
        for(int n=0;n<6;n++)Rod("(T)IridiumRod");
        service.Tick();check(service.Level(c)==2 && c.Items.Count==9,"adapter does not upgrade at 19 Lv2 cycles");
        c.modData[ProductionService.StateKey]=new ProductionState{Level=2,CompletedCycles=500,LevelCycles=20}.Save();
        var protectedRod=(FishingRod)c.Items[3];protectedRod.attachments.Add(ItemRegistry.Create("(O)685"));
        service.Tick();check(service.Level(c)==2 && c.Items[0].Stack==17,"attached rod pauses whole upgrade without consuming bars");
        protectedRod.attachments.Clear();protectedRod.enchantments.Add("test");service.Tick();
        check(service.Level(c)==2,"enchanted iridium rod is protected");protectedRod.enchantments.Clear();
        c.modData.FailNextWrite=true;service.Tick();check(service.Level(c)==2 && c.Items.Count==9,"Lv3 failed state commit restores all six rods, battery and bars");
        service.Tick();check(service.Level(c)==3 && c.Items.Count==2 && c.Items.All(i=>i.Stack==2),"exact Lv3 materials consumed without advanced rods; surplus bars preserved");
        check(service.Read(c).LevelCycles==0 && service.Read(c).CompletedCycles==500 && c.GetActualCapacity()==70,"Lv3 stage reset, total preserved, large chest");
        var carried=ProductionState.Load(service.Portable(c));check(carried.Level==3 && carried.CompletedCycles==500,"actual portable Lv3 state");
        c.Items.Clear();c.Items.Add(ItemRegistry.Create("(O)ChallengeBait",650));service.Tick();
        check(service.Read(c).Phase=="working" && service.Read(c).ChallengeExtraFish==2*service.Read(c).FishCount,"Lv3 adapter accepts challenge bait");
        check(service.Read(c).Pending.Where(p=>p.Id=="(O)128").All(p=>p.Quality is 2 or 4),"adapter creates no forbidden Lv3 fish quality");
        check(MachineLegendaryBridge.CompletionYear(112L*1440+120)==1 && MachineLegendaryBridge.CompletionYear(112L*1440+360)==2,"late winter28 2am remains year1; spring1 6am is year2");
        var owner=Game1.MasterPlayer;owner.modData.Clear();
        owner.fishCaught["(O)163"]=new[]{1,100};
        check(!MachineLegendaryBridge.Available(owner,"(O)163",1),"existing vanilla king history blocks machine for import year");
        owner.fishCaught.Clear();owner.modData.Clear();
        ProductionState Batch(int seed)=>Production.Begin(new BalanceConfig{FishingBalanceVersion=0,LevelThreeBaitPerCycle=650,Normal=new(){Fish=1},LevelThreeCriticalChance=0,LevelThreeTripleChance=1},
            new[]{new BaitSpend(0,650,BaitKind.Challenge,null)},new[]{new FishChoice("(O)128",1),new FishChoice("(O)163",100,true)},new[]{"(O)168"},360,seed,new ProductionState{Level=3});
        var a=Batch(1);string? saved=null;MachineLegendaryBridge.Start(owner,a,()=>saved=a.Save());
        check(saved!=null && MachineLegendaryBridge.Reserved(owner.UniqueMultiplayerID,"(O)163",1),"host persists quota reservation with batch");
        bool conflict=false;try{MachineLegendaryBridge.Start(owner,Batch(2),()=>throw new Exception("must not commit"));}catch(InvalidOperationException){conflict=true;}
        check(conflict,"second machine cannot reserve the same owner/species/year");
        owner.CurrentTool=new FishingRod{isFishing=true};
        check(!MachineLegendaryBridge.CanFinish(a),"active pre-hooked manual fishing defers king-machine completion");
        owner.CurrentTool=null;check(MachineLegendaryBridge.CanFinish(a),"king-machine completion resumes after manual fishing settles");
        a=ProductionState.Load(saved!);a.Complete(a.DueAt);MachineLegendaryBridge.Finish(a,()=>saved=a.Save());
        check(!MachineLegendaryBridge.Available(owner,"(O)163",1) && !MachineLegendaryBridge.Reserved(owner.UniqueMultiplayerID,"(O)163",1),"completion records annual quota and clears reservation");
        check(a.Pending.Where(p=>p.Id=="(O)163").Sum(p=>p.Count)==9 && a.LegendaryPlans.Count==0,"nine king fish remain one completed annual event");
        check(MachineLegendaryBridge.Available(owner,"(O)163",2),"next-year quota reopens");
        owner.modData.Clear();var raced=Batch(4);MachineLegendaryBridge.Start(owner,raced,()=>saved=raced.Save());
        BetterStardewValley.AnnualLegendaryFeature.MarkMachineCatch(owner,"(O)163",1); // pre-hooked manual catch wins
        raced.Complete(raced.DueAt);int promised=raced.Pending.Sum(p=>p.Count);MachineLegendaryBridge.Finish(raced,()=>{});
        check(raced.Pending.All(p=>p.Id!="(O)163") && raced.Pending.Sum(p=>p.Count)==promised,"manual catch race substitutes fixed ordinary fish, retaining quantity and quality");
        owner.modData.Clear();var failed=Batch(5);try{MachineLegendaryBridge.Start(owner,failed,()=>throw new InvalidOperationException("commit failed"));}catch(InvalidOperationException){}
        check(MachineLegendaryBridge.Available(owner,"(O)163",1),"failed start rolls back reservation");
        MachineLegendaryBridge.Start(owner,failed,()=>saved=failed.Save());failed.Complete(failed.DueAt);
        try{MachineLegendaryBridge.Finish(failed,()=>throw new InvalidOperationException("commit failed"));}catch(InvalidOperationException){}
        check(MachineLegendaryBridge.Reserved(owner.UniqueMultiplayerID,"(O)163",1) && BetterStardewValley.AnnualLegendaryFeature.MachineAvailable(owner,"(O)163",1),"failed completion restores ledger and annual record");
        var retry=ProductionState.Load(saved!);retry.Complete(retry.DueAt);MachineLegendaryBridge.Finish(retry,()=>{});
        check(!MachineLegendaryBridge.Available(owner,"(O)163",1) && retry.Pending.Any(p=>p.Id=="(O)163"),"reload retries fixed king batch exactly once after commit failure");
        owner.modData.Clear();
    }
}

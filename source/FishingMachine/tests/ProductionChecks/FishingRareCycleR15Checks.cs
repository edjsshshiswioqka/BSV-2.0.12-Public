using System.Reflection;
using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Objects;
internal static class FishingRareCycleR15Checks
{
 sealed class SeedRandom:Random{readonly int Seed;internal SeedRandom(int seed)=>Seed=seed;public override int Next()=>Seed;}
 internal static void Run(Action<bool,string> check)
 {
  int oldDay=Game1.Date.TotalDays,oldYear=Game1.year,oldTime=Game1.timeOfDay;var rng=Game1.random;var oldTool=Game1.MasterPlayer.CurrentTool;
  var advance=typeof(ProductionService).GetMethod("AdvanceTimeline",BindingFlags.NonPublic|BindingFlags.Instance)!;
  try
  {
   Game1.Date.TotalDays=0;Game1.year=1;Game1.timeOfDay=600;Game1.MasterPlayer.modData.Clear();Game1.MasterPlayer.CurrentTool=null;
   int seed=Enumerable.Range(0,10000).First(n=>CycleLegendaryRules.Decide(2,n,new[]{new CycleLegendaryOpportunity(0,0,"(O)128",9,1,new(){new("(O)163",1,true)})}).Hit);
   Game1.random=new SeedRandom(seed);
   var mod=new ModEntry{Config=new(){LevelTwoBaitPerCycle=4,Normal=new(){Fish=1},LevelTwoCriticalChance=0,LevelTwoTripleChance=1}};
   var service=new ProductionService(mod);
   Chest New()
   {
    var c=new Chest();c.Location.objects[c.TileLocation]=c;c.owner.Value=Game1.MasterPlayer.UniqueMultiplayerID;
    c.modData[ModEntry.Tag+"MachineId"]=Guid.NewGuid().ToString("N");c.modData[ProductionService.StateKey]=new ProductionState{Level=2}.Save();
    c.Items.Add(ItemRegistry.Create("(O)ChallengeBait",4));mod.Machines.All[c]=c.Location;service.Tick();return c;
   }
   var first=New();
   TimelineEnvironment.ReadOverride=(_,at)=>new(){Location=first.Location.NameOrUniqueName,X=(int)first.TileLocation.X,Y=(int)first.TileLocation.Y,Day=MachineTimeline.Day(at),
    Fish=new(){new(600,2600,new(){new("(O)128",1),new("(O)163",1,true)})},Other=new(){"(O)168"}};
   Game1.Date.TotalDays=3;SimulationPump.Drain(service);
   var result=service.Read(first);check(result.CompletedCycles==1&&result.LegendaryStats.Total==1&&result.LegendaryStats.Eligible==1&&result.LegendaryStats.Hits==1&&result.LegendaryStats.Submitted==1,"T09/T10 real service makes one successful eligible-cycle decision");
   check(first.Items.Where(i=>i.QualifiedItemId=="(O)163").Sum(i=>i.Stack)==9&&first.Items.Where(i=>i.QualifiedItemId is "(O)163" or "(O)128").Sum(i=>i.Stack)==36,"T10 one challenge triple fish event replaced inside four paid opportunities");
   var saved=result.Save();service.Reset();SimulationPump.Drain(service);check(service.Read(first).Save()==saved,"T09 repeated settle/reload does not increment cycle stats or duplicate quota");
   // Independently prepared cycle, persisted win plan, manual catch wins before commit.
   mod.Machines.All.Clear();Game1.MasterPlayer.modData.Clear();Game1.Date.TotalDays=0;
   var second=New();Game1.Date.TotalDays=3;
   second.GetMutex().Locked=true;
   while(service.Read(second).Timeline!.Cursor<4)advance.Invoke(service,new object[]{second});
   second.GetMutex().Locked=false;
   var planned=service.Read(second).Snapshot();var t=planned.Timeline!;
   t.LegendaryDecision=CycleLegendaryRules.Decide(2,planned.Seed,t.LegendaryOpportunities);
   check(t.LegendaryDecision.Hit&&t.LegendaryOpportunities.Count==4,"T09 staged cycle has one durable successful roll");
   second.modData[ProductionService.StateKey]=planned.Save();
   BetterStardewValley.AnnualLegendaryFeature.MarkMachineCatch(Game1.MasterPlayer,"(O)163",1);
   SimulationPump.Drain(service);var fallback=service.Read(second);
   check(fallback.LegendaryStats.Hits==1&&fallback.LegendaryStats.CompetitionFallbacks==1&&fallback.LegendaryStats.Submitted==0
      &&second.Items.All(i=>i.QualifiedItemId!="(O)163")&&second.Items.Where(i=>i.QualifiedItemId=="(O)128").Sum(i=>i.Stack)==36,"T09 fixed winning plan falls back after manual competition, no reroll/no lost fish");
  }
  finally{TimelineEnvironment.ReadOverride=null;Game1.Date.TotalDays=oldDay;Game1.year=oldYear;Game1.timeOfDay=oldTime;Game1.random=rng;Game1.MasterPlayer.modData.Clear();Game1.MasterPlayer.CurrentTool=oldTool;}
 }
}

using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Objects;
internal static class RecoveryR9Checks
{
 internal static void Run(Action<bool,string> check)
 {
  BetterStardewValley.MutantLobsterKing.MutantWildQuota.FixtureLedger=new();
  var mod=new ModEntry();var service=new ProductionService(mod);
  var c=new Chest{QualifiedItemId="(BC)"+MachineKinds.CrabId};c.Location.objects[c.TileLocation]=c;
  c.modData[ModEntry.Tag+"MachineId"]=Guid.NewGuid().ToString("N");mod.Machines.All[c]=c.Location;
  var nearby=new Chest();nearby.TileLocation=new(1,0);c.Location.objects[nearby.TileLocation]=nearby;
  nearby.Items.Add(ItemRegistry.Create("(O)685",999));nearby.Items.Add(ItemRegistry.Create("(O)388",81));
  // Paid state is built by the actual scheduler; synthetic ecology replaces Content only.
  var config=new CrabBalanceConfig{LevelOneCriticalChance=1};
  var bait=new CrabBait(0,"(O)685",config.Required(1,false),false);
  var pool=new[]{new CrabAquatic("(O)716",1)};
  ProductionState paid=null!;int hits=0;
  for(int seed=1;seed<=1000;seed++)
  {
   paid=MachineTimeline.StartCrab(new(){Kind=MachineKinds.Crab},config,new[]{bait},Game1.MasterPlayer.UniqueMultiplayerID,false,true,ProductionService.Now,seed);
   hits=paid.Timeline!.Plan.Sum(op=>CrabRules.Begin(new(){Kind=MachineKinds.Crab},new[]{bait with{Count=1}},pool,Game1.MasterPlayer.UniqueMultiplayerID,false,true,op.At,
       MachineTimeline.SeedFor(seed,op.Ordinal,0x38211),config,sewer:true,singleOpportunity:true).Pending.Where(p=>MutantLobsterRules.IsItem(p.Id)).Sum(p=>p.Count));
   if(hits>0)break;
  }
  check(hits>0,"deterministic isolated paid-batch seed contains a natural special hit; formal probability unchanged");
  c.modData[ProductionService.StateKey]=paid.Save();
  int quotaBefore=BetterStardewValley.LobsterKing.KingWildQuota.AvailableReads;
  TimelineEnvironment.ReadOverride=(_,at)=>new(){Location=c.Location.NameOrUniqueName,X=(int)c.TileLocation.X,Y=(int)c.TileLocation.Y,Day=MachineTimeline.Day(at),Sewer=true,Crab=pool.ToList()};
  try
  {
   Game1.Date.TotalDays+=3;
   service.Tick();string checkpoint=service.Read(c).Save();service.Reset();
   check(service.Read(c).Save()==checkpoint,"R9 paid crab cursor and generated output survive cache reload without reroll");
   SimulationPump.Drain(service);
   var result=service.Read(c);
   check(result.CompletedCycles==1&&result.Pending.Count==0&&c.Items.Where(i=>MutantLobsterRules.IsItem(i.QualifiedItemId)).Sum(i=>i.Stack)==1,
     "R9 actual service settles and delivers at most one per farm/year despite multiple potential special hits despite 100 percent ordinary crit and crabbing book");
   check(BetterStardewValley.LobsterKing.KingWildQuota.AvailableReads==quotaBefore,"Sewer production never reads ordinary Lobster King quota");
   check(nearby.Items[0].Stack==999&&nearby.Items[1].Stack==81&&nearby.Items.Count==2,"removed shore transport neither drains nor exports into an adjacent chest");
   string done=result.Save();service.Reset();service.Tick();
   check(service.Read(c).Save()==done,"R9 completed crab batch is stable on repeated polling and reload");
  }
  finally{TimelineEnvironment.ReadOverride=null;BetterStardewValley.MutantLobsterKing.MutantWildQuota.FixtureLedger=null;}
 }
}

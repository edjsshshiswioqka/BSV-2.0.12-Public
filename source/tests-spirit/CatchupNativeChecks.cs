using System.Reflection;
using System.Text.Json;
using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.GameData.Locations;
using ModEntry=Eddie.FishingMachine.ModEntry;
internal static class CatchupNativeChecks
{
 static bool Held(ref bool __result){__result=true;return false;}
 internal static void Run(Farmer player,Dictionary<string,string> fish,IModHelper helper,IMonitor monitor,Action<bool,string> check)
 {
  typeof(Context).GetProperty("IsWorldReady")!.GetSetMethod(true)!.Invoke(null,new object[]{true});
  var harmony=new Harmony("BSV.R11.NativeMutexFixture");
  harmony.Patch(AccessTools.Method(typeof(StardewValley.Network.NetMutex),"IsLockHeld"),prefix:new(typeof(CatchupNativeChecks),nameof(Held)));
  BetterStardewValley.Configuration.SharedPreferences.Register(helper,new(){AutoRestartProduction=true});
  if(Game1.getLocationFromName("Farm") is not Farm){var farm=new Farm();farm.name.Value="Farm";Game1.game1._locations.Add(farm);Game1._locationLookup.Clear();}
  Game1.locationContextData=new Dictionary<string,StardewValley.GameData.LocationContexts.LocationContextData>();
  var location=new GameLocation();location.name.Value="TimelineNativeCatchup";location.map=MutantChecks.Fixture().map;
  Game1.locationData[location.Name]=new(){Fish=new(){new(){Id="both",ItemId="(O)128",Chance=1},new(){Id="rain",ItemId="(O)129",Chance=1}},
      FishAreas=new(){["Default"]=new(){CrabPotFishTypes=new(){"ocean"}}}};
  fish["128"]="Both/50/mixed/1/20/600 2600/spring summer fall winter/both/0/0/1/1/0";
  fish["129"]="Rain/50/mixed/1/20/600 2600/spring summer fall winter/rainy/0/0/1/1/0";
  fish["717"]="Crab/trap/1/0/ocean/1/5";
  foreach(string id in new[]{"685","128","129","717","167","168","169","170","171","172","152","153","157","SeaJelly","RiverJelly","CaveJelly","378","380","384","382","535","536","60","62","336","386","749","72","337","74"})
   Game1.objectData[id]=new(){Name="fixture"+id,DisplayName="fixture"+id,Type=id is "128" or "129" or "717"?"Fish":"Basic",Category=id=="685"?-21:id is "128" or "129" or "717"?-4:0};
  BetterStardewValley.GiantLobster.LobsterData.Objects(Game1.objectData,k=>k);
  BetterStardewValley.LobsterKing.KingData.Objects(Game1.objectData,k=>k,BetterStardewValley.LobsterKing.KingData.ApprovedSashimi);
  Game1.bigCraftableData=new Dictionary<string,StardewValley.GameData.BigCraftables.BigCraftableData>{[ModEntry.Id]=new(){Name="Fish fixture",DisplayName="Fish fixture"},[ModEntry.CrabId]=new(){Name="Crab fixture",DisplayName="Crab fixture"}};
  ItemRegistry.AddTypeDefinition(new StardewValley.ItemTypeDefinitions.BigCraftableDataDefinition());
  var process=typeof(ProductionService).GetMethod("ProcessAutomatic",BindingFlags.NonPublic|BindingFlags.Instance)!;
  try
  {
   foreach(bool crab in new[]{false,true})foreach(int level in Enumerable.Range(1,crab?2:3))
   {
    TimelineEnvironment.Reset();Game1.year=1;Game1.season=Season.Spring;Game1.dayOfMonth=1;Game1.timeOfDay=600;
    location.GetWeather().IsRaining=false;
    var mod=new ModEntry(helper,monitor,(IManifest)Recorder.Make(typeof(IManifest)));
    mod.Config.BaitPerCycle=mod.Config.LevelTwoBaitPerCycle=mod.Config.LevelThreeBaitPerCycle=4;
    mod.CrabConfig.LevelOneBait=mod.CrabConfig.LevelTwoBait=4;
    var service=mod.Production=new(mod);var c=new Chest(true,new(4,4),crab?ModEntry.CrabId:ModEntry.Id){Location=location,SpecialChestType=Chest.SpecialChestTypes.BigChest};
    c.owner.Value=player.UniqueMultiplayerID;c.modData[ModEntry.Tag+"Facing"]="Right";c.modData[ModEntry.Tag+"MachineId"]=Guid.NewGuid().ToString("N");c.modData[ModEntry.Tag+"Role"]="anchor";
    c.modData[ProductionService.StateKey]=new ProductionState{Kind=crab?MachineKinds.Crab:MachineKinds.Fishing,Level=level}.Save();c.Items.Add(ItemRegistry.Create("(O)685",100));
    mod.Machines.Add(location,c);
    void Step(){try{process.Invoke(service,new object[]{c});}catch(TargetInvocationException e){throw e.InnerException!;}}
    void Drain(){for(int i=0;i<2000;i++){string before=service.Read(c).Save();Step();if(before==service.Read(c).Save())return;}throw new Exception("Native backlog stalled");}
    Step();check(service.Read(c).Phase=="working","native paid task starts using actual journal and chest "+crab+level);
    int duration=crab?(level==1?2:4):level+1;
    for(int n=1;n<=5;n++)
    {
     Game1.dayOfMonth=1+n*duration;TimelineEnvironment.Observe(mod);Drain();service.Reset();TimelineEnvironment.Reset();
     check(service.Read(c).CompletedCycles==n&&service.Read(c).Phase=="working","M01 native second and subsequent cycles / uncollected output "+crab+level+"/"+n);
    }
    // No TimeChanged, DayStarted or DayEnding callback: the journal cannot invent those days' weather.
    Game1.season=Season.Summer;Game1.dayOfMonth=3;location.GetWeather().IsRaining=true;
    Drain();var state=service.Read(c);
    check(state.CompletedCycles==30/duration&&state.StartedAt==360+(30/duration)*duration*1440&&state.DueAt-state.StartedAt==duration*1440,"T01/T11 native date mutation catches elapsed level-specific cycles without events "+crab+level);
    check(c.Items.Where(i=>i?.QualifiedItemId=="(O)685").Sum(i=>i.Stack)==100-(30/duration+1)*4,"native inventory pays each completed/active cycle once "+crab+level);
    if(!crab)check(c.Items.All(i=>i?.QualifiedItemId!="(O)129"),"T15 rainy destination never grants rain-only fish in unknown past days "+level);
    var raw=state.Save();service.Reset();TimelineEnvironment.Reset();Drain();check(service.Read(c).Save()==raw,"T12 native cache/journal reload is idempotent "+crab+level);
    // Withdraw and replace an equal fuel quantity before polling: aggregate snapshots alone would miss this.
    Game1.season=Season.Spring;Game1.dayOfMonth=1;Game1.timeOfDay=600;TimelineEnvironment.Reset();
    var reset=new ProductionState{Kind=crab?MachineKinds.Crab:MachineKinds.Fishing,Level=level};
    c.modData[ProductionService.StateKey]=reset.Save();c.Items.Clear();c.Items.Add(ItemRegistry.Create("(O)685",12));service.Reset();Step();
    Game1.dayOfMonth=21;
    var fuel=c.Items.Single(i=>i.QualifiedItemId=="(O)685");fuel.Stack=0;fuel.Stack=8;
    service.Reset();Drain();
    if(service.Read(c).CompletedCycles!=1||service.Read(c).StartedAt!=ProductionService.Now)Console.Error.WriteLine("R15 T06 DIAGNOSTIC "+service.Read(c).Save());
    check(service.Read(c).CompletedCycles==1&&service.Read(c).StartedAt==ProductionService.Now,"T06 native stack events distinguish late replacement from identical old quantity "+crab+level);
    check(service.Read(c).Continuity!.InputCursor>0,"native input observations consumed through a saved cursor "+crab+level);
    Game1.dayOfMonth=1;TimelineEnvironment.Reset();c.modData[ProductionService.StateKey]=reset.Save();
    c.Items.Clear();c.Items.Add(ItemRegistry.Create("(O)685",12));c.modData.Remove(ModEntry.Tag+"InputObservations/v1");service.Reset();Step();
    Game1.dayOfMonth=21;c.Items.Clear();c.Items.Add(ItemRegistry.Create("(O)685",8));
    check(c.modData.ContainsKey(ModEntry.Tag+"InputObservations/v1"),"native slot replacement writes durable evidence before production polling "+crab+level);
    service.Reset();Drain();
    check(service.Read(c).CompletedCycles==1&&service.Read(c).StartedAt==ProductionService.Now,"T06 native remove/add identical fuel cannot backdate replacement "+crab+level);
    // Existing paid work with a missing environmental journal still finishes without charging the task again.
    var paid=service.Read(c);int paidBait=paid.BaitUsed;string token=paid.CycleToken;
    string journalBeforeLoss=Game1.getFarm().modData[ModEntry.Tag+"EnvironmentTimeline/v1"];
    Game1.getFarm().modData.Remove(ModEntry.Tag+"EnvironmentTimeline/v1");TimelineEnvironment.Reset();Game1.dayOfMonth=21+duration;Drain();
    if(!crab&&level>=2)
    {
     check(service.Read(c).CompletedCycles==1&&service.Read(c).Phase=="working"&&service.Read(c).Timeline!.PausedReason!=null,
      "T07 missing ecology retains paid no-trash opportunity instead of empty output "+level);
     Game1.getFarm().modData[ModEntry.Tag+"EnvironmentTimeline/v1"]=journalBeforeLoss;TimelineEnvironment.Reset();Drain();
    }
    check(service.Read(c).CompletedCycles==2&&service.Read(c).CycleToken!=token&&paidBait==4,"M07 paid task survives missing old journal and completes once "+crab+level);

   }
  }
  finally{harmony.UnpatchAll(harmony.Id);TimelineEnvironment.Reset();}
 }
}

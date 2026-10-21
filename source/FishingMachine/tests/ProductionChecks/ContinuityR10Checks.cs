using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
internal static class ContinuityR10Checks
{
 static IEnumerable<ProductionState> Writes(Chest c)=>c.modData.Writes.Where(p=>p.Key==ProductionService.StateKey).Select(p=>ProductionState.Load(p.Value));
 static long Bait(Chest c)=>c.Items.Where(i=>i?.QualifiedItemId=="(O)685"&&i.Stack>0).Sum(i=>(long)i.Stack);
 static long Output(Chest c)=>c.Items.Where(i=>i!=null&&i.QualifiedItemId!="(O)685"&&i.Stack>0).Sum(i=>(long)i.Stack);
 static void AddBait(Chest c,int n){while(n>0){int amount=Math.Min(999,n);c.Items.Add(ItemRegistry.Create("(O)685",amount));n-=amount;}}
 static (ModEntry Mod,ProductionService Service,Chest Chest) Make(bool crab,int level)
 {
  var mod=new ModEntry();var s=new ProductionService(mod);var c=new Chest{QualifiedItemId="(BC)"+(crab?MachineKinds.CrabId:MachineKinds.FishingId)};
  c.modData[ModEntry.Tag+"MachineId"]=Guid.NewGuid().ToString("N");c.modData[ProductionService.StateKey]=new ProductionState{Kind=crab?MachineKinds.Crab:MachineKinds.Fishing,Level=level}.Save();
  c.Location.objects[c.TileLocation]=c;mod.Machines.All[c]=c.Location;return(mod,s,c);
 }
 static void Drain(ProductionService s,int ticks=48)=>SimulationPump.Drain(s);
 internal static void Run(Action<bool,string> check)
 {
  var preference=BetterStardewValley.Configuration.SharedPreferences.Current;preference.AutoRestartProduction=true;Context.IsMainPlayer=true;Game1.player=Game1.MasterPlayer;TimelineEnvironment.ReadOverride=null;
  foreach(bool crab in new[]{false,true})foreach(int level in Enumerable.Range(1,crab?2:3))foreach(int nearbyMode in new[]{0,1,2})
  {
   var (mod,s,c)=Make(crab,level);int duration=crab?(level==1?2:4):level+1;int required=s.RequiredBait(c);AddBait(c,required*4);c.Items.Add(ItemRegistry.Create("(O)390",1));
   Chest? nearby=null;if(nearbyMode>0){nearby=new Chest{TileLocation=new(2,1)};c.Location.objects[nearby.TileLocation]=nearby;for(int i=0;i<(nearbyMode==1?1:70);i++)nearby.Items.Add(ItemRegistry.Create("(O)nearby"+i,999));}
   string label=(crab?"crab":"fish")+level+" neighbor="+nearbyMode;long prior=1;s.Tick();
   check(s.Read(c).Phase=="working"&&Bait(c)==required*3,"B01/B02 initial paid start with old stock "+label);
   for(int cycle=1;cycle<=3;cycle++)
   {
    var paid=s.Read(c).Snapshot();Game1.Date.TotalDays+=duration;Drain(s);var current=s.Read(c);
    long earned=Writes(c).Where(w=>w.CycleToken==paid.CycleToken&&w.Phase=="ready").First().Pending.Sum(p=>(long)p.Count);
    check(current.Phase=="working"&&current.CompletedCycles==cycle&&current.LevelCycles==cycle&&current.DueAt>ProductionService.Now&&current.DueAt-current.StartedAt==duration*1440,"B01/B10 unique completed cycle starts next level-specific task "+label+" cycle="+cycle);
    check(Output(c)==prior+earned&&Bait(c)==required*(3-cycle),"B01 bait/output conservation with three uncollected cycles "+label+" cycle="+cycle);prior+=earned;
    if(nearby!=null)check(nearby.Items.Count==(nearbyMode==1?1:70)&&nearby.Items.All(i=>i.Stack==999),"B02 no reads/writes to nearby inventory "+label);
    string raw=current.Save();s.Reset();s.Tick();check(s.Read(c).Save()==raw&&Output(c)==prior,"B10 reload/repeated callback preserves active task and accumulated output "+label);
   }
   var active=s.Read(c).Snapshot();long taken=Output(c);c.GetMutex().Locked=true;c.GetMutex().Holder=202;
   c.Items.RemoveAll(i=>i.QualifiedItemId!="(O)685");s.Tick();check(s.Read(c).Save()==active.Save(),"B03/B11 taking all old output under remote inventory lock leaves paid task unchanged "+label);
   c.GetMutex().ReleaseLock();preference.AutoRestartProduction=false;Game1.Date.TotalDays+=duration;Drain(s);
   long last=Writes(c).First(w=>w.CycleToken==active.CycleToken&&w.Phase=="ready").Pending.Sum(p=>(long)p.Count);
   check(taken==prior&&Output(c)==last&&s.Read(c).CompletedCycles==4&&s.Read(c).Phase=="idle"&&s.Read(c).LevelCycles==4,"B03/B05 last paid task finishes once after harvest; OFF retains output "+label);
   AddBait(c,required-1);s.Tick();check(Bait(c)==required-1&&s.Read(c).Phase=="idle","B04 insufficient bait remains untouched "+label);
   AddBait(c,1);c.GetMutex().RequestLock(()=>{},()=>throw new Exception("lock"));s.CrabControl(c,Game1.MasterPlayer,ActionKind.Start);s.CrabControl(c,Game1.MasterPlayer,ActionKind.Start);c.GetMutex().ReleaseLock();
   check(s.Read(c).Phase=="working"&&Bait(c)==0&&Output(c)==last,"B04/B05 manual start with old output and OFF pays only once "+label);
   var manual=s.Read(c).Snapshot();mod.Config.CycleGameMinutes=1440;mod.CrabConfig.CycleGameMinutes=1440;s.Tick();check(s.Read(c).DueAt==manual.DueAt,"B06 changed settings never reset paid deadline "+label);
   AddBait(c,required);Game1.Date.TotalDays+=duration;Drain(s);check(s.Read(c).Phase=="idle"&&Bait(c)==required,"B05 OFF does not spend next fuel "+label);
   preference.AutoRestartProduction=true;s.Tick();check(s.Read(c).Phase=="working"&&s.Read(c).DueAt-s.Read(c).StartedAt==1440&&Bait(c)==0,"B05/B06 reenable restarts with retained output and new duration "+label);
   check(s.BlocksRemoval(c),"B12 active task prevents removal "+label);
  }
  foreach(bool crab in new[]{false,true})
  {
   var (mod,s,c)=Make(crab,1);int required=s.RequiredBait(c);AddBait(c,required*2);s.Tick();var paid=s.Read(c).Snapshot();
   // Full real-capacity buffer: later output must remain in the saved ready state.
   while(c.Items.Count<c.GetActualCapacity())c.Items.Add(ItemRegistry.Create("(O)occupied"+c.Items.Count,999));
   Game1.Date.TotalDays+=2;Drain(s);var full=s.Read(c).Snapshot();string token=full.CycleToken;var promised=full.Pending.ToArray();
   check(full.Phase=="ready"&&promised.Length>0&&full.CompletedCycles==1&&Bait(c)==required&&s.Status(c).Contains("status.full"),"B08 full actual capacity preserves paid output and pauses next fuel "+crab);
   string retained=full.Save();s.Reset();Drain(s);check(s.Read(c).Save()==retained&&s.Read(c).CompletedCycles==1,"B08 full capacity repeated completion/reload cannot reroll or recount "+crab);
   c.Items.RemoveAll(i=>i.QualifiedItemId.StartsWith("(O)occupied"));
   // Fail the actual inventory+cycle delivery commit after generated results are durable.
   c.modData.RejectWrite=(key,value)=>key==ProductionService.StateKey&&ProductionState.Load(value).Pending.Sum(p=>(long)p.Count)<promised.Sum(p=>(long)p.Count);
   s.Tick();check(s.Read(c).Save()==retained&&Bait(c)==required&&Output(c)==0,"B08 fault during delivery restores entire inventory and identical pending results "+crab);
   c.modData.RejectWrite=null;s.Tick();check(s.Read(c).Phase=="working"&&s.Read(c).CompletedCycles==1&&Output(c)==promised.Sum(p=>(long)p.Count)&&Bait(c)==0,"B08 retry delivers saved results exactly once and pays next task once "+crab);
   // Separate start failure: inventory and idle/ready state must both roll back.
   var (m2,s2,c2)=Make(crab,1);AddBait(c2,required);c2.Items.Add(ItemRegistry.Create("(O)390",3));c2.modData.RejectWrite=(k,v)=>k==ProductionService.StateKey&&ProductionState.Load(v).Phase=="working";
   s2.Tick();check(s2.Read(c2).Phase=="idle"&&Bait(c2)==required&&Output(c2)==3,"B04 actual failed start leaves all bait and old output "+crab);c2.modData.RejectWrite=null;s2.Tick();check(s2.Read(c2).Phase=="working"&&Bait(c2)==0,"B04 retry successful start consumes once "+crab);
  }
  foreach(bool nonempty in new[]{false,true})foreach(bool crab in new[]{false,true})
  {
   var (m,s,c)=Make(crab,1);var legacy=crab?CrabRules.Begin(new(){Kind=MachineKinds.Crab},new[]{new CrabBait(0,"(O)685",m.CrabConfig.Required(1,false),false)},new[]{new CrabAquatic("(O)717",1)},Game1.MasterPlayer.UniqueMultiplayerID,false,false,ProductionService.Now,123,m.CrabConfig):new ProductionState();legacy.Phase="ready";legacy.CompletedCycles=8;legacy.LevelCycles=8;legacy.Pending.Clear();c.modData[ProductionService.StateKey]=legacy.Save();if(nonempty)c.Items.Add(ItemRegistry.Create("(O)390",9));
   c.Items.Add(ItemRegistry.Create("(O)zero",0));s.Tick();string stable=s.Read(c).Save();s.Reset();s.Tick();
   check(s.Read(c).Phase=="idle"&&s.Read(c).Save()==stable&&s.Read(c).CompletedCycles==8&&Output(c)==(nonempty?9:0)&&c.Items.All(i=>i.Stack>0),"B09 empty/nonempty old ready normalizes once without granting output or cycles; zero stacks removed "+crab+nonempty);
  }
  // All qualities, native-compatible metadata, max stack and full inventory retry.
  {
   var (m,s,c)=Make(false,3);preference.AutoRestartProduction=false;
   var ready=new ProductionState{Level=3,Phase="ready",CompletedCycles=1,LevelCycles=1,BaitUsed=2100,FishCount=2100,Pending=new(){new("(O)128",1005),new("(O)128",2,1),new("(O)128",2,2),new("(O)128",2,4)}};
   var foreign=ItemRegistry.Create("(O)128",5);foreign.modData["source"]="external";c.Items.Add(foreign);c.modData[ProductionService.StateKey]=ready.Save();s.Tick();
   check(c.Items.Single(i=>i.modData.ContainsKey("source")).Stack==5&&c.Items.Where(i=>!i.modData.ContainsKey("source")&&i.Quality==0).Select(i=>i.Stack).OrderBy(i=>i).SequenceEqual(new[]{6,999})&&new[]{1,2,4}.All(q=>c.Items.Single(i=>i.Quality==q).Stack==2),"B07 native-compatible stack predicate preserves metadata/quality and splits above maximum");
   check(s.Read(c).CompletedCycles==1&&s.Read(c).Phase=="idle"&&Output(c)==1016&&s.CollectibleCount(c)==1016,"B07 conservation and visible available count include all deposited qualities");
  }
  preference.AutoRestartProduction=true;TimelineEnvironment.ReadOverride=null;
 }
}

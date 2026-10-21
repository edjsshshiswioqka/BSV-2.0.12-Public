using System.Text.Json;
using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Menus;
int assertions=0;
void Check(bool result,string message){assertions++;if(!result)throw new Exception(message);}
Chest New(ModEntry mod,ProductionState? s=null)
{
    var c=new Chest();c.Location.objects[c.TileLocation]=c;c.modData[ModEntry.Tag+"MachineId"]=Guid.NewGuid().ToString("N");
    if(s!=null)c.modData[ProductionService.StateKey]=s.Save();mod.Machines.All[c]=c.Location;return c;
}
void Add(Chest c,string id,int count=1,int quality=0)=>c.Items.Add(ItemRegistry.Create(id,count,quality));
long Count(Chest c,string id)=>c.Items.Where(i=>i.QualifiedItemId==id).Sum(i=>(long)i.Stack);
var mod=new ModEntry();var service=new ProductionService(mod);
void NextCycle(int days=2){Game1.Date.TotalDays+=days;SimulationPump.Drain(service);} // bounded runtime catch-up, not a precomputed batch
if(args.Contains("--r11")){CatchupR11Checks.Run(Check);Console.WriteLine(JsonSerializer.Serialize(new{status="passed",assertions,live_game=false}));return;}
var c=New(mod,new ProductionState{CompletedCycles=14});
Add(c,"(O)335",26);Add(c,"(O)336",17);Add(c,"(O)787",3);Add(c,"(O)709",10);Add(c,"(O)685",200);
service.Tick();Check(service.Read(c).Phase=="working" && service.Level(c)==1,"14 cycles starts Lv1, leaves upgrade cost");
Check(Count(c,"(O)335")==26 && Count(c,"(O)336")==17 && Count(c,"(O)685")==0,"consume only 200 bait before eligible");
NextCycle();service.Tick();var upgraded=service.Read(c);
Check(upgraded.CompletedCycles==15 && upgraded.Level==2,"actual 15th completion upgrades machine immediately");
int bonusGold=c.modData.Writes.Where(w=>w.Key==ProductionService.StateKey).Select(w=>ProductionState.Load(w.Value)).Max(s=>s.Pending.Where(p=>p.Id=="(O)336").Sum(p=>p.Count));
Check(Count(c,"(O)335")==1 && Count(c,"(O)336")==2+bonusGold && c.GetActualCapacity()==70,"exact upgrade cost, independently generated Lv1 treasure retained, expanded capacity");
var outputs=c.Items.Select(i=>(i.QualifiedItemId,i.Stack,i.Quality)).ToArray();
service.Tick();Check(c.Items.Select(i=>(i.QualifiedItemId,i.Stack,i.Quality)).SequenceEqual(outputs),"repeated tick never charges again or duplicates output");
c.Items.Clear();service.Tick();Check(service.Read(c).Phase=="idle" && service.Read(c).CompletedCycles==15,"collection resets batch but keeps progression");
Add(c,"(O)ChallengeBait",299);service.Tick();Check(service.Read(c).Phase=="idle" && Count(c,"(O)ChallengeBait")==299,"adapter refuses 299 fuel");
Add(c,"(O)ChallengeBait",10);service.Tick();Check(service.Read(c).BaitUsed==300 && Count(c,"(O)ChallengeBait")==9,"adapter consumes exactly 300 across stacks");
var working=service.Read(c);Check(working.ChallengeExtraFish==2*working.FishCount,"actual adapter recognizes challenge bait");
Check(working.Timeline?.Plan.Count==300&&working.Pending.Count==0,"paid plan stores opportunities, not start-time outcomes");
var beforeDue=working.Save();
service.Reset();service.Tick();Check(service.Read(c).Save()==beforeDue,"cache reset/save load does not regenerate working batch");
// Fill all 70 slots with unstackable other IDs while the cycle runs.
c.Items.Clear();for(int n=0;n<70;n++)Add(c,"(O)occupied"+n);
NextCycle(3);service.Tick();var promised=service.Read(c).Pending.ToArray();
Check(service.Read(c).Phase=="ready"&&promised.Length>0&&service.Read(c).Timeline!.Cursor==300,"full big chest keeps all completed timeline fish, quality and treasure");
Check(service.Read(c).ChallengeExtraFish==2*service.Read(c).FishCount,"completed challenge opportunities retain triple quantity");
service.Reset();service.Tick();Check(service.Read(c).Pending.SequenceEqual(promised),"full pending output survives reload unchanged");
service.Tick();Check(service.Read(c).CompletedCycles==16,"blocked delivery cannot increase cycle count repeatedly");
c.Items.Clear();service.Tick();
Check(service.Read(c).Pending.Count==0,"empty big chest receives whole pending batch");
foreach(var p in promised)Check(c.Items.Where(i=>i.QualifiedItemId==p.Id && i.Quality==p.Quality).Sum(i=>i.Stack)==p.Count,"real resolver/deposit preserves quality-specific stack: "+p.Key);
// Carry only idle progression through the production adapter, as the network layer does.
var portable=service.Portable(c);var moved=New(mod,ProductionState.Load(portable));
Check(service.Read(moved).Level==2 && service.Read(moved).CompletedCycles==16,"portable copy retains per-machine progression");
// Per-machine checkbox writes under an open mutex; host must leave all inventory alone until close.
mod.OpenChest.Value=moved;Game1.activeClickableMenu=new ItemGrabMenu{context=moved};moved.GetMutex().Locked=true;
Add(moved,"(O)170",3);Add(moved,"(O)167",4);Add(moved,"(O)SeaJelly",2);Add(moved,"(O)152",2);Add(moved,"(O)335",20);
Check(!service.MenuButton(SButton.T) && !service.Read(moved).AutoTrash,"removed fishing trash hotkey does not mutate state");
service.Tick();Check(Count(moved,"(O)170")==3 && Count(moved,"(O)167")==4,"host cannot clean inventory while player owns chest mutex");
moved.GetMutex().ReleaseLock();Game1.activeClickableMenu=null;mod.OpenChest.Value=null;service.Tick();
Check(Count(moved,"(O)170")==3 && Count(moved,"(O)167")==4,"stored trash and Joja preserved after closing");
Check(Count(moved,"(O)SeaJelly")==2 && Count(moved,"(O)152")==2 && Count(moved,"(O)335")==20,"cleanup preserves gel, algae and bars");
Check(!service.Read(c).AutoTrash,"trash setting is per machine");
// Mouse input is intercepted only inside the setting button, not inventory slots.
mod.OpenChest.Value=moved;Game1.activeClickableMenu=new ItemGrabMenu{context=moved};moved.GetMutex().Locked=true;
Game1.MouseX=400;Game1.MouseY=400;
Check(!service.MenuButton(SButton.MouseLeft) && !service.Read(moved).AutoTrash,"removed setting hotspot leaves native click intact");
Game1.MouseY=300;Check(!service.MenuButton(SButton.MouseLeft),"player inventory click must not toggle settings");
Game1.MouseY=390;Check(!service.MenuButton(SButton.MouseLeft),"click above settings stays with native chest UI");
moved.GetMutex().ReleaseLock();Game1.activeClickableMenu=null;mod.OpenChest.Value=null;
// Upgrade transaction failure cannot consume bars or partially advance state.
var failure=New(mod,new ProductionState{CompletedCycles=50});Add(failure,"(O)335",25);Add(failure,"(O)336",15);Add(failure,"(O)787",3);Add(failure,"(O)709",10);
failure.modData.FailNextWrite=true;service.Tick();
Check(service.Level(failure)==1 && Count(failure,"(O)335")==25 && Count(failure,"(O)336")==15 && Count(failure,"(O)787")==3 && Count(failure,"(O)709")==10,"failed upgrade commit rolls back inventory and level");
service.Tick();Check(service.Level(failure)==2 && failure.Items.Count==0,"upgrade retries safely exactly once");
// Unknown output content stops before spending fuel.
var missing=New(mod,new ProductionState{Level=2,CompletedCycles=50});Add(missing,"(O)685",300);
ItemRegistry.Missing="(O)128";service.Tick();
Check(Count(missing,"(O)685")==300 && service.Read(missing).Phase=="idle","missing output data cannot consume bait");
ItemRegistry.Missing=null;
// Only host produces; paused saves are not mutated.
Context.IsMainPlayer=false;service.Tick();Check(service.Read(missing).Phase=="idle","farmhands cannot independently generate batches");
Context.IsMainPlayer=true;mod.Saving=true;service.Tick();Check(service.Read(missing).Phase=="idle","no state mutation during save");
mod.Saving=false;service.Tick();Check(service.Read(missing).Phase=="working","host resumes once ready");
Lv3AdapterChecks.Run(Check);
MultiplayerCycleChecks.Run(Check);
// Recovery drains the paid cycle without ever purchasing the next batch.
var recovery=New(mod,new ProductionState{Level=3,CompletedCycles=105,LevelCycles=55});
Add(recovery,"(O)685",800);SimulationPump.Drain(service);var savedBatch=service.Read(recovery).Save();
recovery.modData[ProductionService.RecoveryKey]="true";
mod.Config.CycleGameMinutes=20;mod.Config.LevelThreeBaitPerCycle=20;mod.Config.LevelThreeCriticalChance=0;mod.Config.LevelThreeTripleChance=0;
service.Reset();service.Tick();var recoveryPaid=service.Read(recovery).Snapshot();recoveryPaid.Continuity=null;var beforePolicy=ProductionState.Load(savedBatch);beforePolicy.Continuity=null;Check(recoveryPaid.Save()==beforePolicy.Save(),"new settings and cache reset leave current timer/results/cost unchanged");
NextCycle(4);service.Tick();Check(service.Read(recovery).CompletedCycles==106&&service.Read(recovery).Phase!="working"&&Count(recovery,"(O)685")==400,"recovery completes old paid batch exactly once, no automatic bait spend");
service.Tick();Check(service.Read(recovery).CompletedCycles==106,"repeated recovery updates cannot duplicate completion");
recovery.Items.Clear();var recoveredState=ProductionState.Load(service.Portable(recovery));
Check(recoveredState.Level==3&&recoveredState.CompletedCycles==106&&recoveredState.LevelCycles==56,"empty portable recovery retains level and both progress counters");
recovery.modData.Remove(ProductionService.RecoveryKey);Add(recovery,"(O)685",20);service.Tick();
Check(service.Read(recovery).BaitUsed==20&&service.Read(recovery).DueAt-service.Read(recovery).StartedAt==20,"new production uses new settings only after recovery resumes");
var crabConfig=new CrabBalanceConfig{LevelOneBait=20,CycleGameMinutes=30,LevelOneCriticalChance=1};
var crabBatch=CrabRules.Begin(new(){Kind=MachineKinds.Crab},new[]{new CrabBait(0,"(O)685",10,false)},new[]{new CrabAquatic("(O)717",1)},1,true,false,0,17,crabConfig);
Check(crabBatch.BaitUsed==10&&crabBatch.Crab!.Rolls==20&&crabBatch.DueAt==30&&CrabRules.Valid(crabBatch),"custom crab bait respects profession and saved-batch invariants");
var batchRaw=crabBatch.Save();crabConfig.LevelOneBait=1000;crabConfig.CycleGameMinutes=40320;
Check(ProductionState.Load(batchRaw).Save()==batchRaw,"custom crab batch reload does not require today's balance settings");
MachineMigrationChecks.Run(Check);
TimelineAdapterChecks.Run(Check);
// Real production adapter, with transport/content boundaries explicitly faked.
var policy=BetterStardewValley.Configuration.SharedPreferences.Current;
policy.AutoRestartProduction=false;
var gatedMod=new ModEntry();var gated=new ProductionService(gatedMod);var gatedChest=New(gatedMod);
gatedChest.QualifiedItemId="(BC)"+MachineKinds.FishingId;
Add(gatedChest,"(O)685",400);gated.Tick();
Check(gated.Read(gatedChest).Phase=="idle"&&Count(gatedChest,"(O)685")==400,"auto restart OFF cannot buy an idle fish batch");
gatedChest.GetMutex().RequestLock(()=>{},()=>throw new Exception("fixture mutex unexpectedly busy"));
Check(gated.CrabControl(gatedChest,Game1.MasterPlayer,ActionKind.Start)==null,"same authenticated manual command starts fish machine when auto restart OFF");
gatedChest.GetMutex().ReleaseLock();
var started=gated.Read(gatedChest).Snapshot();
Check(started.Phase=="working"&&Count(gatedChest,"(O)685")==200,"manual start pays once under OFF policy");
gatedMod.Config.CycleGameMinutes=1440;
gated.Tick();Check(gated.Read(gatedChest).DueAt==started.DueAt,"new cycle setting never edits saved paid deadline");
Game1.Date.TotalDays+=2;SimulationPump.Drain(gated);
Check(gated.Read(gatedChest).CompletedCycles==1&&gated.Read(gatedChest).Phase!="working"&&Count(gatedChest,"(O)685")==200,"OFF completes and delivers paid cycle but retains next batch bait");
policy.AutoRestartProduction=true;gatedChest.modData[ProductionService.CrabCommandKey]="pending owner command";gated.Tick();
Check(Count(gatedChest,"(O)685")==200,"automatic scheduler yields to pending manual transaction");
gatedChest.modData.Remove(ProductionService.CrabCommandKey);gated.Tick();
Check(gated.Read(gatedChest).Phase=="working"&&Count(gatedChest,"(O)685")==0
    &&gated.Read(gatedChest).DueAt-gated.Read(gatedChest).StartedAt==1440,"ON resumes next batch exactly once with new cycle length");
FishingRareCycleR15Checks.Run(Check);
RecoveryR9Checks.Run(Check);
ContinuityR10Checks.Run(Check);
Console.WriteLine(JsonSerializer.Serialize(new{status="passed",assertions,note="Real ProductionService with headless inventory, mutex, item and menu fakes; no live game UI or network session."},new JsonSerializerOptions{WriteIndented=true}));

using System.Reflection;
using System.Text.Json;
using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Locations;
using Microsoft.Xna.Framework;
int assertions=0;
void Check(bool ok,string why){assertions++;if(!ok)throw new Exception(why);}
void CleanNative(){MineShaft.activeMines.Clear();VolcanoDungeon.activeLevels.Clear();}
var mod=new ModEntry();ModEntry.Instance=mod;mod.Caves=new(mod);mod.Caves.Load();
Chest Add(GameLocation l,int level,int x)
{
    var c=new Chest(true,new(x,10),ModEntry.Id);c.Location=l;
    c.modData[ModEntry.Tag+"MachineId"]=Guid.NewGuid().ToString("N");c.modData[ModEntry.Tag+"Role"]="anchor";
    var state=Production.Begin(new BalanceConfig(),new[]{new BaitSpend(0,MachineUpgrade.RequiredBait(new ProductionState{Level=level},new BalanceConfig()),BaitKind.Normal,null)},new[]{new FishChoice("(O)162",1)},new[]{"(O)168"},0,222,new(){Level=level,CompletedCycles=50,AutoTrash=true},"(O)CaveJelly");
    c.modData[ProductionService.StateKey]=state.Save();
    c.Items.Add(ItemRegistry.Create("(O)162",100,2));c.Items.Add(ItemRegistry.Create("(O)CaveJelly",7));c.Items[0].modData["custom"]= "preserve me";
    c.owner.Value=171; c.SpecialChestType=level>=2?Chest.SpecialChestTypes.BigChest:Chest.SpecialChestTypes.None;
    l.objects[c.TileLocation]=c;mod.Machines.Add(l,c);return c;
}
var mine=MineShaft.GetMine("UndergroundMine100");var lv1=Add(mine,1,10);var lv2=Add(mine,2,20);
var volcano=VolcanoDungeon.GetLevel("VolcanoDungeon0");var vc=Add(volcano,2,15);
var empty=MineShaft.GetMine("UndergroundMine15");
var ordinary=new GameLocation{NameOrUniqueName="FarmCave"};var native=Add(ordinary,2,7);
mod.Caves.Save();var bank=Game1.MasterPlayer.team.GetOrCreateGlobalInventory(CaveMachineStorage.InventoryId);
Check(bank.Count==3,"only generated cave machines need extra snapshots; native FarmCave stays in native location save");
Check(bank.All(i=>!ReferenceEquals(i,lv1) && !ReferenceEquals(i,lv2) && !ReferenceEquals(i,vc)),"snapshots never attach live NetFields twice");
Check(bank.Cast<Chest>().All(c=>c.owner.Value==171 && c.Items[0].Quality==2 && c.Items[0].modData["custom"]=="preserve me"),"snapshot includes owner, inventory quality and item metadata");
string fixedCycle=lv2.modData[ProductionService.StateKey];
var saved=bank.Cast<Chest>().Single(c=>c.modData[ModEntry.Tag+"MachineId"]==lv2.modData[ModEntry.Tag+"MachineId"]);
lv2.Items[0].Stack=999;Check(saved.Items[0].Stack==100,"live inventory mutation cannot mutate detached snapshot");
// Execute the real prefix/finalizer around fake vanilla cleanup, including exception restoration.
void Cleanup(Type type,Action nativeCleanup)
{
    var args=new object?[]{null};type.GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args);
    nativeCleanup();var ex=new Exception("simulated cleanup error");
    var result=type.GetMethod("Finalizer",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object?[]{ex,args[0]});
    Check(ReferenceEquals(result,ex),"cleanup finalizer preserves original exception");
}
Cleanup(typeof(KeepMachineMinesPatch),MineShaft.clearActiveMines);
Check(MineShaft.activeMines.Contains(mine) && !mine.Disposed,"Lv1 and Lv2 keep generated mine alive across leave/sleep");
Check(!MineShaft.activeMines.Contains(empty) && empty.Disposed,"unoccupied mine floors still use vanilla cleanup");
Cleanup(typeof(KeepMachineVolcanoPatch),VolcanoDungeon.ClearAllLevels);
Check(VolcanoDungeon.activeLevels.Contains(volcano) && !volcano.Disposed,"volcano map content not disposed under active machine");
// Simulate a new game load using only saved global inventory; all runtime location/index objects disappear.
CleanNative();mod.Machines.All.Clear();mod.Caves.Load();
Check(mod.Machines.All.Count==3,"three generated machines restored after cold load");
foreach(var pair in mod.Machines.All)
{
    var c=pair.Key;var l=pair.Value;
    Check(l.objects.Count== (l is MineShaft?8:4),"all four footprint cells restored per machine");
    Check(c.Items[0].Stack==100 && c.Items[0].Quality==2 && c.Items[1].QualifiedItemId=="(O)CaveJelly","saved fish stacks, gold quality and jelly survive reload");
    Check(ProductionState.Load(c.modData[ProductionService.StateKey]).CompletedCycles==50,"cycle count not lost or incremented by reload");
    Check(c.owner.Value==171 && !c.modData.ContainsKey(CaveMachineStorage.LocationKey),"restored world object preserves owner and drops archive-only metadata");
}
var loadedLv2=mod.Machines.All.Keys.Single(c=>c.modData[ModEntry.Tag+"MachineId"]==lv2.modData[ModEntry.Tag+"MachineId"]);
Check(loadedLv2.modData[ProductionService.StateKey]==fixedCycle,"working batch seed, due time, pending output and setting survive unchanged");
mod.Caves.Load();Check(mod.Machines.All.Count==3,"duplicate load hook cannot clone deployed machines");
// Removing a deployed machine must remove its next saved snapshot.
foreach(var t in WorldService.Footprint(loadedLv2))loadedLv2.Location.objects.Remove(new(t.X,t.Y));mod.Machines.Remove(loadedLv2);
mod.Caves.Save();Check(bank.Count==2,"removed machine does not reappear from stale previous backup");
// A changed map is preserved without overwriting unrelated objects, and recovery keeps full inventory/state.
CleanNative();mod.Machines.All.Clear();var blocked=MineShaft.GetMine("UndergroundMine100");blocked.Blocked=true;
var unrelated=ItemRegistry.Create("(O)388") as StardewValley.Object;blocked.objects[new(10,10)]=unrelated!;
mod.Caves.Load();Check(mod.Machines.All.Count==1 && ReferenceEquals(blocked.objects[new(10,10)],unrelated),"blocked mine keeps snapshot and never overwrites another item");
mod.Caves.Save();Check(bank.Count==2,"unrestored machine remains in next save");
Game1.currentLocation=new GameLocation{NameOrUniqueName="Beach"};mod.Caves.Recover();
Check(mod.Machines.All.Count==2 && mod.Machines.All.Any(p=>p.Value==Game1.currentLocation),"explicit recovery installs complete retained machine beside player");
mod.Caves.Save();Check(bank.Count==1,"recovered permanent-map machine no longer has a duplicate generated snapshot");
// Primary-source mine branches and jelly geography.
Check(CaveFishing.SpecialFish(0,5,3)?.Id=="(O)158" && CaveFishing.SpecialFish(10,5,3)?.Id=="(O)158","upper mine stonefish");
Check(CaveFishing.SpecialFish(40,5,3)?.Id=="(O)161","ice mine ice pip");
Check(CaveFishing.SpecialFish(80,5,3)?.Id=="(O)162","lava mine lava eel");
Check(CaveFishing.SpecialFish(121,5,3)==null,"no invented skull cavern mine special fish");
Check(CaveFishing.Jelly(80,false)=="(O)CaveJelly" && CaveFishing.Jelly(0,false)==null && CaveFishing.Jelly(40,false)==null,"native cave jelly only at lava-mine area");
Check(CaveFishing.Jelly(null,false)=="(O)RiverJelly" && CaveFishing.Jelly(null,true)=="(O)SeaJelly","outdoor jelly geography unchanged");
for(int level=0;level<=10;level++)foreach(int area in new[]{0,10,40,80})
    Check(CaveFishing.SpecialFish(area,level,5)!.Weight is >0 and <=1,"mine weights finite in valid probability range");
// Lv3 uses the exact generated-floor backup and restore path, including iridium stacks.
CleanNative();mod.Machines.All.Clear();bank.Clear();mod.Caves.Load();
var iridiumMine=Add(MineShaft.GetMine("UndergroundMine100"),3,10);
var iridiumVolcano=Add(VolcanoDungeon.GetLevel("VolcanoDungeon0"),3,20);
foreach(var c3 in new[]{iridiumMine,iridiumVolcano})
{
    c3.Items[0].Quality=4;c3.modData[ProductionService.AutoTrashKey]="False";var state3=ProductionState.Load(c3.modData[ProductionService.StateKey]);state3.LevelCycles=12;state3.CompletedCycles=112;c3.modData[ProductionService.StateKey]=state3.Save();
}
var fixedLv3=iridiumMine.modData[ProductionService.StateKey];mod.Caves.Save();
Check(bank.Count==2 && bank.Cast<Chest>().All(c=>c.Items[0].Quality==4),"Lv3 cave snapshots include iridium-quality inventory");
CleanNative();mod.Machines.All.Clear();mod.Caves.Load();
Check(mod.Machines.All.Count==2,"Lv3 mine and volcano survive cold restore");
foreach(var c3 in mod.Machines.All.Keys)
{
    var restored3=ProductionState.Load(c3.modData[ProductionService.StateKey]);
    Check(restored3.Level==3 && restored3.LevelCycles==12 && restored3.CompletedCycles==112,"Lv3 cave stage and lifetime progression preserved");
    Check(new ProductionService(mod).Read(c3).AutoTrash,"deprecated separate option no longer overrides stored legacy metadata");
    Check(c3.Items[0].Quality==4 && c3.SpecialChestType==Chest.SpecialChestTypes.BigChest,"Lv3 cave iridium inventory and 70-slot type preserved");
    Check(c3.modData[ProductionService.StateKey]==fixedLv3,"Lv3 fixed cave batch restored without reroll");
}
// Crab IDs, fixed rolls and a pending manual handoff must survive generated-map storage too.
var crabCave=MineShaft.GetMine("UndergroundMine120");
var crab=new Chest(true,new(30,10),MachineKinds.CrabId){Location=crabCave,SpecialChestType=Chest.SpecialChestTypes.BigChest};
string crabId=Guid.NewGuid().ToString("N");crab.modData[ModEntry.Tag+"MachineId"]=crabId;crab.modData[ModEntry.Tag+"Role"]="anchor";
var crabState=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=2,CompletedCycles=52,PersistentId=crabId},new[]{new CrabBait(0,"(O)DeluxeBait",175,true)},new[]{new CrabAquatic("(O)717",1)},171,true,true,0,5);
crab.modData[ProductionService.StateKey]=crabState.Save();crab.modData[ProductionService.CrabCommandKey]="saved-handoff";crab.owner.Value=171;crab.Items.Add(ItemRegistry.Create("(O)685",33));
crabCave.objects[crab.TileLocation]=crab;mod.Machines.Add(crabCave,crab);mod.Caves.Save();CleanNative();mod.Machines.All.Clear();mod.Caves.Load();
var restoredCrab=mod.Machines.All.Keys.Single(c=>c.modData[ModEntry.Tag+"MachineId"]==crabId);
Check(ModEntry.IsCrab(restoredCrab)&&restoredCrab.Items.Single().Stack==33&&restoredCrab.GetActualCapacity()==70,"crab native ID, storage and remaining bait survive cave reload");
Check(restoredCrab.modData[ProductionService.StateKey]==crabState.Save()&&restoredCrab.modData[ProductionService.CrabCommandKey]=="saved-handoff","crab fixed 350 rolls, operator, count and handoff not rerolled on restore");
Check(WorldService.Footprint(restoredCrab).All(p=>restoredCrab.Location.objects[new(p.X,p.Y)].QualifiedItemId=="(BC)"+MachineKinds.CrabId),"all restored crab occupied cells retain crab ID");
mod.Caves.Reset();Check(!mod.Caves.Active,"title return ends session protection");
Console.WriteLine(JsonSerializer.Serialize(new{status="passed",assertions,note="Actual CaveMachineStorage and cleanup patch methods against native-boundary fakes; no live save serializer, map render or multiplayer session."},new JsonSerializerOptions{WriteIndented=true}));

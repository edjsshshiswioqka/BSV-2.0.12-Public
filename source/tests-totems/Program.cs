using BetterStardewValley;
using BetterStardewValley.Totems;
using BetterStardewValley.Transactions;
using Eddie.FishingMachine.Core;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using System.Reflection;
using MachineMod=Eddie.FishingMachine.ModEntry;
using ProductionService=Eddie.FishingMachine.ProductionService;
using SObject=StardewValley.Object;

int checks=0;var scenarios=new HashSet<string>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;scenarios.Add(name);}
void Reject(Action action,string name){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,name);}
var helper=new IModHelper();var monitor=new IMonitor();var service=new TotemService(helper,monitor,new());
var execute=typeof(TotemService).GetMethod("Execute",BindingFlags.Instance|BindingFlags.NonPublic)!;
WorshipReply Execute(TotemKind kind,long actor=1,int? day=null)=> (WorshipReply)execute.Invoke(service,new object[]{new WorshipRequest(Guid.NewGuid().ToString("N"),actor,kind,0,0,day??Game1.Date.TotalDays)})!;
void Reset(TotemKind kind)
{
 helper.Events.GameLoop.Reset();Data.FailAt=-1;Data.Writes=0;Context.IsWorldReady=Context.IsMainPlayer=true;Context.IsSplitScreen=false;IntegrationHost.Saving=false;
 Game1.Farm=new();Game1.year=1;Game1.season=0;Game1.Date=new(){TotalDays=10};Game1.netWorldState=new(new());Game1.weatherForTomorrow="Sun";Game1.ForcedWeather=null;Game1.eventUp=false;
 Game1.player=Game1.MasterPlayer=new(){UniqueMultiplayerID=1,currentLocation=Game1.Farm,Tile=new(0,1)};Game1.Players=new(){Game1.player,new(){UniqueMultiplayerID=2,currentLocation=Game1.Farm,Tile=new(1,0)}};
 Game1.Farm.objects[new(0,0)]=new(){QualifiedItemId="(BC)"+TotemRules.Id(kind),TileLocation=new(0,0)};
 Utility.Festival=false;HarmonyLib.Harmony.ForeignPatch=false;MachineMod.Instance=new();ProductionService.Now=1000;helper.Multiplayer.Messages.Clear();
}
HoeDirt Plant(int x=3,int day=0,bool regrow=false){var soil=new HoeDirt();soil.crop.dayOfCurrentPhase.Value=day;soil.crop.fullyGrown.Value=regrow;if(regrow)soil.crop.currentPhase.Value=3;Game1.Farm.terrainFeatures[new(x,3)]=soil;return soil;}
string Stable(ProductionState s){var copy=s.Snapshot();copy.DueAt=0;return copy.Save();}
Chest Machine(bool crab=false,int x=4)
{
 var state=crab?CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=2},new[]{new CrabBait(0,"(O)DeluxeBait",350,true)},new[]{new CrabAquatic("(O)717",1)},1,false,true,500,8765)
  :new ProductionState{Level=3,CompletedCycles=105,LevelCycles=5,Phase="working",StartedAt=500,DueAt=2000,BaitUsed=650,FishCount=600,OtherCount=50,Seed=112233,OwnerId=1,
    CriticalFishCount=400,ChallengeExtraFish=300,NormalTreasures=20,AdvancedTreasures=30,TreasureItemCount=50,CycleToken="cycle-token",LegendaryYear=1,Pending=new(){new("(O)145",500,4),new("(O)168",50)}};
 var chest=new Chest{QualifiedItemId="(BC)"+(crab?MachineKinds.CrabId:MachineKinds.FishingId),TileLocation=new(x,4)};
 chest.modData[ProductionService.StateKey]=state.Save();chest.Items.Add(new(){QualifiedItemId="(O)685",Stack=999});Game1.Farm.objects[chest.TileLocation]=chest;return chest;
}
Check(Enum.GetValues<TotemKind>().Length==4,"exactly four author-requested totems");
foreach(var k in Enum.GetValues<TotemKind>())Check(TotemRules.TryKind("(BC)"+TotemRules.Id(k),out var parsed)&&parsed==k,"stable four item IDs");
foreach(long n in Enumerable.Range(0,10001).Select(x=>(long)x).Concat(new[]{long.MaxValue,int.MaxValue,161280L}))
 Check(TotemRules.Shorten(n)==(long)Math.Ceiling((decimal)n*7/10),"integer ceiling of seventy percent without overflow");
for(int a=0;a<8;a++)for(int b=0;b<8;b++)for(int c=0;c<8;c++)for(int day=0;day<=a;day++)
{
 var durations=new[]{a,b,c,99999};var r=TotemRules.Growth(durations,0,day,false)!.Value;
 int left=durations.Take(3).Sum()-day;int after=r.Phase==3?0:durations.Skip(r.Phase).Take(3-r.Phase).Sum()-r.Day;
 Check(after==TotemRules.Shorten(left)&&r.Phase>=0&&r.Phase<4&&r.Day>=0,"growth follows actual phases including zero-length speed phases");
}
Check(TotemRules.Growth(new[]{2,3,99999},2,100,false)==(2,100),"mature crop is unchanged");
Check(TotemRules.Growth(new[]{2,3,99999},2,10,true)==(2,7),"current regrowth uses ceiling rule");
Check(TotemRules.Growth(new[]{2,3,99999},-1,0,false)==null&&TotemRules.Growth(new[]{2,3,99999},0,3,false)==null,"invalid crop phases refused");
foreach(var kind in Enum.GetValues<TotemKind>())
{
 var s=new TotemState().Used(kind,3,110);var load=JsonConvert.DeserializeObject<TotemState>(JsonConvert.SerializeObject(s))!;
 Check(load==s&&load.Remaining(kind,3,110)>0,"cooldown survives serialization");
 Check(load.Remaining(kind,4,112)==(kind==TotemKind.Thunderstorm?10:0),"season changes reset three quotas but not thunderstorm");
 if(kind==TotemKind.Thunderstorm)Check(load.Remaining(kind,4,121)==1&&load.Remaining(kind,4,122)==0,"storm becomes available at exactly twelve absolute days");
}
Reset(TotemKind.Harvest);var soil=Plant();var another=Plant(4,7,true);
Check(Execute(TotemKind.Harvest).Key=="totem.success"&&TotemStorage.Blessed(soil.crop,Game1.Farm),"existing legal Farm crops receive instance blessing");
var oldCrop=soil.crop;string marker=oldCrop.modData[TotemRules.BlessingKey];
for(int fertilizer=0;fertilizer<=3;fertilizer++){soil.Fertilizer=fertilizer;Check(TotemStorage.Quality(oldCrop,soil,Game1.Farm)==(fertilizer==0?2:4),"quality fertilizers promote to iridium; no quality boost including speed-gro gives gold floor");}
Check(!TotemStorage.Blessed(oldCrop,new GameLocation()),"greenhouse/interior/island crop scope excluded");
var later=Plant(7);Check(!TotemStorage.Blessed(later.crop,Game1.Farm),"planted after worship remains unblessed");
soil.crop=new();Check(!TotemStorage.Blessed(soil.crop,Game1.Farm),"same tile replant cannot inherit crop modData");
var copied=JsonConvert.DeserializeObject<Crop>(JsonConvert.SerializeObject(another.crop))!;Check(TotemStorage.Blessed(copied,Game1.Farm),"headless crop instance save/load preserves blessing identity");
Game1.season=1;Game1.Date.TotalDays=28;helper.Events.GameLoop.Day();Check(TotemStorage.Blessed(another.crop,Game1.Farm),"living regrowing plant remains blessed across season");
another.crop.dead.Value=true;helper.Events.GameLoop.Day();Check(!another.crop.modData.ContainsKey(TotemRules.BlessingKey),"dead plant marker cleanup");
Reset(TotemKind.Harvest);Plant();Execute(TotemKind.Harvest);var farmJson=JsonConvert.SerializeObject(Game1.Farm.modData);Game1.Farm.objects.Clear();TotemStorage.Reset();Game1.Farm.modData=JsonConvert.DeserializeObject<Data>(farmJson)!;
Game1.Farm.objects[new(0,0)]=new(){QualifiedItemId="(BC)"+TotemRules.Id(TotemKind.Harvest)};
Check(Execute(TotemKind.Harvest,2).Key=="totem.used","remove-save-load-new-copy cannot reset shared farm quota");
Reset(TotemKind.Growth);soil=Plant();var phases=soil.crop.phaseDays.ToArray();var mature=Plant(4);mature.crop.currentPhase.Value=3;var regrow=Plant(5,10,true);
Check(Execute(TotemKind.Growth).Key=="totem.success"&&soil.crop.currentPhase.Value==1&&soil.crop.dayOfCurrentPhase.Value==1,"runtime Growth plan changes current remaining 10 days to seven");
Check(soil.crop.phaseDays.SequenceEqual(phases)&&regrow.crop.dayOfCurrentPhase.Value==7&&mature.crop.currentPhase.Value==3,"duration/fertilizer definitions and mature crops preserved");
Check(Execute(TotemKind.Growth,2).Key=="totem.used"&&regrow.crop.dayOfCurrentPhase.Value==7,"second player cannot repeat same seasonal effect");
var nextCrop=Plant(9);Check(nextCrop.crop.currentPhase.Value==0&&nextCrop.crop.dayOfCurrentPhase.Value==0,"no persistent growth buff on future crops");
regrow.crop.dayOfCurrentPhase.Value=10;Check(regrow.crop.dayOfCurrentPhase.Value==10,"future regrowth countdown remains vanilla-owned");
Reset(TotemKind.Processing);var vanilla=new SObject{QualifiedItemId="(BC)12",TileLocation=new(2,2),MinutesUntilReady=100};vanilla.heldObject.Value=new(){QualifiedItemId="(O)348",Stack=1};Game1.Farm.objects[vanilla.TileLocation]=vanilla;
var idle=new SObject{QualifiedItemId="(BC)13",TileLocation=new(3,2)};Game1.Farm.objects[idle.TileLocation]=idle;
var ready=new SObject{QualifiedItemId="(BC)15",TileLocation=new(4,2),MinutesUntilReady=100};ready.readyForHarvest.Value=true;ready.heldObject.Value=new();Game1.Farm.objects[ready.TileLocation]=ready;
var fishing=Machine();var crab=Machine(true,5);string fBefore=fishing.modData[ProductionService.StateKey],cBefore=crab.modData[ProductionService.StateKey];var held=vanilla.heldObject.Value;
Check(Execute(TotemKind.Processing).Key=="totem.success","runtime Processing supports safe vanilla and both BSV machines");
Check(vanilla.MinutesUntilReady==70&&idle.MinutesUntilReady==0&&ready.MinutesUntilReady==100&&ReferenceEquals(held,vanilla.heldObject.Value),"working vanilla time only; idle/ready/output unchanged");
foreach(var (chest,before) in new[]{(fishing,fBefore),(crab,cBefore)})
{
 var original=ProductionState.Load(before);var after=ProductionState.Load(chest.modData[ProductionService.StateKey]);
 Check(after.DueAt==ProductionService.Now+TotemRules.Shorten(original.DueAt-ProductionService.Now)&&Stable(after)==Stable(original),"serialized batch changes only DueAt, preserving all production/legendary fields");
 Check(chest.Items.Single().Stack==999&&!chest.Mutex.IsLocked(),"full internal bait stack untouched and mutex released");
 Check(ProductionState.Load(after.Save()).DueAt==after.DueAt,"shortened fishing/crab state survives original production codec validation");
}
Check(MachineMod.Instance.Production.InventoryWrites==0,"totem never writes fishing/crab inventory even with full chests");
Check(Execute(TotemKind.Processing,2).Key=="totem.used"&&vanilla.MinutesUntilReady==70,"two players share Processing quota");
vanilla.MinutesUntilReady=100;Check(vanilla.MinutesUntilReady==100,"next processing cycle not patched or permanently accelerated");
var freshCrab=CrabRules.Begin(new(){Kind=MachineKinds.Crab},new[]{new CrabBait(0,"(O)685",150,false)},new[]{new CrabAquatic("(O)717",1)},1,false,false,9000,42);Check(freshCrab.DueAt-freshCrab.StartedAt==2880,"new crab cycles retain full two-day duration, not a permanent Totem discount");
Reset(TotemKind.Processing);fishing=Machine();crab=Machine(true,5);
foreach(var chest in new[]{fishing,crab})
{
 var legacy=ProductionState.Load(chest.modData[ProductionService.StateKey]);legacy.Schema=6;legacy.CycleRulesVersion=0;
 chest.modData[ProductionService.StateKey]=MachineStateMigration.Prepare(legacy).Save();
}
fBefore=fishing.modData[ProductionService.StateKey];cBefore=crab.modData[ProductionService.StateKey];
Check(Execute(TotemKind.Processing).Key=="totem.success","Processing Totem also accepts migrated paid cycles");
foreach(var (chest,before) in new[]{(fishing,fBefore),(crab,cBefore)})
{
 var prior=ProductionState.Load(before);var shortened=ProductionState.Load(chest.modData[ProductionService.StateKey]);
 Check(shortened.LegacyPrecomputed && shortened.DueAt==ProductionService.Now+TotemRules.Shorten(prior.DueAt-ProductionService.Now) && Stable(shortened)==Stable(prior),"legacy migration metadata coexists with one-time Totem shortening");
 Check(MachineStateMigration.Prepare(shortened).DueAt==shortened.DueAt,"reload cannot undo Processing Totem or re-extend migrated cycle");
}
long yearStart=112*1440+360;Check(TotemRules.DueAt(yearStart-1000,yearStart+10,2)==yearStart,"reserved next-year legendary catch never crosses backward across year boundary");
for(int failure=1;failure<=3;failure++)
{
 Reset(TotemKind.Processing);fishing=Machine();crab=Machine(true,5);fBefore=fishing.modData[ProductionService.StateKey];cBefore=crab.modData[ProductionService.StateKey];Data.Writes=0;Data.FailAt=failure;
 Check(Execute(TotemKind.Processing).Key=="totem.error","injected partial machine/state write reports failure");
 Check(fishing.modData[ProductionService.StateKey]==fBefore&&crab.modData[ProductionService.StateKey]==cBefore&&!Game1.Farm.modData.ContainsKey(TotemRules.SharedKey),"atomic rollback restores exact machine JSON and farm cooldown");
 Check(!fishing.Mutex.IsLocked()&&!crab.Mutex.IsLocked(),"fault path releases all nested machine locks");
}
Reset(TotemKind.Processing);fishing=Machine();crab=Machine(true,5);crab.Mutex.Locked=true;fBefore=fishing.modData[ProductionService.StateKey];Check(Execute(TotemKind.Processing).Key=="totem.busy"&&fishing.modData[ProductionService.StateKey]==fBefore&&!fishing.Mutex.IsLocked()&&!Game1.Farm.modData.Any(),"another player's open chest rejects whole plan without partial timing/cooldown");
Reset(TotemKind.Growth);Plant();Context.IsMainPlayer=false;Check(Execute(TotemKind.Growth).Key=="totem.unavailable"&&!Game1.Farm.modData.Any(),"farmhand cannot directly mutate shared effects");
Context.IsMainPlayer=true;IntegrationHost.Saving=true;Check(Execute(TotemKind.Growth).Key=="totem.unavailable","save gate blocks worship");IntegrationHost.Saving=false;
Check(Execute(TotemKind.Growth,day:9).Key=="totem.unavailable","previous-day packets cannot consume new season allowance");
Game1.player.currentLocation=new();Check(Execute(TotemKind.Growth).Key=="totem.unavailable","remote location worship rejected");
Reset(TotemKind.Growth);Plant();Context.IsSplitScreen=true;
var requestA=new WorshipRequest(Guid.NewGuid().ToString("N"),1,TotemKind.Growth,0,0,10);var requestB=requestA with{Id=Guid.NewGuid().ToString("N"),Actor=2};
helper.Events.Multiplayer.Send(requestA,1,"TotemWorship.v1");helper.Events.Multiplayer.Send(requestB,2,"TotemWorship.v1");helper.Events.GameLoop.Tick();
Check(Game1.Farm.terrainFeatures.Values.Cast<HoeDirt>().Single().crop.dayOfCurrentPhase.Value==1&&helper.Multiplayer.Messages.Any(m=>m.Payload is WorshipReply r&&r.Actor==2&&r.Key=="totem.used"),"near-simultaneous host/remote requests apply once; split-screen host sends remote reply");
helper.Events.Multiplayer.Send(requestA,1,"TotemWorship.v1");helper.Events.GameLoop.Tick();Check(Game1.Farm.terrainFeatures.Values.Cast<HoeDirt>().Single().crop.dayOfCurrentPhase.Value==1,"lost reply replay uses cached success without repeated crop scan");
Reset(TotemKind.Growth);Plant();helper.Events.Multiplayer.Send(requestA,2,"TotemWorship.v1");helper.Events.GameLoop.Tick();Check(!Game1.Farm.modData.Any(),"forged sender actor rejected");
Reset(TotemKind.Growth);Game1.Farm.modData[TotemRules.SharedKey]="{\"Schema\":1}";Check(Execute(TotemKind.Growth).Key=="totem.error"&&Game1.Farm.modData[TotemRules.SharedKey]=="{\"Schema\":1}","incomplete shared state preserved fail closed");
Reset(TotemKind.Growth);Check(Execute(TotemKind.Growth).Key=="totem.no-targets"&&!Game1.Farm.modData.Any(),"no eligible crops does not consume cooldown");
foreach(var forced in new[]{"Festival","GreenRain","Sun","Storm"})
{Reset(TotemKind.Thunderstorm);Game1.ForcedWeather=forced;Check(Execute(TotemKind.Thunderstorm).Key=="totem.weather-blocked"&&!Game1.Farm.modData.Any()&&Game1.weatherForTomorrow=="Sun","forced-date weather protected without cooldown");}
Reset(TotemKind.Thunderstorm);Utility.Festival=true;Check(Execute(TotemKind.Thunderstorm).Key=="totem.weather-blocked","passive festival protected");
Reset(TotemKind.Thunderstorm);Game1.player.friendshipData["Spouse"]=new(){Engaged=true,WeddingDate=new(){TotalDays=11}};Check(Execute(TotemKind.Thunderstorm).Key=="totem.weather-blocked","NPC wedding weather protected");
Reset(TotemKind.Thunderstorm);Game1.player.team.friendshipData[(1,2)]=new(){Engaged=true,WeddingDate=new(){TotalDays=11}};Check(Execute(TotemKind.Thunderstorm).Key=="totem.weather-blocked","player wedding weather protected through read-only friendship lookup");
Reset(TotemKind.Thunderstorm);HarmonyLib.Harmony.ForeignPatch=true;Check(Execute(TotemKind.Thunderstorm).Key=="totem.weather-blocked","unknown patched weather policy fails closed");
Reset(TotemKind.Thunderstorm);Check(Execute(TotemKind.Thunderstorm).Key=="totem.success"&&Game1.weatherForTomorrow=="Storm"&&Game1.netWorldState.Value.WeatherForTomorrow=="Storm"&&Game1.netWorldState.Value.Regional.WeatherForTomorrow=="Storm","all native default-context forecasts change together");
Game1.season=1;Game1.Date.TotalDays=21;Check(Execute(TotemKind.Thunderstorm,2).Days==1,"thunderstorm cooldown survives season boundary");Game1.Date.TotalDays=22;Check(Execute(TotemKind.Thunderstorm,2).Key=="totem.success","thunderstorm ready exactly twelve days later");
// Late failures must restore weather, crop identities and quota together.
for(int fail=1;fail<=3;fail++)
{
 Reset(TotemKind.Harvest);var p1=Plant();var p2=Plant(4);Data.Writes=0;Data.FailAt=fail;
 Check(Execute(TotemKind.Harvest).Key=="totem.error"&&!p1.crop.modData.Any()&&!p2.crop.modData.Any()&&!Game1.Farm.modData.Any(),"partial blessing write rolls back all crop markers and shared quota");
}
Reset(TotemKind.Thunderstorm);Data.Writes=0;Data.FailAt=1;
Check(Execute(TotemKind.Thunderstorm).Key=="totem.error"&&Game1.weatherForTomorrow=="Sun"&&Game1.netWorldState.Value.WeatherForTomorrow=="Sun"&&Game1.netWorldState.Value.Regional.WeatherForTomorrow=="Sun"&&!Game1.Farm.modData.Any(),"cooldown write failure rolls back every tomorrow forecast");
Reset(TotemKind.Growth);soil=Plant();Data.Writes=0;Data.FailAt=1;
Check(Execute(TotemKind.Growth).Key=="totem.error"&&soil.crop.currentPhase.Value==0&&soil.crop.dayOfCurrentPhase.Value==0&&!Game1.Farm.modData.Any(),"cooldown failure restores original crop phase and day");
Reset(TotemKind.Processing);fishing=Machine();fishing.Mutex.Delayed=true;fBefore=fishing.modData[ProductionService.StateKey];
Check(Execute(TotemKind.Processing).Key=="totem.busy","asynchronous lock request never commits after transaction lifetime");fishing.Mutex.Delayed=false;fishing.Mutex.Update(Game1.Farm);
Check(fishing.modData[ProductionService.StateKey]==fBefore&&!Game1.Farm.modData.Any()&&!fishing.Mutex.IsLocked(),"late lock callback exits and releases without effect");
Reset(TotemKind.Processing);fishing=Machine();ProductionService.Now=400;Check(Execute(TotemKind.Processing).Key=="totem.no-targets"&&!Game1.Farm.modData.Any(),"future-start corrupt clock cannot write invalid shortened batch");
Reset(TotemKind.Harvest);soil=Plant();soil.crop.Wild=true;var forage=Plant(4);forage.crop.forageCrop.Value=true;var dead=Plant(5);dead.crop.dead.Value=true;
Check(Execute(TotemKind.Harvest).Key=="totem.no-targets"&&!Game1.Farm.modData.Any(),"dead forage and wild-seed crops excluded from blessing");
Reset(TotemKind.Growth);soil=Plant();soil.crop.phaseDays=new(){1,1,5,99999};
Check(Execute(TotemKind.Growth).Key=="totem.success"&&soil.crop.phaseDays.SequenceEqual(new[]{1,1,5,99999})&&soil.crop.currentPhase.Value==2&&soil.crop.dayOfCurrentPhase.Value==0,"speed-modified phases stay intact while remaining seven days becomes five");
foreach(string lang in new[]{"default","zh"}){var strings=JObject.Parse(File.ReadAllText(Path.Combine(args[0],lang+".json")));foreach(var k in new[]{"ready","used","cooldown","confirm","success","weather-blocked","command","blessed","error"})Check(!string.IsNullOrWhiteSpace(strings["totem."+k]?.ToString()),"totem localization available in both supported languages");}
Console.WriteLine(JsonConvert.SerializeObject(new{passed=true,checks,scenarios=scenarios.OrderBy(x=>x),runtime_sources_under_test=new[]{"TotemRules","TotemStorage","TotemEffects","TotemService","TotemTiming","AtomicChange","ProductionState","CrabRules"},boundary="handwritten API doubles; not real game",real_game=false,real_multiplayer=false},Formatting.Indented));

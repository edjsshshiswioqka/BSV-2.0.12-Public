using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using BetterStardewValley;
using BetterStardewValley.Configuration;
using BetterStardewValley.Farming;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.GameData.Objects;
using StardewValley.ItemTypeDefinitions;
using StardewValley.TerrainFeatures;
using Newtonsoft.Json.Linq;
using SObject=StardewValley.Object;

partial class Program
{
 static readonly BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static readonly List<string> Checks=new();static readonly List<object> Stats=new();
 static Farmer Host=null!;static Farmer Guest=null!;static GameLocation Farm=null!;static FertilizerService Service=null!;
 static bool IsHost=true;static ModConfig Config=new();
 static void Check(bool b,string m){if(!b)throw new Exception(m);Checks.Add(m);}
 static bool Skip()=>false;static bool No(ref bool __result){__result=false;return false;}static bool Yes(ref bool __result){__result=true;return false;}
 static bool MainPlayer(ref bool __result){__result=IsHost;return false;}
 static bool Master(ref Farmer __result){__result=Host;return false;}
 static bool Everyone(ref IEnumerable<Farmer> __result){__result=new[]{Host,Guest}.Where(f=>f!=null);return false;}
 static bool Text(string path,ref string __result){__result=path;return false;}
 static object Blank(Type t){var o=FormatterServices.GetUninitializedObject(t);GC.SuppressFinalize(o);return o;}
 sealed class Services:IServiceProvider{public object? GetService(Type t)=>null;}
 sealed class Content:LocalizedContentManager
 {
  internal Content():base(new Services(),"."){}
  public override T Load<T>(string n)=>(T)(object)(n.Replace('\\','/') switch{
    "Data/Objects"=>Game1.objectData,"Data/Crops"=>Game1.cropData,
    "Data/Machines"=>new Dictionary<string,StardewValley.GameData.Machines.MachineData>(),
    "Data/ObjectContextTags" or "Data/Furniture" or "Strings/StringsFromCSFiles"=>new Dictionary<string,string>(),
    _=>throw new Exception("Missing farming fixture: "+n)});
 }
 static void Player(Farmer f)=>typeof(Game1).GetField("_player",Any)!.SetValue(null,f);
 static Farmer NewFarmer(long id){var f=new Farmer{UniqueMultiplayerID=id};f.buffs.SetOwner(f);f.buffs.Dirty=false;f.maxItems.Value=36;for(int i=0;i<36;i++)f.Items.Add(null);return f;}
 static void Setup()
 {
  Game1.game1=(Game1)Blank(typeof(Game1));Game1.game1._locations=new List<GameLocation>();Game1.otherFarmers=new();Game1._locationLookup=new();Game1.random=new(17);
  typeof(Game1).GetField("log",Any)!.SetValue(null,Recorder.Make(typeof(StardewValley.Logging.IGameLogger)));
  Game1.options=(Options)Blank(typeof(Options));Game1.content=new Content();Game1.netWorldState=new(new StardewValley.Network.NetWorldState());
  typeof(Context).GetProperty("IsWorldReady",Any)!.SetValue(null,true);
  var h=new Harmony("BSV.Farming.NativeFixture");
  void Patch(Type t,string n,string callback,params Type[] parameters)=>h.Patch(t.GetMethod(n,Any,null,parameters,null)!,prefix:new(typeof(Program),callback));
  Patch(typeof(Farmer),"farmerInit",nameof(Skip));Patch(typeof(AnimatedSprite),"LoadTexture",nameof(Skip),typeof(string),typeof(bool));
  Patch(typeof(Context),"get_IsWorldReady",nameof(Yes));Patch(typeof(Context),"get_IsSplitScreen",nameof(No));Patch(typeof(LocalMultiplayer),"IsLocalMultiplayer",nameof(No),typeof(bool));Patch(typeof(Context),"get_IsMainPlayer",nameof(MainPlayer));Patch(typeof(Game1),"get_IsMasterGame",nameof(MainPlayer));
  Patch(typeof(Game1),"get_MasterPlayer",nameof(Master));Patch(typeof(Game1),"getAllFarmers",nameof(Everyone));
  Patch(typeof(StardewValley.BellsAndWhistles.PlayerStatusList),"AddSpriteDefinition",nameof(Skip),typeof(string),typeof(string),typeof(int),typeof(int),typeof(int),typeof(int));
  Patch(typeof(HoeDirt),"initialize",nameof(Skip),typeof(GameLocation));Patch(typeof(Crop),"updateDrawMath",nameof(Skip),typeof(Vector2));
  Patch(typeof(LocalizedContentManager),"LoadString",nameof(Text),typeof(string));
  Host=NewFarmer(1);Guest=NewFarmer(2);Player(Host);Farm=new Farm();Farm.name.Value="Farm";Host.currentLocation=Farm;Guest.currentLocation=Farm;
  Game1.game1._locations.Add(Farm);Game1._locationLookup["Farm"]=Farm;
  Game1.objectData=new Dictionary<string,ObjectData>();Game1.cropData=new Dictionary<string,CropData>();Game1.locationData=new Dictionary<string,StardewValley.GameData.Locations.LocationData>{["Default"]=new()};
  foreach(string id in new[]{"368","369","919","465","466","918","370","371","920","24","472"})
   Game1.objectData[id]=new(){Name=id,DisplayName=id,Type="Basic",Category=id is "24" or "472"?-75:-19,Price=10};
  Game1.cropData["472"]=new(){HarvestItemId="24",DaysInPhase=new(){1,2,3,4},Seasons=new(){Season.Spring},RegrowDays=7};
  ItemRegistry.AddTypeDefinition(new ObjectDataDefinition());
  var helper=(IModHelper)Recorder.Make(typeof(IModHelper));var monitor=(IMonitor)Recorder.Make(typeof(IMonitor));Recorder.PeerVersion=typeof(FertilizerService).Assembly.GetName().Version!.ToString(3);
  SharedPreferences.Register(helper,Config);Service=new(helper,monitor,"BSV.Farming.Tests");
  Check(FertilizerService.Ready,"Actual HoeDirt Harmony targets install on game 1.6.15");
 }
 static HoeDirt Dirt(int x=3,int y=3)
 {
  var soil=new HoeDirt();soil.Location=Farm;soil.Tile=new(x,y);Farm.terrainFeatures[soil.Tile]=soil;
  Host.Position=Guest.Position=new(x*64,y*64);FertilizerService.Initialize(soil);return soil;
 }
 static void Apply(HoeDirt soil,string id,Farmer? who=null)
 {
  who??=Host;Player(who);who.CurrentToolIndex=0;who.Items[0]=ItemRegistry.Create(id,5);
  bool native=soil.plant(id,who,true);
  Check(!native&&who.Items[0].Stack==4,"Native caller receives false; approved escrow alone spends one "+id);
  Service.Network.Tick();Service.Network.Tick();
  Check(!who.modData.ContainsKey(FertilizerNetwork.Pending)&&who.Items[0].Stack==4,"Commit acknowledged without native second spend "+id);
 }
 static void Probabilities()
 {
  var rng=new Random(20261019);
  for(int grade=0;grade<4;grade++)
  {
   int[] counts=new int[4];for(int i=0;i<100000;i++)counts[HarvestRules.YieldMultiplier(grade,rng.NextDouble(),new())]++;
   var p=HarvestRules.YieldChances(grade);double mean=(counts[1]+2d*counts[2]+3d*counts[3])/100000;
   Check(Math.Abs(counts[2]/100000d-p.Double)<.006&&Math.Abs(counts[3]/100000d-p.Triple)<.006&&Math.Abs(mean-(1+p.Double+2*p.Triple))<.012,"H01 100000 random harvests grade "+grade);
   Stats.Add(new{grade,ordinary=counts[1],twice=counts[2],triple=counts[3],mean,expected=1+p.Double+2*p.Triple});
   foreach(bool extra in new[]{false,true})foreach(bool f in new[]{false,true})
   {
    var o=new HarvestBonusOptions{Enabled=extra,EnableFertilizerYieldBonus=f};
    foreach(double u in new[]{0,.049999,.05,.1,.2,.3,.43,.55,.999999})Check(HarvestRules.YieldMultiplier(grade,u,o)==(extra?HarvestRules.YieldMultiplier(f?grade:0,u):1),$"H03 switches {grade}/{extra}/{f}/{u}");
   }
   Check(HarvestRules.YieldMultiplier(grade,Math.BitDecrement(p.Triple))==3&&HarvestRules.YieldMultiplier(grade,p.Triple)==2&&HarvestRules.YieldMultiplier(grade,p.Triple+p.Double)==1,"H02 exclusive interval boundaries "+grade);
  }
  var bad=new ModConfig();bad.HarvestBonus.DoubleChance=-1;bad.HarvestBonus.TripleChance=2;ConfigPolicy.Normalize(bad);
  Check(bad.HarvestBonus.DoubleChance==.2&&bad.HarvestBonus.TripleChance==.05,"H02 invalid probability pair falls back, never normalized");
  var custom=new ModConfig();custom.HarvestBonus.QualityDoubleChance=.1;custom.HarvestBonus.QualityTripleChance=.8;ConfigPolicy.Normalize(custom);
  Check(custom.HarvestBonus.QualityTripleChance==.8,"H02 valid custom final distribution retained");
 }
 static void Pure()
 {
  foreach(int d in new[]{-1,0,1,2,3,4,5,6,7,8,28})foreach(int g in new[]{0,1,2,3})
  {
   int expected=d<=3||g==0?d:g==1?(d>=7?d-1:d):g==2?d-1:d>=7?d-2:d-1;
   Check(FertilizerRules.Regrow(d,g,true)==expected&&FertilizerRules.Regrow(d,g,false)==d,$"S01 regrowth {d}/{g}");
   for(int round=0;round<5;round++)Check(FertilizerRules.Regrow(d,g,true)==expected,$"S04 stable original D {d}/{g}/{round}");
  }
  foreach(bool crab in new[]{false,true})
  {
   foreach(int uniform in new[]{2880,4320,1440})foreach(bool custom in new[]{false,true})
   {
    var raw=JObject.Parse("{\"CycleGameMinutes\":"+uniform+",\"UniformCycleWasCustomized\":"+custom.ToString().ToLowerInvariant()+",\"Foreign\":\"keep\"}");
    var migrated=MachineBalanceMigration.Prepare(raw,crab);int expected=uniform!=2880||custom?uniform:crab?5760:4320;
    Check(migrated.Value<int>("LevelTwoCycleGameMinutes")==expected&&migrated.Value<string>("Foreign")=="keep","M10 migration value and foreign fields "+crab+uniform+custom);
    Check(JToken.DeepEquals(migrated,MachineBalanceMigration.Prepare(migrated,crab)),"M10 migration idempotent");
   }
   var explicitLevel=MachineBalanceMigration.Prepare(JObject.Parse("{\"CycleGameMinutes\":2880,\"LevelTwoCycleGameMinutes\":6000}"),crab);
   Check(explicitLevel.Value<int>("LevelTwoCycleGameMinutes")==6000,"M10 explicit per-level value retained");
  }
  var fish=new BalanceConfig();var crabConfig=new CrabBalanceConfig();
  Check(Enumerable.Range(1,3).Select(fish.MinutesForLevel).SequenceEqual(new[]{2880,4320,5760})&&Enumerable.Range(1,2).Select(crabConfig.MinutesForLevel).SequenceEqual(new[]{2880,5760}),"M01 fresh level defaults");
  var roundTrip=JsonSerializer.Deserialize<BalanceConfig>(JsonSerializer.Serialize(fish))!;Check(roundTrip.MinutesForLevel(2)==4320,"M10 captured policy roundtrip preserves defaults");
  var legacy=JsonSerializer.Deserialize<BalanceConfig>("{\"CycleGameMinutes\":2880}")!;Check(legacy.MinutesForLevel(3)==2880,"M09 old saved policy keeps historical uniform duration");
  var ordered=Newtonsoft.Json.JsonConvert.DeserializeObject<BalanceConfig>("{\"LevelTwoCycleGameMinutes\":6000,\"CycleGameMinutes\":1440,\"LevelDurationVersion\":1}")!;
  Check(ordered.MinutesForLevel(2)==6000,"M10 explicit level values survive arbitrary JSON property ordering");
  foreach(bool trapper in new[]{false,true})Check(CrabRules.Craft(trapper).SequenceEqual(CrabRules.Craft(false))&&CrabRules.Upgrade(trapper).SequenceEqual(CrabRules.Upgrade(false)),"M07 Trapper full material parity");
  Check(CrabRules.Required(1,true)==75&&CrabRules.Required(2,true)==175&&CrabRules.Rolls(1)==150&&CrabRules.Rolls(2)==350,"M07 Luremaster bait and attempts independent");
  foreach(int level in new[]{1,2})foreach(int delta in new[]{-1,0,3})
  {
   int threshold=level==1?15:20;var state=new ProductionState{Level=level,CompletedCycles=100,LevelCycles=threshold+delta};if(level==1)state.CompletedCycles=threshold+delta;
   var material=MachineUpgrade.Requirements(level).SelectMany((r,i)=>r.Id.StartsWith("(T)")?Enumerable.Range(0,r.Count).Select(n=>new UpgradeSlot(i*10+n,r.Id,1,true)):new[]{new UpgradeSlot(i*10,r.Id,r.Count)}).ToArray();
   Check((MachineUpgrade.Plan(state,material)!=null)==(delta>=0),"M04 stage threshold "+level+"/"+delta);
   if(delta>=0)for(int i=0;i<material.Length;i++){var missing=material.Where((_,n)=>n!=i).ToArray();Check(MachineUpgrade.Plan(state,missing)==null,"M05 missing material returns no partial plan "+level+"/"+i);}
  }
  var recovered=new ProductionState{Level=2,CompletedCycles=119,LevelCycles=19,StageProgressUnverified=true,Phase="working",DueAt=100};
  recovered.Complete(100);Check(recovered.LevelCycles==20&&!recovered.StageProgressUnverified&&recovered.CompletedCycles==120,"M09 newly verified completions unlock unknown stage without lifetime credit or permanent block");
  Check(!MachineUpgrade.Requirements(2).Any(r=>r.Id.Contains("Advanced")),"M06 no advanced rod or mastery requirement");
  var unknown=ProductionState.Load("{\"Schema\":6,\"Level\":2,\"CompletedCycles\":100}");Check(unknown.StageProgressUnverified&&MachineUpgrade.Plan(unknown,Array.Empty<UpgradeSlot>())==null,"M09 missing stage history flagged without granting lifetime credit");
 }
 static void Dual()
 {
  foreach(string q in new[]{"(O)368","(O)369","(O)919"})foreach(string s in new[]{"(O)465","(O)466","(O)918"})foreach(bool reverse in new[]{false,true})
  {
   var soil=Dirt();string raw=string.Join(";",soil.modData.Keys.Select(k=>k+"="+soil.modData[k]));
   for(int i=0;i<5;i++)Check(soil.CanApplyFertilizer(q),"D02 preview allows quality");
   Check(raw==string.Join(";",soil.modData.Keys.Select(k=>k+"="+soil.modData[k])),"D02 preview is pure");
   Apply(soil,reverse?s:q);Apply(soil,reverse?q:s);
   Check(FertilizerService.Read(soil).Dual&&soil.GetFertilizerQualityBoostLevel()==FertilizerRules.Quality(q)
    &&Math.Abs(soil.GetFertilizerSpeedBoost()-(FertilizerRules.Speed(s) switch{1=>.1f,2=>.25f,_=>.33f}))<1e-6,"D01/D05 actual independent effects "+q+s+reverse);
   foreach(bool f in new[]{false,true})foreach(bool speed in new[]{false,true})
   {Config.HarvestBonus.EnableFertilizerYieldBonus=f;Config.EnableBsvSpeedGro=speed;Check(soil.GetFertilizerQualityBoostLevel()>0&&soil.GetFertilizerSpeedBoost()>0,"D07 both native effects remain "+f+speed);}
   Check(!soil.CanApplyFertilizer(q)&&!soil.CanApplyFertilizer(s)&&!soil.CanApplyFertilizer("(O)370"),"D02 duplicates and third slot rejected");
   var saved=JsonSerializer.Serialize(FertilizerService.Read(soil));var copy=new HoeDirt();copy.fertilizer.Value=soil.fertilizer.Value;copy.modData[FertilizerService.SlotKey]=soil.modData[FertilizerService.SlotKey];
   FertilizerService.Initialize(copy);FertilizerService.Initialize(copy);Check(JsonSerializer.Serialize(FertilizerService.Read(copy))==saved,"D03 two loads retain original dual identities");
   soil.fertilizer.Value=null;Check(!FertilizerService.Read(soil).Dual&&!soil.modData.ContainsKey(FertilizerService.SlotKey),"D04 native clear synchronously clears extension");
  }
  var unknown=Dirt();unknown.fertilizer.Value="(O)370";Check(!unknown.CanApplyFertilizer("(O)465")&&!unknown.CanApplyFertilizer("(O)368"),"D02 preserving retaining soil blocks unsupported dual mix");
  var late=Dirt();late.crop=new Crop();late.crop.currentPhase.Value=1;Check(!late.CanApplyFertilizer("(O)368")&&!late.CanApplyFertilizer("(O)369")&&late.CanApplyFertilizer("(O)919"),"D02 actual native sprouting restrictions retained");
 }
 static void Main()
 {
  Pure();Probabilities();Setup();Dual();NativeGrowth();Transactions();Harvests();ConfigFilesChecks();StorageChecks();NativeToolSafety();NativeHarvests();
  Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=Checks,probability=Stats,real_game_assembly=true,headless=true,live_game=false,multiplayer_session=false,gpu=false}));
 }
}

using BetterStardewValley;
using BetterStardewValley.AfkFishing;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Tools;
using StardewValley.Inventories;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Tools;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using xTile;
using xTile.Layers;
using xTile.Tiles;
using SObject=StardewValley.Object;
using Size=xTile.Dimensions.Size;
partial class Program
{
 static AfkFishingService Service=null!;static Farmer Who=null!;static GameLocation Map=null!;static FishingRod Rod=null!;
 static Dictionary<string,StardewValley.GameData.Crops.CropData> CropData=new();
 static Dictionary<string,string> Recipes=new();
 static bool HostTransactions;
 static bool HostMode(ref bool __result){__result=HostTransactions;return false;}
 static int DayValue=1;
 static int GameDay { get=>DayValue; set { DayValue=value;Game1.year=1;Game1.season=Season.Spring;Game1.dayOfMonth=value; } }
 static bool Date(ref WorldDate __result){__result=new WorldDate(1,"spring",GameDay);return false;}
 static int Checks,BeginCalls,ReleaseCalls,Pulls,XpCalls;static float LastPower;static bool Water=true;
 static readonly List<string> Names=new(),Notices=new();static readonly List<float> Powers=new();
 static readonly BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static object Session=>typeof(AfkFishingService).GetProperty("Current",Any)!.GetValue(Service)!;
 static AfkSave Data=>(AfkSave)Session.GetType().GetField("Data",Any)!.GetValue(Session)!;
 static Inventory Pending=>Who.team.GetOrCreateGlobalInventory(AfkFishingService.QueuePrefix+Who.UniqueMultiplayerID);
 static void Check(bool ok,string name){Checks++;if(!ok)throw new Exception(name);Names.Add(name);}
 static bool Skip()=>false;static bool Yes(ref bool __result){__result=true;return false;}static bool No(ref bool __result){__result=false;return false;}
 static bool Notice(string key){Notices.Add(key);return false;}
 static bool Bed(ref TileNode? __result){__result=new("HomeFixture",1,1);return false;}
 static bool Route(ref List<RouteStep>? __result){__result=new();return false;}
 static bool CanFish(ref bool __result){__result=Water;return false;}
 static bool Area(out string? id,out StardewValley.GameData.Locations.FishAreaData? data,ref bool __result)
 {id=null;data=null;__result=false;return false;}
 static bool Begin(FishingRod __instance,Farmer who,ref bool __result)
 {if(ObserveFirstCast)FirstCastChest=Field<Chest?>("Chest");BeginCalls++;__instance.lastUser=who;__instance.isTimingCast=true;who.UsingTool=true;who.CanMove=false;__result=true;return false;}
 static bool Start(FishingRod __instance)
 {ReleaseCalls++;LastPower=__instance.castingPower;Powers.Add(LastPower);__instance.isTimingCast=false;__instance.isCasting=true;return false;}
 static void CountXp(){XpCalls++;}
 static bool ContextTags(ref HashSet<string> __result){__result=new();return false;}
 sealed class EmptyServices:IServiceProvider { public object? GetService(Type type)=>null; }
 sealed class TestContent:LocalizedContentManager
 {
  public TestContent():base(new EmptyServices(),".") { }
  public override T Load<T>(string name)
  {
   name=name.Replace('\\','/');
   object value=name switch {
    "Data/Fish"=>new Dictionary<string,string>{{"128","Fixture fish/85/mixed/1/30/600 2600/summer/sunny/600 2600/6/0.3/0.3/0"}},
    "Data/Furniture"=>new Dictionary<string,string>(),
    "Strings/Locations"=>new Dictionary<string,string>{{"FarmHouse_Bed_GoToSleep","Sleep?"}},
    "Strings/StringsFromCSFiles"=>new Dictionary<string,string>(),
    "Data/Crops"=>CropData,
    "Data/Machines"=>new Dictionary<string,StardewValley.GameData.Machines.MachineData>(),
    "Data/CookingRecipes"=>Recipes,
    _=>throw new InvalidOperationException("Unconfigured content boundary: "+name)
   };return (T)value;
  }
 }
 static void Run(string name,params object?[] args)=>typeof(AfkFishingService).GetMethod(name,Any)!.Invoke(Service,args);
 static bool Capture(Item item)
 {
  object?[] args={Rod,false};Run("BeginOutput",args);
  try{return Who.addItemToInventoryBool(item);}
  finally{Run("EndOutput",Rod,args[1],null);}
 }
 static void Clock(double seconds){Game1.currentGameTime=new GameTime(TimeSpan.FromSeconds(seconds),TimeSpan.FromMilliseconds(16));}
 static void Ready(){Rod.isTimingCast=false;Rod.isCasting=false;Rod.isFishing=false;Rod.isNibbling=false;Rod.fishCaught=false;Rod.hit=false;Rod.isReeling=false;Rod.pullingOutOfWater=false;Rod.showingTreasure=false;Rod.castedButBobberStillInAir=false;Who.UsingTool=false;Who.CanMove=true;Who.isEating=false;Who.freezePause=0;}
 static void Reset()
 {
  var sessions=typeof(AfkFishingService).GetField("Sessions",Any)!.GetValue(Service)!;
  var property=sessions.GetType().GetProperty("Value")!;
  property.SetValue(sessions,Activator.CreateInstance(property.PropertyType,true));
  Who.Items.Clear();Who.MaxItems=12;Rod=new FishingRod(0){ItemId="BambooPole",PlayUseSounds=false};Who.Items.Add(Rod);
  while(Who.Items.Count<Who.MaxItems)Who.Items.Add(null);
  Who.CurrentToolIndex=0;Who.fishingLevel.Value=0;Who.Stamina=100;Who.experiencePoints[1]=20000;
  Map.objects.Clear();Who.team.globalInventories.Clear();Notices.Clear();BeginCalls=ReleaseCalls=0;Powers.Clear();Water=true;
  Game1.activeClickableMenu=null;
  Game1.timeOfDay=900;
  Who.currentLocation=Map;Game1.game1.instanceGameLocation=Map;
  typeof(Farmer).GetProperty("Position")!.SetValue(Who,new Vector2(5*64,5*64-16));
  Who.FacingDirection=1;
  Clock(10);
  Ready();
 }
 static void F8()
 {
  var ctor=typeof(ButtonPressedEventArgs).GetConstructors(Any).Single();
  var args=ctor.GetParameters().Select(p=>p.ParameterType==typeof(SButton)?(object)SButton.F8:p.ParameterType.IsInterface?Recorder.Make(p.ParameterType):p.HasDefaultValue?p.DefaultValue:p.ParameterType.Name=="SInputState"?FormatterServices.GetUninitializedObject(p.ParameterType):throw new Exception("Unrecognized input constructor parameter "+p)).ToArray();
  var e=(ButtonPressedEventArgs)ctor.Invoke(args);
  var handler=Recorder.Events.Single(x=>x.Name=="add_ButtonPressed"&&x.Handler.Method.DeclaringType==typeof(AfkFishingService)).Handler;
  handler.DynamicInvoke(null,e);
 }
 static void Main()
 {
  Game1.game1=(Game1)FormatterServices.GetUninitializedObject(typeof(Game1));Game1.game1._locations=new List<GameLocation>();Game1.netWorldState=new Netcode.NetRoot<StardewValley.Network.NetWorldState>(new StardewValley.Network.NetWorldState());Game1.random=new Random(118);Game1.otherFarmers=new();Game1._locationLookup=new(StringComparer.OrdinalIgnoreCase);
  GameDay=1;
  GameRunner.instance=(GameRunner)FormatterServices.GetUninitializedObject(typeof(GameRunner));GC.SuppressFinalize(GameRunner.instance);GameRunner.instance.gameInstances=new List<Game1>{Game1.game1};
  typeof(Context).GetProperty("IsWorldReady",Any)!.SetValue(null,true);
  var h=new Harmony("BSV.Afk.Hotfix.Boundaries");
  void Patch(Type t,string n,string callback,params Type[] p)=>h.Patch(t.GetMethod(n,Any,null,p,null)!,prefix:new(typeof(Program),callback));
  Patch(typeof(GameLocation),"updateMap",nameof(Skip));Patch(typeof(GameLocation),"InIslandContext",nameof(No));
  Patch(typeof(StardewValley.BellsAndWhistles.PlayerStatusList),"AddSpriteDefinition",nameof(Skip),typeof(string),typeof(string),typeof(int),typeof(int),typeof(int),typeof(int));
  Patch(typeof(Farmer),"farmerInit",nameof(Skip));Patch(typeof(AnimatedSprite),"LoadTexture",nameof(Skip),typeof(string),typeof(bool));
  Patch(typeof(Game1),"get_Date",nameof(Date));
  Patch(typeof(Utility),"getHomeOfFarmer",nameof(HomeBoundary),typeof(Farmer));
  Patch(typeof(Game1),"get_HostPaused",nameof(No));Patch(typeof(Game1),"get_IsMasterGame",nameof(HostMode));Patch(typeof(Game1),"isFestival",nameof(No));
  Patch(typeof(SButtonExtensions),"IsActionButton",nameof(No),typeof(SButton));Patch(typeof(SButtonExtensions),"IsUseToolButton",nameof(No),typeof(SButton));
  Patch(typeof(Context),"get_IsWorldReady",nameof(Yes));Patch(typeof(AfkFishingService),"get_SafeWorld",nameof(Yes));
  Patch(typeof(AfkFishingService),"Notice",nameof(Notice),typeof(string));
  Patch(typeof(AfkFishingService),"UpdateReturnBudget",nameof(Skip));
  Game1.realMilliSecondsPerGameTenMinutes=7000;
  Patch(typeof(AfkWorld),"Bed",nameof(Bed));Patch(typeof(AfkWorld),"Route",nameof(Route),typeof(TileNode),typeof(TileNode));
  Patch(typeof(GameLocation),"canFishHere",nameof(CanFish));
  Patch(typeof(GameLocation),"TryGetFishAreaForTile",nameof(Area),typeof(Vector2),typeof(string).MakeByRefType(),typeof(StardewValley.GameData.Locations.FishAreaData).MakeByRefType());
  Patch(typeof(FishingRod),"beginUsing",nameof(Begin),typeof(GameLocation),typeof(int),typeof(int),typeof(Farmer));
  Patch(typeof(FishingRod),"doStartCasting",nameof(Start));
  Patch(typeof(Farmer),"OnItemReceived",nameof(Skip),typeof(Item),typeof(int),typeof(Item),typeof(bool));
  ItemRegistry.AddTypeDefinition(new ObjectDataDefinition());ItemRegistry.AddTypeDefinition(new BigCraftableDataDefinition());ItemRegistry.AddTypeDefinition(new ToolDataDefinition());
  Game1.objectData=new Dictionary<string,ObjectData>();
  foreach(string id in new[]{"128","388","774"})Game1.objectData[id]=new(){Name=id,DisplayName=id,Type="Basic",Category=id=="128"?-4:0,Texture="fixture",SpriteIndex=0,Price=100};
  Game1.bigCraftableData=new Dictionary<string,StardewValley.GameData.BigCraftables.BigCraftableData>{{"130",new(){Name="Chest",DisplayName="Chest",Texture="fixture",SpriteIndex=0}}};
  Game1.toolData=new Dictionary<string,ToolData>{{"BambooPole",new(){ClassName="FishingRod",Name="Bamboo Pole",DisplayName="Bamboo Pole",Texture="fixture",SpriteIndex=0}}};
  Map=new GameLocation();Map.name.Value="FishingFixture";Map.map=Fixture();Game1.locations.Add(Map);Game1.game1.instanceGameLocation=Map;
  Who=new Farmer();Who.buffs.SetOwner(Who);Who.buffs.Dirty=false;typeof(Game1).GetField("_player",Any)!.SetValue(null,Who);
  Game1.serverHost=new StardewValley.Network.NetFarmerRoot(Who);
  Service=new AfkFishingService((IModHelper)Recorder.Make(typeof(IModHelper)),(IMonitor)Recorder.Make(typeof(IMonitor)),(IManifest)Recorder.Make(typeof(IManifest)),new ModConfig());
  Reset();F8();Console.Error.WriteLine($"R9 startup: enabled={Data.Enabled} begin={BeginCalls} notices={string.Join(",",Notices)} local={Who.IsLocalPlayer} world={Context.IsWorldReady} move={Who.CanMove} phase={Data.Phase}");Check(Data.Enabled&&BeginCalls==1,"F8 enables AFK without a chest and without a previously cast rod");
  Check(ReleaseCalls==1&&LastPower==1f&&Rod.isCasting,"first F8 cast releases native event at MAX=1");
  for(int i=0;i<5;i++){Ready();Clock(20+i*3);Run("Tick");}
  Check(Powers.Count==6&&Powers.All(p=>p==1),"every later cast releases at MAX");
  F8();Check(!Data.Enabled,"second F8 disables AFK via the registered button handler");
  Reset();var prior=new SObject("388",17);Who.Items[0]=prior;Who.Items[4]=Rod;Rod.attachments[0]=new SObject("774",7);Rod.modData["fixture"]="kept";F8();
  Check(Data.Enabled&&Who.CurrentToolIndex==4&&ReferenceEquals(Who.Items[0],prior)&&prior.Stack==17&&ReferenceEquals(Who.Items[4],Rod)&&Rod.attachments[0]!.Stack==7&&Rod.modData["fixture"]=="kept","unequipped existing rod selected without replacing items, bait or metadata");
  Reset();Who.Items[0]=null;F8();Check(!Data.Enabled&&BeginCalls==0&&Notices.Contains("afk.no-rod"),"no rod refuses start with localized notice; no rod is generated");
  Reset();var chest=new Chest(true);Map.objects[new(5,6)]=chest;F8();
  Check(Data.Enabled&&ReferenceEquals(Session.GetType().GetField("Chest",Any)!.GetValue(Session),chest)&&BeginCalls==1,"nearby eligible chest connects but does not delay activation");
  Map.objects.Clear();Ready();Clock(20);Run("Tick");Check(Data.Enabled&&BeginCalls==2,"removing connected chest does not disable or pause casting");
  Reset();Map.objects[new(5,16)]=new Chest(true);F8();Check(Data.Enabled&&Session.GetType().GetField("Chest",Any)!.GetValue(Session)==null&&BeginCalls==1,"chest outside centered 3x3 does not connect or block F8");
  Reset();for(int i=1;i<Who.Items.Count;i++)Who.Items[i]=new SObject("388",999);F8();
  Check(Data.Enabled&&Data.Phase==AfkPhase.PausedStorageFull&&BeginCalls==0,"no chest and full backpack safely pauses before new cast");
  Reset();F8();for(int i=1;i<Who.Items.Count;i++)Who.Items[i]=new SObject("388",999);
  var output=new SObject("128",2,quality:2);output.modData["catch"]="unchanged";
  Check(Capture(output)&&Pending.Count==1,"in-flight catch crosses real production capture hook into durable pending inventory");
  Ready();Clock(14);Run("Tick");
  Check(Pending.Count==1&&ReferenceEquals(Pending[0],output)&&output.Stack==2&&BeginCalls==1,"full inventory retains exact pending fish identity, quantity and metadata");
  Who.Items[1]=null;Clock(18);Run("Tick");
  Check(Pending.Count==0&&Who.Items.Any(i=>i?.QualifiedItemId=="(O)128"&&i.Stack==2&&i.Quality==2&&i.modData["catch"]=="unchanged")&&BeginCalls==2,"freeing backpack space drains once then automatically resumes MAX cast");
  Ready();Clock(22);Run("Tick");Check(Who.Items.Where(i=>i?.QualifiedItemId=="(O)128").Sum(i=>i.Stack)==2,"resumed cycle never duplicates drained pending fish");
  Reset();F8();Ready();Who.Stamina=0;Clock(20);Run("Tick");Check(Data.Enabled&&Data.Phase==AfkPhase.ReturningHome,"no chest and exhaustion still begins automatic trip home");
  GameDay++;Run("NewDay");Check(Data.Enabled&&Data.Phase==AfkPhase.WakingUp&&Data.WakePending,"next day preserves AFK and defers return until world initialization finishes");
  Run("ResumeAfterWake");Run("ResumeAfterWake");
  Data.Phase=AfkPhase.Fishing;Who.Stamina=100;Ready();Clock(24);Run("Tick");Check(LastPower==1&&BeginCalls==2,"resuming after the next-day boundary still uses maximum casting power");
  Reset();F8();Ready();Game1.timeOfDay=2430;Clock(20);Run("Tick");Check(Data.Phase==AfkPhase.ReturningHome&&BeginCalls==1,"existing 00:30 return takes priority and never begins another cast");
  Reset();Water=false;F8();Check(!Data.Enabled&&BeginCalls==0&&Notices.Contains("afk.invalid-cast"),"invalid facing water never produces a fabricated cast or catch");
  Reset();F8();Ready();Water=false;Clock(20);Run("Tick");Check(Data.Enabled&&Data.Phase==AfkPhase.WaitingForCast&&BeginCalls==1,"water invalidated during AFK pauses safely without casting");
  Water=true;Clock(23);Run("Tick");Check(Data.Enabled&&BeginCalls==2&&LastPower==1,"valid water restored resumes at MAX");
  Reset();chest=new Chest(true);for(int i=0;i<36;i++)chest.Items.Add(new SObject("388",999));Map.objects[new(5,6)]=chest;F8();
  output=new SObject("128",1);Check(Capture(output)&&Who.Items.Contains(output)&&Pending.Count==0&&chest.Items.Sum(i=>i.Stack)==36*999,
   "full optional chest cannot affect catch already accepted into backpack");
  Reset();
  foreach(var level in new[]{(0,0),(1,1),(4,2),(8,3),(15,4)})
  {
   Who.fishingLevel.Value=level.Item1;
   for(int facing=0;facing<4;facing++)
   {
    var stand=new Point(16*64+32,12*64+32);
    float distance=(level.Item2+(facing is 1 or 3?4:3))*64-(facing is 1 or 3?8:0);
    var expected=new Point((int)((stand.X+(facing==1?distance:facing==3?-distance:0))/64),
     (int)((stand.Y+(facing==2?distance:facing==0?-distance:0))/64));
    Check(AfkFishingService.TryMaximumLanding(Map,stand,facing,Rod,Who,out var actual,out _)&&actual==expected,
     "MAX landing uses native rod distance at Fishing Level "+level.Item1+", facing "+facing);
   }
  }
  BehaviourChecks(h,Patch);
  FoodChecks(h,Patch);
  Food129Checks(h);
  NineTileChecks(h);
  Shore128Checks(h);
  Route120Checks();
  Rest121Checks(h);
  Movement122Checks(h);
  ObstacleR12Checks();
  PerfectChecks(h,Patch);
  Morning127Checks(h);
  RecoveryR9Checks();
  Compatibility131Checks();
  h.UnpatchAll(h.Id);new Harmony("BSV.Afk.Hotfix.Tests.AfkFishing").UnpatchAll("BSV.Afk.Hotfix.Tests.AfkFishing");
  LobsterBait139Checks();
  Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=Checks,scenarios=Names,production_afk_source=true,native_inventory_and_perfect_xp=true,headless_boundaries="synthetic maps/data; selected activation preflights; legal warp transport, emergency warp effect, sleep/food effects, rod launch graphics, HUD, item notifications, post-XP landing graphics; native walking/collision/animation, mutex and Perfect settlement execute; see TEST_REPORT",live_game=false,real_multiplayer=false}));
 }
 sealed class LandingGraphicsEnd:Exception { }
 static bool InLanding;
 static bool GraphicsBoundary(){if(InLanding)throw new LandingGraphicsEnd();return true;}
 static bool ExitMenu(){Game1.activeClickableMenu=null;return false;}
 static bool NativePull(FishingRod __instance,string fishId,int fishSize,int fishQuality,int fishDifficulty,bool treasureCaught,bool wasPerfect,bool fromFishPond,string setFlagOnCatch,bool isBossFish,int numCaught)
 {
  Pulls++;
  // Exercise the exact native event receiver and gainExperience. Only the network envelope
  // and the graphics AFTER quality/XP are boundaries in this headless fixture.
  using var bytes=new MemoryStream();using(var w=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))
  {w.Write(fishId);w.Write(fishSize);w.Write(fishQuality);w.Write(fishDifficulty);w.Write(treasureCaught);w.Write(wasPerfect);w.Write(fromFishPond);w.Write(setFlagOnCatch??"");w.Write(isBossFish);w.Write(numCaught);}
  bytes.Position=0;using var reader=new BinaryReader(bytes);InLanding=true;
  try{typeof(FishingRod).GetMethod("doPullFishFromWater",Any)!.Invoke(__instance,new object[]{reader});}
  catch(TargetInvocationException e) when(e.InnerException is LandingGraphicsEnd){}
  finally{InLanding=false;}
  return false;
 }
 static void PerfectChecks(Harmony h,Action<Type,string,string,Type[]> patch)
 {
  Reset();Rod.lastUser=Who;
  // Native BobberBar constructor still chooses quality, Training Rod override and bobbers.
  // Its base UI initialization/rendering is not needed for the native successful branch.
  var menuCtor=typeof(IClickableMenu).GetConstructor(new[]{typeof(int),typeof(int),typeof(int),typeof(int),typeof(bool)})!;
  h.Patch(menuCtor,prefix:new(typeof(Program),nameof(Skip)));
  patch(typeof(BobberBar),"Reposition",nameof(Skip),Type.EmptyTypes);
  Game1.content=new TestContent();
  patch(typeof(ItemContextTagManager),"GetBaseContextTags",nameof(ContextTags),new[]{typeof(string)});
  patch(typeof(Game1),"exitActiveMenu",nameof(ExitMenu),Type.EmptyTypes);
  patch(typeof(Game1),"setRichPresence",nameof(Skip),new[]{typeof(string),typeof(object)});
  patch(typeof(FishingRod),"pullFishFromWater",nameof(NativePull),new[]{typeof(string),typeof(int),typeof(int),typeof(int),typeof(bool),typeof(bool),typeof(bool),typeof(string),typeof(bool),typeof(int)});
  h.Patch(typeof(Farmer).GetMethod("gainExperience",new[]{typeof(int),typeof(int)})!,prefix:new(typeof(Program),nameof(CountXp)));
  var metadataType=typeof(FishingRod).GetField("whichFish",Any)!.FieldType;
  h.Patch(metadataType.GetMethod("GetParsedOrErrorData",Type.EmptyTypes)!,prefix:new(typeof(Program),nameof(GraphicsBoundary)));
  foreach(var row in new[]{(0.1f,0,0),(0.5f,1,2),(0.9f,2,4)})
  {
   Rod.UpgradeLevel=0;var bar=new BobberBar("128",row.Item1,false,new(),"",false);
   Check(bar.fishQuality==row.Item2,"native BobberBar initial quality "+row.Item2);
   Game1.activeClickableMenu=bar;int before=Who.experiencePoints[1],calls=XpCalls,pulls=Pulls;
   AfkFishingService.CompleteMinigame(bar,Game1.currentGameTime);
   Check(bar.perfect&&bar.handledFishResult&&Game1.activeClickableMenu==null,"Perfect success closes minigame before rendering for base quality "+row.Item2);
   Check(Rod.fishQuality==row.Item3,"native Perfect quality "+row.Item2+" -> "+row.Item3);
   int basic=(row.Item2+1)*3+85/3;int expected=basic+(int)(basic*1.4f);
   Check(Who.experiencePoints[1]-before==expected&&XpCalls==calls+1,"native Perfect XP mutates real farmer experience once for base quality "+row.Item2);
   AfkFishingService.CompleteMinigame(bar,Game1.currentGameTime);
   Check(Pulls==pulls+1&&XpCalls==calls+1&&Who.experiencePoints[1]==before+expected,"duplicate resolution cannot duplicate fish landing or XP");
  }
  var quality=new BobberBar("128",.9f,false,new(){"(O)877"},"",false);
  Check(quality.fishQuality==4,"native Quality Bobber constructor produces iridium before completion");
  AfkFishingService.CompleteMinigame(quality,Game1.currentGameTime);Check(Rod.fishQuality==4,"native iridium stays iridium with Perfect");
  var qualityNormal=new BobberBar("128",.1f,false,new(){"(O)877"},"",false);
  Check(qualityNormal.fishQuality==1,"native Quality Bobber promotes normal base quality to silver");
  AfkFishingService.CompleteMinigame(qualityNormal,Game1.currentGameTime);Check(Rod.fishQuality==2,"Quality Bobber then native Perfect promotes silver to gold once");
  Rod.UpgradeLevel=1;var training=new BobberBar("128",.9f,false,new(){"(O)877"},"",false);
  Check(training.fishQuality==0&&training.beginnersRod,"native Training Rod override wins over input size and bobber");
  AfkFishingService.CompleteMinigame(training,Game1.currentGameTime);Check(Rod.fishQuality==0,"Training Rod remains normal quality under native Perfect rules");
  Rod.UpgradeLevel=0;
  var manual=new BobberBar("128",.5f,false,new(),"",false);manual.perfect=false;Game1.activeClickableMenu=manual;
  int beforePulls=Pulls;Data.Enabled=false;Run("AfterMinigameCreation",Rod);
  Check(!manual.handledFishResult&&!manual.perfect&&Game1.activeClickableMenu==manual&&Pulls==beforePulls,"manual fishing remains untouched by AFK postfix when disabled");
  Data.Enabled=true;var otherRod=new FishingRod();Run("AfterMinigameCreation",otherRod);
  Check(!manual.handledFishResult&&Pulls==beforePulls,"another rod/player's minigame is outside AFK ownership");
  Game1.activeClickableMenu=null;Data.Enabled=false;
 }
 static Map Fixture()
 {
  var map=new Map("FishingFixture");var sheet=new TileSheet("fixture",map,"not-rendered",new Size(2,2),new Size(16,16));map.AddTileSheet(sheet);
  var back=new Layer("Back",map,new Size(32,24),new Size(16,16));var walls=new Layer("Buildings",map,new Size(32,24),new Size(16,16));map.AddLayer(back);map.AddLayer(walls);
  for(int y=0;y<24;y++)for(int x=0;x<32;x++){back.Tiles[x,y]=new StaticTile(back,sheet,BlendMode.Alpha,0);if(x>=8)back.Tiles[x,y].Properties["Water"]="T";}
  return map;
 }
}
public class Recorder:DispatchProxy
{
 public static HashSet<string> LoadedMods=new();
 public static Dictionary<string,Action<string,string[]>> Commands=new();
 public static List<(string Path,object Data)> JsonWrites=new();
 public static List<(string Name,Delegate Handler)> Events=new();private Dictionary<Type,object> Values=new();
 public static object Make(Type t)=>typeof(DispatchProxy).GetMethods().Single(m=>m.Name=="Create"&&m.IsGenericMethodDefinition).MakeGenericMethod(t,typeof(Recorder)).Invoke(null,null)!;
 protected override object? Invoke(MethodInfo? method,object?[]? args)
 {
  var m=method!;if(m.Name=="Add"&&args?.Length==3&&args[2] is Action<string,string[]> command){Commands[(string)args[0]!]=command;return null;}
  if(m.Name=="WriteJsonFile"){JsonWrites.Add(((string)args![0]!,args[1]!));return null;}
  if(m.Name=="Log"){Console.Error.WriteLine(args![0]);return null;}if(m.Name.StartsWith("add_")&&args?[0] is Delegate d){Events.Add((m.Name,d));return null;}
  if(m.Name=="IsLoaded")return LoadedMods.Contains((string)args![0]!);
  if(m.Name=="get_UniqueID")return "BSV.Afk.Hotfix.Tests";
  if(m.Name=="get_Version")return new SemanticVersion("1.1.8");
  if(m.Name=="get_Locale")return NativeTranslationFixture.Locale;
  if(m.Name=="Get"&&m.DeclaringType==typeof(ITranslationHelper))return NativeTranslationFixture.Get((string)args![0]!,args.Length>1?args[1]:null);
  if(m.Name=="GetInAllLocales")return NativeTranslationFixture.All((string)args![0]!);
  var t=m.ReturnType;if(t==typeof(void))return null;
  if(t.IsInterface){if(!Values.TryGetValue(t,out var value))Values[t]=value=Make(t);return value;}
  return t.IsValueType?Activator.CreateInstance(t):null;
 }
}

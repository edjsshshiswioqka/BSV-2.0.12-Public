// A — minimal headless API doubles. No game/SMAPI implementations copied. Not a real game test.
using Microsoft.Xna.Framework;
namespace Microsoft.Xna.Framework { public record struct Vector2(float X,float Y); }
namespace HarmonyLib
{
 public sealed class Harmony { public string Id;public Harmony(string id)=>Id=id;public void Patch(object? a,HarmonyMethod? prefix=null){} public void UnpatchAll(string id){} public static bool ForeignPatch;public static PatchInfo? GetPatchInfo(object? method)=>ForeignPatch?new():null; }
 public sealed class PatchInfo {public IEnumerable<string> Owners=>new[]{"WeatherExample"};}
 public sealed class HarmonyMethod{public HarmonyMethod(Type type,string method){}}
 public static class AccessTools { public static object? Method(Type type,string name)=>null; }
}
namespace StardewModdingAPI
{
 public static class Context{public static bool IsWorldReady=true,IsMainPlayer=true,IsSplitScreen;}
 public enum LogLevel{Info,Warn,Error}
 public class IMonitor {public List<string> LogLines=new();public void Log(string s,LogLevel l)=>LogLines.Add(s);}
 public class SemanticVersion {public override string ToString()=>"1.1.2";}
 public class IManifest {public string UniqueID=>"Eddie.BetterStardewValley";public SemanticVersion Version=>new();}
 public class ModInfo {public SemanticVersion Version=>new();}
 public class Peer {public ModInfo? GetMod(string id)=>new();}
 public class Multiplayer {public List<(object Payload,string Type,long[] Players)> Messages=new();public Peer? GetConnectedPlayer(long id)=>new();public void SendMessage<T>(T msg,string type,string[] mods,long[] players)=>Messages.Add((msg!,type,players));}
 public class Loop
 {
  public event EventHandler<Events.UpdateTickedEventArgs>? UpdateTicked;public event EventHandler? ReturnedToTitle,SaveLoaded,DayStarted;
  public void Tick()=>UpdateTicked?.Invoke(null,new());public void Reset()=>ReturnedToTitle?.Invoke(null,EventArgs.Empty);public void Day()=>DayStarted?.Invoke(null,EventArgs.Empty);public void Load()=>SaveLoaded?.Invoke(null,EventArgs.Empty);
 }
 public class MultiplayerEvents{public event EventHandler<Events.ModMessageReceivedEventArgs>? ModMessageReceived;public void Send(object payload,long actor,string type,string mod="Eddie.BetterStardewValley")=>ModMessageReceived?.Invoke(null,new(){Payload=payload,FromPlayerID=actor,Type=type,FromModID=mod});}
 public class EventsHub {public Loop GameLoop=new();public MultiplayerEvents Multiplayer=new();}
 public class Commands {public Dictionary<string,Action<string,string[]>> Entries=new();public void Add(string name,string description,Action<string,string[]> action)=>Entries[name]=action;}
 public class IModHelper {public EventsHub Events=new();public Commands ConsoleCommands=new();public Multiplayer Multiplayer=new();}
}
namespace StardewModdingAPI.Events
{
 public class UpdateTickedEventArgs:EventArgs {public bool IsMultipleOf(uint n)=>true;}
 public class ModMessageReceivedEventArgs:EventArgs {public string FromModID="",Type="";public long FromPlayerID;public object Payload=null!;public T ReadAs<T>()=>(T)Payload;}
}
namespace StardewValley.GameData.Machines
{
 public enum MachineOutputTrigger {ItemPlacedInMachine,OutputCollected}
 public class TriggerEntry{public MachineOutputTrigger Trigger;}
 public class Output{public string? OutputMethod;}
 public class Rule{public bool RecalculateOnCollect;public List<TriggerEntry>? Triggers;public List<Output>? OutputItem;}
 public class MachineData {public bool AllowLoadWhenFull,IsIncubator;public string? InteractMethod,ExperienceGainOnHarvest;public List<string>? StatsToIncrementWhenHarvested;public List<Rule>? OutputRules;}
}
namespace StardewValley
{
 public class Field<T>{public T Value=default!;public Field(T value)=>Value=value;}
 public class NetMap<K,V>:Dictionary<K,V> where K:notnull {public IEnumerable<KeyValuePair<K,V>> Pairs=>this;}
 public class Data:Dictionary<string,string>
 {
  public static int FailAt=-1,Writes;public new string this[string k]{get=>base[k];set{base[k]=value;if(++Writes==FailAt)throw new InvalidOperationException("injected after-write fault");}}
 }
 public class Item{public string QualifiedItemId="";public int Stack=1;}
 public class Object:Item
 {
  public Vector2 TileLocation;public string DisplayName="Totem";public Field<bool> readyForHarvest=new(false);public Field<Object?> heldObject=new(null);
  public int MinutesUntilReady;public GameData.Machines.MachineData Machine=new();public Data modData=new();
  public GameData.Machines.MachineData GetMachineData()=>Machine;public bool checkForAction(Farmer who,bool justCheckingForActivity=false)=>false;
 }
 public class Crop
 {
  public Field<bool> dead=new(false),forageCrop=new(false),fullyGrown=new(false);public Field<string> netSeedIndex=new("472"),indexOfHarvest=new("24");
  public Field<int> currentPhase=new(0),dayOfCurrentPhase=new(0);public List<int> phaseDays=new(){2,3,5,99999};public Data modData=new();public bool Wild;public object? GetData()=>new();public bool isWildSeedCrop()=>Wild;
 }
 public class GameLocation
 {
  public NetMap<Vector2,Object> objects=new();public NetMap<Vector2,TerrainFeatures.TerrainFeature> terrainFeatures=new();public Data modData=new();public string GetLocationContextId()=>"Default";
  public void createQuestionDialogue(string question,Response[] answers,Action<Farmer,string> action){}
 }
 public class Response {public Response(string key,string text){}}
 public class HUDMessage {public HUDMessage(string text,int type){}}
 public class WorldDate {public int TotalDays,DayOfMonth=10,Season;public WorldDate(){}public WorldDate(WorldDate d){TotalDays=d.TotalDays;DayOfMonth=d.DayOfMonth;Season=d.Season;}}
 public class Friendship {public bool Engaged;public WorldDate? WeddingDate;public bool IsEngaged()=>Engaged;}
 public class Team {public Dictionary<(long,long),Friendship> friendshipData=new();}
 public class Farmer
 {
  public long UniqueMultiplayerID;public GameLocation currentLocation=null!;public Vector2 Tile;public Dictionary<string,Friendship> friendshipData=new();public Team team=new();public List<Item> Items=new();public void addItemByMenuIfNecessary(Item item)=>Items.Add(item);
 }
 public class Weather {public string WeatherForTomorrow="Sun";}
 public class World {public string WeatherForTomorrow="Sun";public Weather Regional=new();public Weather GetWeatherForLocation(string key)=>Regional;}
 public static class Game1
 {
  public static int year=1,season;public static WorldDate Date=new(){TotalDays=10};public static GameLocation Farm=new();public static Farmer player=new(){UniqueMultiplayerID=1},MasterPlayer=player;
  public static List<Farmer> Players=new();public static Field<World> netWorldState=new(new());public static string weatherForTomorrow="Sun";public static object? activeClickableMenu;public static bool eventUp;
  public static string? ForcedWeather;public static string getWeatherModificationsForDate(WorldDate date,string forecast)=>ForcedWeather??forecast;
  public static GameLocation getFarm()=>Farm;public static GameLocation currentLocation=>player.currentLocation;public static IEnumerable<Farmer> getAllFarmers()=>Players;public static IEnumerable<Farmer> getOnlineFarmers()=>Players;
  public static void showRedMessage(string s){}public static void drawObjectDialogue(string s){}public static void addHUDMessage(HUDMessage message){}
 }
 public static class Utility {public static bool Festival;public static bool TryGetPassiveFestivalDataForDay(int d,int s,string c,out string id,out object data){id="festival";data=new();return Festival;}}
 public static class ItemRegistry {public static Item Create(string id,int count=1)=>new(){QualifiedItemId=id,Stack=count};}
}
namespace StardewValley.TerrainFeatures
{
 public class TerrainFeature{}
 public class HoeDirt:TerrainFeature {public StardewValley.Crop crop=new();public int Fertilizer;public int GetFertilizerQualityBoostLevel()=>Fertilizer;}
}
namespace StardewValley.Objects
{
 public class Mutex
 {
  public bool Locked,Held,Delayed;private Action? Callback;public bool IsLocked()=>Locked;public bool IsLockHeld()=>Held;
  public void Update(StardewValley.GameLocation l){if(Callback!=null&&!Delayed){var f=Callback;Callback=null;Locked=Held=true;try{f();}finally{Locked=Held=false;}}}
  public void RequestLock(Action f)=>Callback=f;public void ReleaseLock(){Held=Locked=false;}
 }
 public class Chest:StardewValley.Object {public Mutex Mutex=new();public Mutex GetMutex()=>Mutex;public List<StardewValley.Item> Items=new();public bool Footprint=true;}
 public class IndoorPot:StardewValley.Object {public StardewValley.Field<StardewValley.TerrainFeatures.HoeDirt> hoeDirt=new(new());}
}
namespace BetterStardewValley
{
 internal static class BsvI18n {internal static string Get(StardewModdingAPI.IModHelper h,string key,object? tokens=null)=>key;}
 internal static class IntegrationHost {internal static bool Saving;internal static bool CanMutate=>StardewModdingAPI.Context.IsMainPlayer&&StardewModdingAPI.Context.IsWorldReady&&!Saving;}
}
namespace BetterStardewValley.Transactions
{
 internal static class NetworkManager {internal static bool Live(StardewValley.GameLocation l,StardewValley.Object o)=>l.objects.TryGetValue(o.TileLocation,out var live)&&ReferenceEquals(live,o);}
}
namespace BetterStardewValley.Totems {internal class TotemArt{internal TotemArt(StardewModdingAPI.IModHelper h,HarmonyLib.Harmony patcher){}}}
namespace Eddie.FishingMachine
{
 internal class ModEntry
 {
  internal static ModEntry Instance=new();internal bool Ready=true,Saving;internal ProductionService Production=new();
  internal static bool IsAnchor(StardewValley.Item obj)=>obj is StardewValley.Objects.Chest&&obj.QualifiedItemId is "(BC)Eddie.FishingMachine_Machine" or "(BC)Eddie.BetterStardewValley_CrabCatcher";
 }
 internal sealed partial class ProductionService
 {
  internal const string StateKey="cycle";private ModEntry Mod=>ModEntry.Instance;internal static long Now=1000;internal int InventoryWrites,Commits;
  private Dictionary<StardewValley.Objects.Chest,object> Cache=new();
  internal Core.ProductionState Read(StardewValley.Objects.Chest c)=>Core.ProductionState.Load(c.modData[StateKey]);
  private bool CompleteFootprint(StardewValley.Objects.Chest c)=>c.Footprint;
  private void Commit(StardewValley.Objects.Chest c,object? inventory,Core.ProductionState s){if(inventory!=null)InventoryWrites++;Commits++;c.modData[StateKey]=s.Save();}
 }
}

// Test boundary only: lighting is exercised with actual LightSource in AutomationNativeChecks.
namespace BetterStardewValley.Totems {internal class TotemLighting{internal TotemLighting(StardewModdingAPI.IModHelper h,StardewModdingAPI.IMonitor m){}}}
namespace BetterStardewValley {internal static class WineRules {internal const string BsvKeg="(BC)Eddie.BetterStardewValley_Keg";}}

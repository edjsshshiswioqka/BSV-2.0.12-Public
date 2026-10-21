// A — intentionally small API doubles. Tests execute the actual BSV service and state machine.
namespace HarmonyLib
{
 public class Harmony { public string Id; public Harmony(string id)=>Id=id; public void Patch(object? target,HarmonyMethod? prefix=null,HarmonyMethod? postfix=null){} public void UnpatchAll(string id){} }
 public class HarmonyMethod { public HarmonyMethod(Type t,string m){} }
 public static class AccessTools { public static object? Method(Type t,string m)=>null; }
}
namespace StardewModdingAPI
{
 public static class Context { public static bool IsWorldReady=true; }
 public enum LogLevel { Info,Error }
 public class IMonitor { public List<string> Lines=new(); public void Log(string s,LogLevel l)=>Lines.Add(s); }
 public class IManifest { public string UniqueID="BSV.Test"; }
 public class Loop
 {
  public event EventHandler? OneSecondUpdateTicked,ReturnedToTitle,SaveLoaded,DayStarted;
  public void Tick()=>OneSecondUpdateTicked?.Invoke(null,EventArgs.Empty);
  public void Reset()=>ReturnedToTitle?.Invoke(null,EventArgs.Empty);
  public void Load()=>SaveLoaded?.Invoke(null,EventArgs.Empty);
  public void Day()=>DayStarted?.Invoke(null,EventArgs.Empty);
 }
 public class ContentEvents { public event EventHandler<Events.AssetRequestedEventArgs>? AssetRequested; public event EventHandler? AssetsInvalidated;public void Load(Events.AssetRequestedEventArgs e)=>AssetRequested?.Invoke(null,e);public void Clear()=>AssetsInvalidated?.Invoke(null,EventArgs.Empty); }
 public class EventsHub { public Loop GameLoop=new();public ContentEvents Content=new(); }
 public class Commands { public Dictionary<string,Action<string,string[]>> Items=new();public void Add(string name,string help,Action<string,string[]> callback)=>Items[name]=callback; }
 public class IModHelper { public EventsHub Events=new();public Commands ConsoleCommands=new(); }
}
namespace StardewModdingAPI.Events
{
 public class AssetName { public string Text="";public bool IsEquivalentTo(string other)=>Text==other; }
 public class Dict<K,V> where K:notnull { public Dictionary<K,V> Data=new(); }
 public class Asset { private Dictionary<Type,object> Values=new();public Dict<K,V> AsDictionary<K,V>() where K:notnull {var t=typeof(Dict<K,V>);if(!Values.ContainsKey(t))Values[t]=new Dict<K,V>();return (Dict<K,V>)Values[t];} }
 public class AssetRequestedEventArgs:EventArgs { public AssetName NameWithoutLocale=new();public Asset Data=new();public void Edit(Action<Asset> edit)=>edit(Data); }
}
namespace StardewValley
{
 public class Net<T> { public T Value;public Net(T value)=>Value=value; }
 public class Item { public string QualifiedItemId="";public int Stack=1; }
 public class Object:Item { public const int artisanGoodsCategory=-26,highQuality=2;public Net<bool> bigCraftable=new(false),questItem=new(false);public int Quality;public string ItemId=>QualifiedItemId.Replace("(O)",""); }
 public class Farmer
 {
  public long UniqueMultiplayerID=1;public bool IsLocalPlayer=true;public Net<int> farmingLevel=new(0);
  public List<Item> Items=new(),displayedShippedItems=new();public List<string> mailbox=new(),mailReceived=new(),mailForTomorrow=new();
  public Dictionary<string,int> basicShipped=new(),craftingRecipes=new();public Dictionary<string,string> modData=new();
  public void OnItemReceived(Item item,int countAdded,Item mergedIntoStack,bool hideHudNotification=false){}
 }
 public class GameLocation {public Dictionary<string,string> modData=new();}
 public class DateData { public int TotalDays; }
 public class ObjectData { public int Category; }
 public class CropData { public string HarvestItemId=""; }
 public static class DataLoader { public static Dictionary<string,CropData> Data=new();public static Dictionary<string,CropData> Crops(object content)=>Data; }
 public static class Game1 {public static Farmer player=new();public static GameLocation Farm=new();public static DateData Date=new();public static bool eventUp;public static object content=new();public static Dictionary<string,ObjectData> objectData=new();public static GameLocation getFarm()=>Farm;}
}
namespace StardewValley.Menus {public class ShippingMenu {public void parseItems(IList<StardewValley.Item> items){}}}
namespace BetterStardewValley
{
 internal static class IntegrationHost {internal static bool Saving;}
 internal static class BsvI18n {internal static Dictionary<string,string> Text=new();internal static string Get(StardewModdingAPI.IModHelper h,string key,object? tokens=null)=>Text.GetValueOrDefault(key,key);}
}

// Headless boundary doubles; tests compile the production feature itself against these.
using System.Reflection;
namespace HarmonyLib {
 public class Harmony { public Harmony(string id){} public void Patch(MethodInfo m,HarmonyMethod? prefix=null){} public void UnpatchAll(string id){} }
 public class HarmonyMethod { public int priority; public HarmonyMethod(Type t,string n){} }
 public static class Priority { public const int First=800; }
 public static class AccessTools { public static MethodInfo Method(Type t,string n,Type[] p)=>null!; }
}
namespace StardewModdingAPI {
 public enum LogLevel {Info,Error} public enum AssetLoadPriority {Exclusive} public enum AssetEditPriority {Late}
 public static class Context { public static bool IsWorldReady=true; }
 public interface IMonitor {void Log(string s,LogLevel l);}
 public class Translation {public override string ToString()=>"translated";}
 public interface ITranslationHelper {Translation Get(string s);}
 public interface IGameContentHelper {void InvalidateCache(string s);T Load<T>(string s);}
 public interface ICommandHelper {void Add(string n,string help,Action<string,string[]> action);}
 public interface IModHelper { IEvents Events{get;} ITranslationHelper Translation{get;} IGameContentHelper GameContent{get;} ICommandHelper ConsoleCommands{get;} }
 public interface IEvents {IContentEvents Content{get;} IGameEvents GameLoop{get;} IWorldEvents World{get;}}
 public interface IContentEvents {event EventHandler<Events.AssetRequestedEventArgs> AssetRequested; event EventHandler<EventArgs> LocaleChanged;}
 public interface IWorldEvents {event EventHandler<EventArgs> LocationListChanged;event EventHandler<Events.ObjectListChangedEventArgs> ObjectListChanged;}
 public interface IGameEvents {event EventHandler<EventArgs> DayStarted;event EventHandler<EventArgs> ReturnedToTitle;event EventHandler<EventArgs> SaveLoaded;event EventHandler<EventArgs> Saving;event EventHandler<EventArgs> OneSecondUpdateTicked;}
 public class AssetName {public bool IsEquivalentTo(string s)=>false;}
 public class AssetData {public AssetDictionary<K,V> AsDictionary<K,V>() where K:notnull=>new();}
 public class AssetDictionary<K,V> where K:notnull {public IDictionary<K,V> Data=new Dictionary<K,V>();}
}
namespace StardewModdingAPI.Events {
 public sealed class ObjectListChangedEventArgs:EventArgs {public KeyValuePair<int,StardewValley.Object>[] Added=Array.Empty<KeyValuePair<int,StardewValley.Object>>(),Removed=Array.Empty<KeyValuePair<int,StardewValley.Object>>();}
 public class AssetRequestedEventArgs:EventArgs {public StardewModdingAPI.AssetName NameWithoutLocale=new();
 public void LoadFromModFile<T>(string p,StardewModdingAPI.AssetLoadPriority priority){}
 public void Edit(Action<StardewModdingAPI.AssetData> edit,StardewModdingAPI.AssetEditPriority priority){} }
}
namespace StardewValley {
 public class ModData:Dictionary<string,string> {public IEnumerable<KeyValuePair<string,string>> Pairs=>this;}
 public class Ref<T> {public T? Value;}
 public class Farmer {}
 public class Item {
  public string QualifiedItemId="";public int Stack=1,Quality;public ModData modData=new();
  public virtual Item getOne(){var copy=new Object{QualifiedItemId=QualifiedItemId,Stack=1,Quality=Quality};foreach(var p in modData)copy.modData[p.Key]=p.Value;return copy;}
 }
 public class Object:Item {public GameLocation? Location;public int TileLocation;public Ref<Object> heldObject=new();public int MinutesUntilReady;public bool Ready;public bool minutesElapsed(int n)=>false;public bool checkForAction(Farmer who,bool test)=>false;}
 public class GameLocation {public Dictionary<int,Object> objects=new();}
 public static class Utility {public static List<GameLocation> Locations=new();public static int Scans;public static void ForEachLocation(Func<GameLocation,bool> action,bool includeInteriors=true,bool includeGenerated=false){Scans++;foreach(var l in Locations)if(!action(l))break;}}
 public static class Game1 {public static bool IsMasterGame;}
 public static class ItemRegistry {
  public static bool FailRare;
  public static Item Create(string id,int stack=1,int quality=0)=>Create<Object>(id,stack,quality);
  public static T Create<T>(string id,int stack=1,int quality=0) where T:Item,new(){if(FailRare&&id.Contains("BloodOfJesus"))throw new Exception("registry unavailable");return new T{QualifiedItemId=id,Stack=stack,Quality=quality};}
  public static object GetData(string id)=>new();
 }
}

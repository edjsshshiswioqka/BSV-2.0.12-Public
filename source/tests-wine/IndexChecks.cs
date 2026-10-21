using BetterStardewValley;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using SObject=StardewValley.Object;
partial class Program
{
 static void IndexChecks()
 {
  Game1.IsMasterGame=true;Context.IsWorldReady=true;Utility.Scans=0;Utility.Locations.Clear();
  var location=new GameLocation();Utility.Locations.Add(location);
  for(int n=0;n<10000;n++)location.objects[n]=new SObject{QualifiedItemId="(O)388",Location=location,TileLocation=n};
  var keg=Pending();keg.Location=location;keg.TileLocation=5;location.objects[5]=keg;
  var helper=new Helper();var feature=new GrapeWineFeature(helper,new Monitor());helper.Loop.Load();
  Check(Utility.Scans==1&&!keg.heldObject.Value!.modData.ContainsKey(WineRules.PendingKey),"load indexes and resolves pending keg");
  for(int n=0;n<600;n++)helper.Loop.Tick();Check(Utility.Scans==1,"600 ticks do not rescan 10000 world objects");
  var second=Pending();second.Location=location;second.TileLocation=10000;location.objects[10000]=second;
  helper.Worlds.Change(new[]{new KeyValuePair<int,SObject>(10000,second)},Array.Empty<KeyValuePair<int,SObject>>());helper.Loop.Tick();
  Check(Utility.Scans==1&&!second.heldObject.Value!.modData.ContainsKey(WineRules.PendingKey),"object event tracks new keg without full scan");
  var removed=Pending();removed.Location=location;removed.TileLocation=10001;location.objects[10001]=removed;
  helper.Worlds.Change(new[]{new KeyValuePair<int,SObject>(10001,removed)},Array.Empty<KeyValuePair<int,SObject>>());location.objects.Remove(10001);helper.Loop.Tick();
  Check(removed.heldObject.Value!.modData.ContainsKey(WineRules.PendingKey),"removed object is pruned before event flush");
  keg.heldObject.Value=Pending().heldObject.Value;Game1.IsMasterGame=false;helper.Loop.Tick();Check(keg.heldObject.Value!.modData.ContainsKey(WineRules.PendingKey),"farmhand never resolves");
  Game1.IsMasterGame=true;helper.Loop.Tick();Check(!keg.heldObject.Value!.modData.ContainsKey(WineRules.PendingKey),"late replicated pending output resolves from index");
  helper.Loop.Title();keg.heldObject.Value=Pending().heldObject.Value;helper.Loop.Tick();Check(keg.heldObject.Value!.modData.ContainsKey(WineRules.PendingKey),"title clears previous save references");
  helper.Loop.Load();Check(Utility.Scans==2&&!keg.heldObject.Value!.modData.ContainsKey(WineRules.PendingKey),"next save rebuilds cleanly");
 }
 sealed class Monitor:IMonitor { public void Log(string s,LogLevel l){} }
 sealed class Helper:IModHelper,IEvents
 {
  public Loops Loop=new();public Worlds Worlds=new();public IEvents Events=>this;
  public IContentEvents Content {get;}=new Contents();public IGameEvents GameLoop=>Loop;public IWorldEvents World=>Worlds;
  public ITranslationHelper Translation{get;}=new Translations();public IGameContentHelper GameContent{get;}=new ContentLoader();public ICommandHelper ConsoleCommands{get;}=new Commands();
 }
 sealed class Contents:IContentEvents {public event EventHandler<AssetRequestedEventArgs>? AssetRequested;public event EventHandler<EventArgs>? LocaleChanged;}
 sealed class Worlds:IWorldEvents
 {
  public event EventHandler<EventArgs>? LocationListChanged;public event EventHandler<ObjectListChangedEventArgs>? ObjectListChanged;
  public void Change(KeyValuePair<int,SObject>[] added,KeyValuePair<int,SObject>[] removed)=>ObjectListChanged?.Invoke(this,new(){Added=added,Removed=removed});
 }
 sealed class Loops:IGameEvents
 {
  public event EventHandler<EventArgs>? DayStarted,ReturnedToTitle,SaveLoaded,Saving,OneSecondUpdateTicked;
  public void Load()=>SaveLoaded?.Invoke(this,EventArgs.Empty);public void Title()=>ReturnedToTitle?.Invoke(this,EventArgs.Empty);public void Tick()=>OneSecondUpdateTicked?.Invoke(this,EventArgs.Empty);
 }
 sealed class Translations:ITranslationHelper {public Translation Get(string s)=>new();}
 sealed class ContentLoader:IGameContentHelper {public void InvalidateCache(string s){}public T Load<T>(string s)=>throw new NotSupportedException();}
 sealed class Commands:ICommandHelper {public void Add(string n,string help,Action<string,string[]> action){} }
}

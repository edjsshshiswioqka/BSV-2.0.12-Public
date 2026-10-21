// Headless boundary doubles. Production features/data/rules/transpilers are compiled unchanged.
// These are not shipped into the mod DLL and do not claim to simulate the game's renderer/network.
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
namespace HarmonyLib {
 public class Harmony {public Harmony(string id){} public void UnpatchAll(string id){} public void Patch(MethodBase m,HarmonyMethod? prefix=null,HarmonyMethod? postfix=null,HarmonyMethod? transpiler=null){if(m==null)throw new Exception("Missing fake API");}}
 public class HarmonyMethod {public int priority;public HarmonyMethod(Type t,string n){if(AccessTools.Method(t,n)==null)throw new Exception(n);}}
 public static class Priority {public const int Last=0;}
 public static class AccessTools {public static MethodInfo Method(Type t,string n,Type[]? args=null)=>args==null?t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance).SingleOrDefault(m=>m.Name==n)!:t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance,null,args,null)!;public static ConstructorInfo Constructor(Type t,Type[] args)=>t.GetConstructor(args)!;public static FieldInfo Field(Type t,string n)=>t.GetField(n)!;public static MethodInfo PropertyGetter(Type t,string n)=>t.GetProperty(n)!.GetMethod!;}
 public class CodeInstruction {public OpCode opcode;public object? operand;public CodeInstruction(OpCode op,object? value=null){opcode=op;operand=value;}public bool StoresField(FieldInfo f)=>opcode==OpCodes.Stfld&&Equals(operand,f);public bool Calls(MethodInfo m)=>(opcode==OpCodes.Call||opcode==OpCodes.Callvirt)&&Equals(operand,m);}
}
namespace StardewModdingAPI {
 public enum LogLevel {Info,Error,Warn,Trace} public enum AssetLoadPriority {Exclusive}public enum AssetEditPriority {Late}
 public static class Context {public static bool IsWorldReady=true,IsMainPlayer=true,IsMultiplayer;}
 public interface IMonitor {void Log(string s,LogLevel l);}
 public class Translation {public string Text="";public override string ToString()=>Text;public static implicit operator string(Translation t)=>t.Text;}
 public interface ITranslationHelper {Translation Get(string s);}
 public interface IGameContentHelper {void InvalidateCache(string s);T Load<T>(string s);}
 public interface ICommandHelper {void Add(string s,string help,Action<string,string[]> action);}
 public interface IMultiplayerHelper {void SendMessage<T>(T value,string type,string[] modIDs);}
 public interface IModHelper {IEvents Events{get;}ITranslationHelper Translation{get;}IGameContentHelper GameContent{get;}ICommandHelper ConsoleCommands{get;}IMultiplayerHelper Multiplayer{get;}}
 public interface IEvents {IContentEvents Content{get;}IGameEvents GameLoop{get;}IMultiplayerEvents Multiplayer{get;}}
 public interface IContentEvents {event EventHandler<Events.AssetRequestedEventArgs> AssetRequested;event EventHandler<EventArgs> LocaleChanged;}
 public interface IGameEvents {event EventHandler<EventArgs> SaveLoaded;event EventHandler<EventArgs> DayStarted;event EventHandler<EventArgs> OneSecondUpdateTicked;event EventHandler<EventArgs> TimeChanged;event EventHandler<Events.UpdateTickedEventArgs> UpdateTicked;event EventHandler<EventArgs> ReturnedToTitle;}
 public interface IMultiplayerEvents {event EventHandler<Events.ModMessageReceivedEventArgs> ModMessageReceived;}
 public class AssetName {public string Name="";public bool IsEquivalentTo(string s)=>Name==s;}
 public class AssetData {public AssetDictionary<K,V> AsDictionary<K,V>()where K:notnull=>new();}
 public class AssetDictionary<K,V>where K:notnull {public IDictionary<K,V> Data=new Dictionary<K,V>();}
}
namespace StardewModdingAPI.Events {
 public class AssetRequestedEventArgs:EventArgs {public StardewModdingAPI.AssetName NameWithoutLocale=new();public void LoadFromModFile<T>(string p,StardewModdingAPI.AssetLoadPriority priority){}public void Edit(Action<StardewModdingAPI.AssetData> action,StardewModdingAPI.AssetEditPriority priority){}}
 public class UpdateTickedEventArgs:EventArgs {}
 public class ModMessageReceivedEventArgs:EventArgs {public string FromModID="",Type="";public long FromPlayerID;public object Body=null!;public T ReadAs<T>()=>(T)Body;}
}
namespace StardewValley {
 public static class GameStateQuery {public static Dictionary<string,Func<string[],object,bool>> Queries=new();public static void Register(string name,Func<string[],object,bool> query)=>Queries[name]=query;}
 public class Ref<T> {public T Value=default!;public Ref(){}public Ref(T v){Value=v;}}
 public class ModData:Dictionary<string,string>{}
 public class Item {public string QualifiedItemId="";public int Stack=1;public int Quality{get;set;}public ModData modData=new();}
 public class Object:Item {public GameLocation Location=null!;public int staminaRecoveredOnConsumption()=>0;public int healthRecoveredOnConsumption()=>0;public void ApplySprinkler(Vector2 tile){}public bool placementAction(GameLocation l,int x,int y,Farmer who)=>true;}
 public class Tool {}
 public class Farmer {
  public long UniqueMultiplayerID;public string Name="player";public bool IsLocalPlayer=true,isEating,temporarilyInvincible,flashDuringThisTemporaryInvincibility;
  public int health=100,temporaryInvincibilityTimer,currentTemporaryInvincibilityDuration;public ModData modData=new();
  public Dictionary<string,int> craftingRecipes=new(),cookingRecipes=new();public HashSet<string> mailReceived=new(),eventsSeen=new();
  public BuffManager buffs=new();public Item? itemToEat;public GameLocation currentLocation=null!;public Locations.FarmHouse Home=null!;
  public void eatObject(Object o){isEating=true;itemToEat=o;}public void doneEating(){isEating=false;}public void forceCanMove(){}public void completelyStopAnimatingOrDoingAction(){}
  public bool hasBuff(string id)=>buffs.AppliedBuffs.ContainsKey(id);public void applyBuff(Buff b)=>buffs.AppliedBuffs[b.id]=b;
  public bool CanBeDamaged()=>!temporarilyInvincible;public void takeDamage(int damage,bool over,object monster){}public void draw(SpriteBatch b){}
 }
 public class BuffManager {public Dictionary<string,Buff> AppliedBuffs=new();public void Remove(string id)=>AppliedBuffs.Remove(id);}
 public class Buff {public const int ENDLESS=-2;public string id;public int Duration;public string DisplayName,Description;public Buff(string id,string? source=null,string? displaySource=null,int duration=-1,Texture2D? iconTexture=null,int iconSheetIndex=-1,object? effects=null,bool? isDebuff=null,string? displayName=null,string? description=null){this.id=id;Duration=duration;DisplayName=displayName??"";Description=description??"";}}
 public class FarmerSprite {public class AnimationFrame{}}
 public class FarmerRenderer {public void draw(SpriteBatch b,FarmerSprite.AnimationFrame f,int frame,Rectangle rect,Vector2 pos,Vector2 origin,float depth,int facing,Color color,float rotation,float scale,Farmer who){}}
 public class CraftingRecipe {public string name="";public Item createItem()=>new();}
 public class GameLocation {
  public string Name="Farm";public bool IsOutdoors=true,Rain,NoSprinklers;
  public Dictionary<Vector2,TerrainFeatures.TerrainFeature> terrainFeatures=new();public List<TerrainFeatures.Bush> largeTerrainFeatures=new();public Dictionary<Vector2,Object> objects=new();
  public bool IsRainingHere()=>Rain;public string doesTileHavePropertyNoNull(int x,int y,string key,string layer)=>NoSprinklers?"T":"";public void playSound(string s,Vector2 tile){}
  public virtual Objects.Chest? GetFridge()=>null;
 }
 public class HUDMessage {public string Message;public HUDMessage(string text,int type=1){Message=text;}}
 public static class Game1 {
  public static Farmer player=new();public static GameLocation currentLocation=new();public static Dictionary<long,Farmer> Players=new();public static Season season=Season.Spring;
  public class WorldDate{public int TotalDays;}public static WorldDate Date=new();public class Stats{public uint DaysPlayed;}public static Stats stats=new();
  public static bool eventUp,PassTime=true;public static int timeOfDay=600;public static GameTime currentGameTime=new();
  public static List<string> Messages=new();public static List<(string,long,GameLocation)> Drops=new();
  public static void addHUDMessage(HUDMessage m)=>Messages.Add(m.Message);public static void showRedMessage(string m)=>Messages.Add(m);
  public static void createObjectDebris(string id,int x,int y,long farmer,GameLocation l)=>Drops.Add((id,farmer,l));
  public static bool shouldTimePass()=>PassTime;public static Farmer? GetPlayer(long id,bool online)=>Players.GetValueOrDefault(id);
 }
 public static class Utility {public static List<GameLocation> Locations=new();public static void ForEachLocation(Func<GameLocation,bool> f){foreach(var l in Locations)f(l);}public static Locations.FarmHouse? getHomeOfFarmer(Farmer f)=>f.Home;}
}
namespace StardewValley.Tools {public class WateringCan:Tool{}public class Axe:Tool{}}
namespace StardewValley.Objects {
 public class Chest:Object {public List<Item> Items=new();}
 public class IndoorPot:Object {public Ref<TerrainFeatures.Bush?> bush=new();public Ref<TerrainFeatures.HoeDirt> hoeDirt=new(new());public void Water(){}public bool performObjectDropInAction(Item dropInItem,bool probe,Farmer who,bool returnFalseIfItemConsumed=false)=>false;}
}
namespace StardewValley.Locations {public class FarmHouse:GameLocation {public Farmer owner=null!;public int upgradeLevel;public Objects.Chest Fridge=new();public override Objects.Chest GetFridge()=>Fridge;}}
namespace StardewValley.TerrainFeatures {
 public class TerrainFeature {public ModData modData=new();public GameLocation Location=null!;public Vector2 Tile;}
 public class HoeDirt:TerrainFeature {public bool Watered;public bool isWatered()=>Watered;}
 public class NetMutex {public bool Deferred;public Queue<Action> Pending=new();public int Released,Updates;public void RequestLock(Action action){if(Deferred)Pending.Enqueue(action);else action();}public void ReleaseLock()=>Released++;public void Update(GameLocation l)=>Updates++;public void Flush(){while(Pending.TryDequeue(out var a))a();}}
 public class Bush:TerrainFeature {
  public const int greenTeaBush=3;public Ref<int> size=new(3),datePlanted=new(),tileSheetOffset=new();public Ref<Rectangle> sourceRect=new();public float health;public bool NeedsUpdate;
  public NetMutex uniqueSpawnMutex=new();public Bush(){}public Bush(Vector2 tile,int size,GameLocation location,int planted=-1){Tile=tile;this.size.Value=size;Location=location;datePlanted.Value=planted;}
  public void dayUpdate(){}public bool inBloom()=>tileSheetOffset.Value==1&&BetterStardewValley.TeaFeature.Get(this,BetterStardewValley.TeaAppleRules.DeadKey,0)==0&&BetterStardewValley.TeaAppleRules.GrowingSeason((int)Game1.season);
  public int getAge()=>0;public void setUpSourceRect(){}public bool seasonUpdate(bool onLoad)=>false;public void shake(Vector2 tile,bool force){}public bool tickUpdate(GameTime time)=>true;public bool performToolAction(Tool t,int explosion,Vector2 tile)=>false;
 }
}
namespace StardewValley.Menus {
 public class ClickableTextureComponent{}
 public class CraftingPage {public bool cooking;public List<object>? _materialContainers;public int currentCraftingPage;public List<Dictionary<ClickableTextureComponent,CraftingRecipe>> pagesOfCraftingRecipes=new();private void clickCraftingRecipe(ClickableTextureComponent c,bool playSound=true){}}
}

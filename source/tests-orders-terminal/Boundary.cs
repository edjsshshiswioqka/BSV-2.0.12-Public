// A — original indoor map/content/event test boundaries, not a loaded farmhouse map.
using Microsoft.Xna.Framework;using Microsoft.Xna.Framework.Graphics;
namespace Microsoft.Xna.Framework
{
 public record struct Vector2(float X,float Y){public Vector2(float n):this(n,n){}public static Vector2 Zero=>new(0,0);public static Vector2 operator+(Vector2 a,Vector2 b)=>new(a.X+b.X,a.Y+b.Y);}
 public record struct Point(int X,int Y);
 public record struct Rectangle(int X,int Y,int Width,int Height){public int Top=>Y;public int Left=>X;public int Right=>X+Width;public int Bottom=>Y+Height;public Point Center=>new(X+Width/2,Y+Height/2);public bool Intersects(Rectangle b)=>X<b.Right&&Right>b.X&&Y<b.Bottom&&Bottom>b.Y;}
 public record struct Color{public static Color White=>new();public static Color operator*(Color c,float a)=>c;}
}
namespace Microsoft.Xna.Framework.Graphics{public enum SpriteEffects{None}public class Texture2D{}public class SpriteBatch{public void Draw(Texture2D t,Vector2 p,Rectangle? r,Color c,float rotate,Vector2 origin,float scale,SpriteEffects fx,float layer){}}}
namespace HarmonyLib{public class Harmony{public string Id;public Harmony(string id)=>Id=id;public void Patch(object? target,HarmonyMethod? prefix=null,HarmonyMethod? postfix=null,HarmonyMethod? transpiler=null,HarmonyMethod? finalizer=null){}}public class HarmonyMethod{public HarmonyMethod(Type t,string m){}}public static class AccessTools{public static object? Method(Type t,string m,Type[]? p=null)=>null;}}
namespace StardewModdingAPI
{
 public static class Context{public static bool IsWorldReady=true,IsMainPlayer=true;}
 public enum LogLevel{Warn,Error}public enum AssetLoadPriority{Exclusive}
 public class IMonitor{public List<string> Lines=new();public void Log(string text,LogLevel l)=>Lines.Add(text);}
 public class Loop{public event EventHandler? OneSecondUpdateTicked,ReturnedToTitle,SaveLoaded,DayStarted;public void Tick()=>OneSecondUpdateTicked?.Invoke(null,EventArgs.Empty);public void Reset()=>ReturnedToTitle?.Invoke(null,EventArgs.Empty);public void Load()=>SaveLoaded?.Invoke(null,EventArgs.Empty);public void Day()=>DayStarted?.Invoke(null,EventArgs.Empty);}
 public class ContentEvents{public event EventHandler<Events.AssetRequestedEventArgs>? AssetRequested;public event EventHandler? AssetsInvalidated;public void Load(Events.AssetRequestedEventArgs e)=>AssetRequested?.Invoke(null,e);public void Clear()=>AssetsInvalidated?.Invoke(null,EventArgs.Empty);}
 public class EventsHub{public Loop GameLoop=new();public ContentEvents Content=new();}public class Content{public T Load<T>(string path) where T:new()=>new();}
 public class IModHelper{public EventsHub Events=new();public Content ModContent=new();}
}
namespace StardewModdingAPI.Events
{
 public class AssetName{public string Text="";public bool IsEquivalentTo(string other)=>Text==other;}
 public class Dict<K,V> where K:notnull{public Dictionary<K,V> Data=new();}
 public class Asset{public Dictionary<Type,object> Values=new();public Dict<K,V> AsDictionary<K,V>() where K:notnull{var t=typeof(Dict<K,V>);if(!Values.ContainsKey(t))Values[t]=new Dict<K,V>();return (Dict<K,V>)Values[t];}}
 public class AssetRequestedEventArgs:EventArgs{public AssetName NameWithoutLocale=new();public void LoadFromModFile<T>(string path,StardewModdingAPI.AssetLoadPriority p){}public Asset Data=new();public void Edit(Action<Asset> edit)=>edit(Data);}
}
namespace StardewValley.GameData.BigCraftables{public class BigCraftableData{public string Name="",DisplayName="",Description="",Texture="";public int SpriteIndex,Price,Fragility;public bool CanBePlacedIndoors,CanBePlacedOutdoors;}}
namespace StardewValley
{
 public class Field<T>{public T Value;public Field(T value){Value=value;}}
 public class HUDMessage{public HUDMessage(string s,int n){}}
 public static class Utility{public static void ForEachLocation(Func<GameLocation,bool> visit,bool includeInteriors=true,bool includeGenerated=true){foreach(var l in new[]{Game1.Farm,Game1.House})if(!visit(l))break;}}
 public enum StackDrawType{Draw}
 public class Item{public string QualifiedItemId="";}
 public class Object:Item{public Field<long> owner=new(1);public int Fragility;public Vector2 TileLocation;public bool checkForAction()=>false;public bool performToolAction()=>false;public void draw(SpriteBatch b,int x,int y,float a){}public void drawInMenu(){}public void drawWhenHeld(){}public void DrawMenuIcons(SpriteBatch b,Vector2 l,float s,float a,float d,StackDrawType t,Color c){}}
 public class GameLocation{public Dictionary<Vector2,Object> objects=new();public Dictionary<string,string> modData=new();public string NameOrUniqueName="FarmHouse";}
 public class Farmer{public long UniqueMultiplayerID=1;public GameLocation currentLocation=null!;public Point StandingPixel=new(0,0);public List<Item> Items=new();public Object? ActiveObject=>Items.FirstOrDefault() as Object;public List<string> mailReceived=new(),mailbox=new(),mailForTomorrow=new();public Dictionary<string,string> modData=new();}
 public static class Game1{public static GameLocation Farm=new();public static Locations.FarmHouse House=new();public static Farmer player=new(){currentLocation=House};public static bool eventUp;public static object? activeClickableMenu;public static object viewport=new();public static void drawMouseCursor(){}public static void drawObjectDialogue(string text){}public static void addHUDMessage(HUDMessage message){}public static void loadForNewGame(bool loadedGame=false){}public static GameLocation getFarm()=>Farm;public static GameLocation getLocationFromName(string name)=>House;public static Vector2 GlobalToLocal(object viewport,Vector2 p)=>p;}
 public static class ItemRegistry{public static T Create<T>(string id) where T:Item,new()=>new(){QualifiedItemId=id};}
}
namespace StardewValley.Objects
{
 public class Furniture{public Rectangle Bounds;public bool Passable;public Rectangle GetBoundingBox()=>Bounds;public bool isPassable()=>Passable;}
 public class BedFurniture:Furniture{}public class TV:Furniture{}
}
namespace StardewValley.Locations
{
 public class Layer{public int LayerWidth=12,LayerHeight=10;public object?[,] Tiles=new object?[12,10];public Layer(){for(int x=0;x<12;x++)for(int y=0;y<10;y++)Tiles[x,y]=new();}}
 public class Map{public List<Layer> Layers=new(){new()};public Layer GetLayer(string name)=>Layers[0];}
 public class Warp{public int X,Y;}
 public class FarmHouse:GameLocation
 {
  public Map Map=new();public List<Objects.Furniture> furniture=new();public List<Warp> warps=new(){new(){X=6,Y=9}};public Point Entry=new(6,8);public HashSet<Vector2> Blocked=new();public HashSet<Vector2> ActionTiles=new();
  public bool isTilePassable(Vector2 tile)=>!Blocked.Contains(tile);public bool CanItemBePlacedHere(Vector2 tile)=>isTilePassable(tile)&&!objects.ContainsKey(tile);public string? doesTileHaveProperty(int x,int y,string name,string layer)=>ActionTiles.Contains(new(x,y))?"Action":null;public Point getEntryLocation()=>Entry;
 }
}
namespace BetterStardewValley
{
 internal static class BsvI18n{internal static string Get(StardewModdingAPI.IModHelper h,string key)=>key;}
 internal static class IntegrationHost{internal static bool Saving;internal static bool CanMutate=>StardewModdingAPI.Context.IsMainPlayer&&StardewModdingAPI.Context.IsWorldReady&&!Saving;}
}
namespace BetterStardewValley.Orders{internal class OrdersService{internal static OrdersService Instance=new();internal bool Enabled=true;}internal class OrdersMenu{internal OrdersMenu(OrdersService s,string name,int x,int y){}}}

namespace BetterStardewValley { internal static class DrawPatchTargets { internal static object? Require(Type t,string n,Type r,params Type[] p)=>null; internal static object? ObjectMenu()=>null; } }

// World/footprint behavior is exercised separately against actual game assemblies in TerminalUpgradeChecks.
namespace BetterStardewValley.Orders
{
 internal class TerminalWorld { internal TerminalWorld(StardewModdingAPI.IModHelper h,StardewModdingAPI.IMonitor m,HarmonyLib.Harmony p){} }
 internal static class TerminalEntity
 {
  internal static readonly Rectangle WorldSource=new(0,64,64,64);
  internal const int WorldSize=2;
  internal static Rectangle VisualBounds(Vector2 tile)=>new((int)tile.X*64,(int)tile.Y*64,128,128);
  internal static bool IsPart(StardewValley.Object o)=>false;
  internal static bool Resolve(StardewValley.GameLocation l,Vector2 t,out StardewValley.Object a)=>l.objects.TryGetValue(t,out a!);
  internal static bool Allowed(StardewValley.GameLocation l,StardewValley.Farmer f)=>true;
  internal static bool CanUse(StardewValley.Farmer f,string l,int x,int y)=>true;
  internal static bool Pending(StardewValley.Object o)=>false;
 }
}

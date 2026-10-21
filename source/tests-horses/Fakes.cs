// Headless boundary doubles. These files are excluded from the released mod assembly.
using System;using System.Collections.Generic;using System.Linq;using System.Reflection;
namespace Microsoft.Xna.Framework {
 public struct Vector2 {public float X,Y;public Vector2(float x,float y){X=x;Y=y;}public static Vector2 Zero=>new(0,0);public static Vector2 operator+(Vector2 a,Vector2 b)=>new(a.X+b.X,a.Y+b.Y);public static Vector2 operator-(Vector2 a,Vector2 b)=>new(a.X-b.X,a.Y-b.Y);public static Vector2 operator/(Vector2 a,float n)=>new(a.X/n,a.Y/n);public static float DistanceSquared(Vector2 a,Vector2 b)=>(a-b).LengthSquared();public static float Distance(Vector2 a,Vector2 b)=>(a-b).Length();public float LengthSquared()=>X*X+Y*Y;public float Length()=>MathF.Sqrt(LengthSquared());public override string ToString()=>$"{X},{Y}";}
 public struct Point {public int X,Y;public Point(int x,int y){X=x;Y=y;}}
 public struct Rectangle {public int X,Y,Width,Height;public int Left=>X;public int Top=>Y;public int Right=>X+Width;public int Bottom=>Y+Height;public Rectangle(int x,int y,int w,int h){X=x;Y=y;Width=w;Height=h;}public bool Intersects(Rectangle b)=>Left<b.Right&&Right>b.Left&&Top<b.Bottom&&Bottom>b.Top;public bool Contains(int x,int y)=>x>=X&&x<Right&&y>=Y&&y<Bottom;public void Inflate(int x,int y){X-=x;Y-=y;Width+=2*x;Height+=2*y;}}
 public struct Color {public Color(int r,int g,int b){} public static Color operator*(Color c,float v)=>c;}
 public class GameTime {public TimeSpan ElapsedGameTime{get;set;}=TimeSpan.FromMilliseconds(16.67);}
}
namespace Microsoft.Xna.Framework.Audio {public class SoundEffect {public static SoundEffect FromStream(System.IO.Stream s)=>new();public bool Play(float v,float p,float pan){StardewValley.Game1.Trace.Add("Whistle");return true;}}}
namespace HarmonyLib {
 public static class Priority{public const int First=800,Last=0;}public class HarmonyMethod{public int priority;public HarmonyMethod(Type t,string n){if(t.GetMethod(n,BindingFlags.NonPublic|BindingFlags.Static)==null)throw new Exception(n);}}
 public class Harmony{public static int Patches;public Harmony(string s){}public void Patch(MethodInfo m,HarmonyMethod? prefix,HarmonyMethod? postfix){Patches++;}public void UnpatchAll(string s){}}
 public static class AccessTools {public delegate ref F FieldRef<T,F>(T o);static ref bool GetFrozen(StardewValley.Character h)=>ref h.Frozen;public static FieldRef<T,F> FieldRefAccess<T,F>(string n)=>(FieldRef<T,F>)(object)new FieldRef<StardewValley.Character,bool>(GetFrozen);public static MethodInfo? Method(Type t,string n,Type[]? a=null)=>a==null?t.GetMethods().FirstOrDefault(m=>m.Name==n):t.GetMethod(n,a);}
}
namespace StardewModdingAPI {
 public enum SButton{F8,None,H,J,V,G,MouseX1,ControllerA}public enum LogLevel{Info,Warn,Error}public class IMonitor{public List<string> Logs=new();public void Log(string s,LogLevel l)=>Logs.Add(s);}
 public static class Context{public static bool IsMainPlayer=true,IsWorldReady=true,IsPlayerFree=true,IsSplitScreen;public static int ScreenId;}
 public class IModHelper {public string DirectoryPath="";public EventsHub Events=new();public InputHelper Input=new();public MultiplayerHelper Multiplayer=new();public TranslationHelper Translation=new();public Commands ConsoleCommands=new();public void WriteConfig<T>(T c){} }
 public class TranslationHelper{public string Get(string key,object? tokens=null)=>key;}
 public class Commands{public Dictionary<string,Action<string,string[]>> Items=new();public void Add(string n,string d,Action<string,string[]> f)=>Items[n]=f;}
 public class InputHelper{public bool AlreadySuppressed;public bool IsSuppressed(SButton b)=>AlreadySuppressed;public int Suppressed;public void Suppress(SButton b)=>Suppressed++;public Cursor GetCursorPosition()=>new();}public class Cursor{public Microsoft.Xna.Framework.Vector2 AbsolutePixels=new(-10000,-10000);}
 public class ModInfo{public Version Version=new(typeof(BetterStardewValley.ModEntry).Assembly.GetName().Version!.ToString(3));}public class Peer{public long PlayerID;public ModInfo? Info=new();public ModInfo? GetMod(string id)=>Info;}
 public class MultiplayerHelper{public List<(object Body,string Type,long[]? Targets)> Sent=new();public Dictionary<long,Peer> Peers=new();public Peer? GetConnectedPlayer(long id)=>Peers.GetValueOrDefault(id);public void SendMessage<T>(T body,string type,string[] mods,long[]? ids=null)=>Sent.Add((body!,type,ids));}
 public class EventsHub{public InputEvents Input=new();public LoopEvents GameLoop=new();public DisplayEvents Display=new();public MultiplayerEvents Multiplayer=new();public WorldEvents World=new();}
 public class InputEvents{public event EventHandler<Events.ButtonPressedEventArgs>? ButtonPressed;public void Raise(SButton b)=>ButtonPressed?.Invoke(this,new(){Button=b});}
 public class LoopEvents{public event EventHandler<Events.UpdateTickedEventArgs>? UpdateTicked;public event EventHandler? SaveLoaded,DayStarted,Saving,ReturnedToTitle;public void Tick()=>UpdateTicked?.Invoke(this,new());public void Load()=>SaveLoaded?.Invoke(this,EventArgs.Empty);public void Day()=>DayStarted?.Invoke(this,EventArgs.Empty);public void Save()=>Saving?.Invoke(this,EventArgs.Empty);public void Title()=>ReturnedToTitle?.Invoke(this,EventArgs.Empty);}
 public class DisplayEvents{public event EventHandler<Events.RenderedWorldEventArgs>? RenderedWorld;public void Draw(Events.RenderedWorldEventArgs e)=>RenderedWorld?.Invoke(this,e);}
 public class MultiplayerEvents{public event EventHandler<Events.ModMessageReceivedEventArgs>? ModMessageReceived;public event EventHandler<Events.PeerDisconnectedEventArgs>? PeerDisconnected;public void Receive(Events.ModMessageReceivedEventArgs e)=>ModMessageReceived?.Invoke(this,e);public void Disconnect(long id)=>PeerDisconnected?.Invoke(this,new(){Peer=new(){PlayerID=id}});}
 public class WorldEvents{public event EventHandler<Events.BuildingListChangedEventArgs>? BuildingListChanged;public void Remove(StardewValley.Buildings.Stable s)=>BuildingListChanged?.Invoke(this,new(){Removed=new[]{s}});}
}
namespace StardewModdingAPI.Events {
 public class ButtonPressedEventArgs:EventArgs{public StardewModdingAPI.SButton Button;}public class UpdateTickedEventArgs:EventArgs{}
 public class RenderedWorldEventArgs:EventArgs{public FakeBatch SpriteBatch=new();}public class FakeBatch{public int Draws;public void Draw(object t,Microsoft.Xna.Framework.Rectangle r,Microsoft.Xna.Framework.Color c)=>Draws++;}
 public class ModMessageReceivedEventArgs:EventArgs{public long FromPlayerID;public string FromModID="Eddie.BetterStardewValley",Type="";public object Body=new();public T ReadAs<T>()=>(T)Body;}
 public class PeerDisconnectedEventArgs:EventArgs{public StardewModdingAPI.Peer Peer=new();}public class BuildingListChangedEventArgs:EventArgs{public IEnumerable<StardewValley.Buildings.Building> Removed=Array.Empty<StardewValley.Buildings.Building>();}
}
namespace StardewModdingAPI.Utilities{public class PerScreen<T>{readonly Func<T> Create;readonly Dictionary<int,T> Values=new();public PerScreen(Func<T> f)=>Create=f;public T Value{get{if(!Values.TryGetValue(StardewModdingAPI.Context.ScreenId,out var v))Values[StardewModdingAPI.Context.ScreenId]=v=Create();return v;}set=>Values[StardewModdingAPI.Context.ScreenId]=value;}}}
namespace StardewValley {
 using Microsoft.Xna.Framework;
 public class Net<T>{public T Value;public Net(T v=default!){Value=v;}}
 public class PositionField{public bool Interpolating;public PositionField Field=>this;public bool IsInterpolating()=>Interpolating;}
 public class Character{public PositionField position=new();public bool Frozen;public Vector2 Position;public GameLocation currentLocation=null!;public Rectangle GetBoundingBox()=>new((int)Position.X+8,(int)Position.Y+16,32,32);}
 public class Sprite {public int SpriteWidth=32,CurrentFrame;public object? CurrentAnimation;}
 public class NPC:Character {public string Name="",displayName="";public bool IsInvisible,flip;public int FacingDirection=3;public Vector2 drawOffset;public Sprite Sprite=new();public object? controller;public Dictionary<string,string> modData=new();public void Halt(){}public void faceDirection(int d){FacingDirection=d;}public new Rectangle GetBoundingBox()=>this is Characters.Horse?new((int)Position.X+8,(int)Position.Y+16,96,32):base.GetBoundingBox();}
 public class Farmer:Character{public float getMovementSpeed()=>10;public bool isRidingHorse()=>mount!=null;public long UniqueMultiplayerID;public bool IsLocalPlayer=true,CanMove=true;public string Name="Player";public Net<string> horseName=new("");public Characters.Horse? mount;public Dictionary<string,string> modData=new();}
 public class FakeTile{}
 public class FakeLayer{public int LayerWidth,LayerHeight;public FakeTile?[,] Tiles;public FakeLayer(int w,int h){LayerWidth=w;LayerHeight=h;Tiles=new FakeTile[w,h];for(int x=0;x<w;x++)for(int y=0;y<h;y++)Tiles[x,y]=new();}}
 public class FakeMap{public List<FakeLayer> Layers=new();public Dictionary<string,string> Properties=new();public FakeLayer? GetLayer(string s)=>s=="Back"?Layers[0]:s=="Buildings"?Layers[1]:null;public FakeMap(int w=70,int h=60){Layers.Add(new(w,h));Layers.Add(new(w,h));}}
 public class GameLocation {
  public string Name="Farm";public string NameOrUniqueName=>Name;public bool IsOutdoors=true,IsTemporary;public object? currentEvent;public FakeMap Map=new();public List<Buildings.Building> buildings=new();public List<NPC> characters=new();public List<Farmer> farmers=new();public List<Rectangle> Obstacles=new();public HashSet<(int,int)> Water=new();public (Response[] Responses,Action<Farmer,string> Callback)? Question;
  public string? doesTileHaveProperty(int x,int y,string p,string layer)=>p=="Water"&&Water.Contains((x,y))?"T":null;
  public bool isCollidingPosition(Rectangle box,Rectangle v,bool f,int d,bool g,Character h,bool pathfinding,bool projectile=false,bool ignoreCharacterRequirement=false,bool skipCollisionEffects=false)=>Obstacles.Any(r=>r.Intersects(box));
  public void createQuestionDialogue(string text,Response[] responses,Action<Farmer,string> action){Question=(responses,action);Game1.activeClickableMenu=Question;}
 }
 public class Response{public string responseKey,responseText;public Response(string key,string text){responseKey=key;responseText=text;}}
 public class Options{public float soundVolumeLevel=1;}public class Font{public Vector2 MeasureString(string s)=>new(s.Split('\n').Max(x=>x.Length)*8,s.Split('\n').Length*16);}
 public static class Game1{
  public static bool IsMasterGame=>StardewModdingAPI.Context.IsMainPlayer;public static bool IsMultiplayer=true,eventUp;public static Farmer player=new(),MasterPlayer=player;public static GameLocation currentLocation=new();public static Rectangle viewport=new(0,0,640,480);public static Options options=new();public static object? activeClickableMenu;public static object staminaRect=new();public static Font smallFont=new();public static Color textColor=new();public static List<GameLocation> Locations=new();public static List<Farmer> Players=new();public static HashSet<long> Offline=new();public static List<string> Trace=new();public static List<(Characters.Horse Horse,string Destination,Vector2 Position)> Warps=new();
  public static Farmer? GetPlayer(long id,bool onlyOnline=false)=>onlyOnline&&Offline.Contains(id)?null:Players.FirstOrDefault(p=>p.UniqueMultiplayerID==id);public static IEnumerable<Farmer> getAllFarmers()=>Players;public static IEnumerable<Farmer> getOnlineFarmers()=>Players.Where(p=>!Offline.Contains(p.UniqueMultiplayerID));public static void showRedMessage(string m)=>Trace.Add("Message:"+m);public static void exitActiveMenu()=>activeClickableMenu=null;public static Vector2 GlobalToLocal(Rectangle v,Vector2 p)=>p-new Vector2(v.X,v.Y);
  public static void warpCharacter(Characters.Horse h,string loc,Point p){h.currentLocation.characters.Remove(h);h.currentLocation=Locations.Single(l=>l.Name==loc);h.currentLocation.characters.Add(h);h.Position=new(p.X*64,p.Y*64);Warps.Add((h,loc,h.Position));}
 }
 public static class Utility{public static void ForEachVillager(Func<NPC,bool> f){foreach(var n in Game1.Locations.SelectMany(l=>l.characters).Where(n=>n is not Characters.Horse))if(!f(n))break;}public static void ForEachLocation(Func<GameLocation,bool> f){foreach(var l in Game1.Locations)if(!f(l))break;}public static Characters.Horse? findHorse(Guid id)=>Game1.Locations.SelectMany(l=>l.characters.OfType<Characters.Horse>()).FirstOrDefault(h=>h.HorseId==id);public static void drawTextWithShadow(StardewModdingAPI.Events.FakeBatch b,string s,Font f,Vector2 p,Color c){} }
}
namespace StardewValley.Buildings {
 using StardewValley;public class Building{public Net<string> buildingType=new("Stable");public Net<long> owner=new();public Net<int> daysOfConstructionLeft=new();public Dictionary<string,string> modData=new();public GameLocation Parent=null!;public GameLocation GetParentLocation()=>Parent;}
 public class Stable:Building{public Guid HorseId=Guid.NewGuid();public Characters.Horse? getStableHorse()=>Utility.findHorse(HorseId);public void updateHorseOwnership(){var h=getStableHorse();if(h!=null){h.ownerId.Value=owner.Value;h.Name=Game1.GetPlayer(owner.Value)?.horseName.Value??"";}}public void grabHorse(){}public void performActionOnDemolition(GameLocation l){} }
}
namespace StardewValley.Characters {
 using StardewValley;public class Mutex{public bool Locked;public bool IsLocked()=>Locked;}
 public class Horse:NPC{public Guid HorseId;public Net<long> ownerId=new();public Farmer? rider;public Net<bool> mounting=new(),dismounting=new();public Mutex mutex=new();public Farmer? getOwner()=>Game1.GetPlayer(ownerId.Value);public bool checkAction(Farmer who,GameLocation l)=>true;public void update(Microsoft.Xna.Framework.GameTime time,GameLocation location){}public void dismount(bool from_demolish=false){if(rider!=null)rider.mount=null;rider=null;}}
}
namespace StardewValley.Menus{public class NamingMenu{public Action<string> Callback;public NamingMenu(Action<string> callback,string prompt,string initial){Callback=callback;}}}
namespace BetterStardewValley{public class ModEntry{}}


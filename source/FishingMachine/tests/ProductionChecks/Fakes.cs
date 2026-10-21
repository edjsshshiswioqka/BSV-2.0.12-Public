// Minimal headless game boundary. Tests execute the real ProductionService but do not claim UI/network integration.
using Eddie.FishingMachine.Core;
namespace Microsoft.Xna.Framework
{
    public readonly record struct Vector2(float X,float Y){public static Vector2 Zero=>new(0,0);public static Vector2 operator*(Vector2 p,float f)=>new(p.X*f,p.Y*f);}
    public readonly record struct Rectangle(int X,int Y,int Width,int Height){public int Left=>X;public int Top=>Y;public bool Contains(int x,int y)=>x>=X && y>=Y && x<X+Width && y<Y+Height;}
    public readonly record struct Color(int R,int G,int B){public static Color Black=>new(0,0,0);public static Color White=>new(255,255,255);public static Color Gold=>new(255,215,0);public static Color operator*(Color c,float f)=>c;}
}
namespace Microsoft.Xna.Framework.Graphics
{
    using Microsoft.Xna.Framework;
    public enum SpriteEffects{None}
    public class SpriteBatch{public void Draw(object t,Rectangle r,Color c){}public void DrawString(Font f,string s,Vector2 p,Color c,float r=0,Vector2 o=default,float z=1,SpriteEffects e=0,float d=0){}}
    public class Font{public Vector2 MeasureString(string text)=>new(text.Length*8,24);}
}
namespace StardewModdingAPI
{
    public static class Context{public static bool IsWorldReady=true,IsMainPlayer=true;}
    public enum LogLevel{Debug,Info,Warn,Error}
    public enum SButton{T,ControllerX,MouseLeft,MouseRight,R,ControllerY}
}
namespace StardewValley
{
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    public class Net<T>{public T Value;public Net(T value){Value=value;}}
    public class Metadata:Dictionary<string,string>
    {
        public bool FailNextWrite;public Func<string,string,bool>? RejectWrite;public readonly List<(string Key,string Value)> Writes=new();public readonly List<string> WrittenKeys=new();
        public new string this[string key]{get=>base[key];set{if(FailNextWrite || RejectWrite?.Invoke(key,value)==true){FailNextWrite=false;throw new InvalidOperationException("Injected write failure");}base[key]=value;WrittenKeys.Add(key);if(value.Contains("\"Phase\":\"ready\""))Writes.Add((key,value));}}
    }
    public class Item
    {
        public string QualifiedItemId="";public int Stack=1,Quality;public bool specialItem,isLostItem,IsRecipe;public readonly Metadata modData=new();
        public virtual Item getOne(){var i=new Object{QualifiedItemId=QualifiedItemId,Quality=Quality};if(this is Object o){i.preservedParentSheetIndex.Value=o.preservedParentSheetIndex.Value;i.questItem.Value=o.questItem.Value;}foreach(var p in modData)i.modData[p.Key]=p.Value;return i;}
        public virtual bool canStackWith(Item i)=>i.QualifiedItemId==QualifiedItemId && i.Quality==Quality;
        public virtual int maximumStackSize()=>999;
    }
    public partial class Object:Item{public Net<bool> questItem=new(false);public Net<string> preservedParentSheetIndex=new("");public Net<string?> preserve=new(null);public Net<string> questId=new(""),orderData=new("");public int Price,Edibility;}
    public partial class Farmer{public int FishingLevel=10;public Item? CurrentTool;public long UniqueMultiplayerID=1;public Metadata modData=new();public Dictionary<string,int[]> fishCaught=new();}
    public partial class GameLocation{public Dictionary<Vector2,Object> objects=new();public string NameOrUniqueName="Beach";}
    public static class ItemRegistry
    {
        public static string? Missing;
        public sealed class Data{public bool IsErrorItem;public string DisplayName="fixture";}
        public static Data GetDataOrErrorItem(string id)=>GetData(id);
        public static Data GetData(string id)=>new(){IsErrorItem=id==Missing||Missing=="*"};
        public static Item Create(string id,int amount=1,int quality=0)=>new Object{QualifiedItemId=id,Stack=amount,Quality=quality};
    }
    public sealed class DateInfo{public int TotalDays;}
    public sealed class TimeInfo{public TimeSpan TotalGameTime;}
    public static partial class Game1
    {
        public static DateInfo Date=new();public static int timeOfDay=600,year=1;
        public static TimeInfo currentGameTime=new();public static Random random=new(77);
        public static Farmer MasterPlayer=new();public static Farmer player=MasterPlayer;
        public static readonly Dictionary<long,Farmer> Players=new();public static readonly HashSet<long> Offline=new();
        public static Farmer? GetPlayer(long id,bool onlyOnline=false)=>onlyOnline && Offline.Contains(id)?null:id==MasterPlayer.UniqueMultiplayerID?MasterPlayer:Players.GetValueOrDefault(id);
        public static IEnumerable<Farmer> getOnlineFarmers()=>Players.Values.Append(MasterPlayer).Where(p=>!Offline.Contains(p.UniqueMultiplayerID));
        public static object? activeClickableMenu;public static Font smallFont=new();public static Rectangle uiViewport=new(0,0,1280,720);
        public static object staminaRect=new();public static Color textColor=Color.Black;
        public static void PushUIMode(){}public static void PopUIMode(){}public static int MouseX,MouseY;public static int getMouseX()=>MouseX;public static int getMouseY()=>MouseY;public static void playSound(string s){}
    }
}
namespace StardewValley.Objects
{
    using Microsoft.Xna.Framework;
    public class Mutex
    {
        public bool Locked;public long Holder;public void Update(GameLocation l){}public bool IsLocked()=>Locked;public bool IsLockHeld()=>Locked && (Holder==0 || Holder==Game1.player.UniqueMultiplayerID);
        public void RequestLock(Action yes,Action no){if(Locked)no();else{Locked=true;Holder=Game1.player.UniqueMultiplayerID;yes();}}public void ReleaseLock(){Locked=false;Holder=0;}
    }
    public class Chest:StardewValley.Object
    {
        public enum SpecialChestTypes{None,BigChest,JunimoChest,MiniShippingBin}
        public Net<bool> playerChest=new(true),fridge=new(false),giftbox=new(false);public string? GlobalInventoryId;
        public List<Item> Items=new();public GameLocation Location=new();public Vector2 TileLocation=new(1,1);public Net<long> owner=new(1);
        public SpecialChestTypes SpecialChestType;private readonly Mutex mutex=new();public Mutex GetMutex()=>mutex;
        public int GetActualCapacity()=>Eddie.FishingMachine.ModEntry.IsCrab(this)?70:MachineUpgrade.Capacity(modData.TryGetValue(Eddie.FishingMachine.ProductionService.StateKey,out var raw)?ProductionState.Load(raw).Level:1);
    }
}
namespace StardewValley.Menus
{
    using Microsoft.Xna.Framework.Graphics;
    public class ClickableComponent{public Microsoft.Xna.Framework.Rectangle bounds=new(160,436,64,64);}
    public class InventoryMenu{public int xPositionOnScreen=160,yPositionOnScreen=440,width=768;public List<ClickableComponent> inventory=new(){new()};}
    public class ItemGrabMenu{public object? context;public InventoryMenu inventory=new(),ItemsToGrabMenu=new();}
    public class IClickableMenu{public static void drawHoverText(SpriteBatch b,string s,Font f){}}
}
namespace Eddie.FishingMachine
{
    using StardewValley;
    using StardewValley.Objects;
    using Microsoft.Xna.Framework;
    public class Screen<T>{public T Value=default!;}
    public class Monitor{public List<string> Messages=new();public void Log(string s,StardewModdingAPI.LogLevel l)=>Messages.Add(s);}
    public class Index
    {
        public Dictionary<Chest,GameLocation> All=new();public void Remove(Chest c)=>All.Remove(c);
        public bool Contains(Chest c,GameLocation l)=>All.TryGetValue(c,out var found)&&found==l;
    }
    public class WorldService
    {
        public static GridTile[] Footprint(Chest c)=>new[]{new GridTile((int)c.TileLocation.X,(int)c.TileLocation.Y)};
        public static Facing GetFacing(Chest c)=>Facing.Right;public static bool IsExtended(Chest c)=>false;
        public bool TryAnchor(GameLocation l,Vector2 p,out Chest? c){c=l.objects.GetValueOrDefault(p) as Chest;return c!=null;}
    }
    public static class FishPool
    {
        public static List<FishChoice> Choices=new(){new("(O)128",1)};
        public static string[] Junk={"(O)168"};public static string? LocalJelly="(O)RiverJelly";
        public static List<FishChoice> Get(GameLocation l,Vector2 p,Facing f,Farmer owner,bool e,int machineLevel=1,int catchYear=0)=>Choices;
        public static string[] Other(GameLocation l,Vector2 p,Facing f,bool e)=>Junk;
        public static string? Jelly(GameLocation l,Vector2 p,Facing f,bool e)=>LocalJelly;
    }
    internal partial class ModEntry
    {
        public static bool IsAnchor(StardewValley.Item item)=>item.modData.ContainsKey(Tag+"MachineId");
        public const string Tag="Eddie.FishingMachine/";public BalanceConfig Config=new();public bool Saving;
        public Index Machines=new();public WorldService World=new();public Monitor Monitor=new();public Screen<Chest?> OpenChest=new();
        public string T(string key,object? tokens=null)=>key;public void Message(string key){}
    }
}

namespace StardewValley.Tools
{
    public class FishingRod:StardewValley.Item
    {
        public int UpgradeLevel;
        public bool isFishing,isReeling,pullingOutOfWater,hit,fishCaught;
        public List<StardewValley.Item?> attachments=new();public List<string> enchantments=new();
        public override StardewValley.Item getOne(){var c=new FishingRod{QualifiedItemId=QualifiedItemId,UpgradeLevel=UpgradeLevel};c.attachments.AddRange(attachments);c.enchantments.AddRange(enchantments);foreach(var p in modData)c.modData[p.Key]=p.Value;return c;}
    }
}
namespace BetterStardewValley
{
    internal static class AnnualLegendaryFeature
    {
        internal static bool MachineAvailable(StardewValley.Farmer p,string id,int year,bool committed=false,bool historical=false)
        {id=AnnualLegendaryRules.NormalizeId(id)!;p.modData.TryGetValue(AnnualLegendaryRules.KeyPrefix+id,out var raw);return AnnualLegendaryRules.ReadRecord(raw,p.fishCaught.TryGetValue("(O)"+id,out var count)?count[0]:0,StardewValley.Game1.year).Year<year;}
        internal static void MarkMachineCatch(StardewValley.Farmer p,string id,int year)
        {id=AnnualLegendaryRules.NormalizeId(id)!;p.modData[AnnualLegendaryRules.KeyPrefix+id]=new LegendaryCatchRecord(year,0).Serialize();}
    }
}

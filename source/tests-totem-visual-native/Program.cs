// A — focused rendering harness adapted from BSV tests-automation-native; no third-party implementation copied.
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Xml.Serialization;
using BetterStardewValley;
using BetterStardewValley.Totems;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.BigCraftables;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Tools;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.Tools;
using xTile;
using xTile.Layers;
using xTile.Tiles;
using SObject = StardewValley.Object;
using Size = xTile.Dimensions.Size;

partial class Program
{
    static readonly BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly List<string> Scenarios = new(), Notices = new();
    static Farmer Who = null!, Host = null!, Guest = null!;
    static Farm Farm = null!; static FarmHouse Home = null!;
    static bool HostAuthority; static int Checks;
    static readonly Dictionary<string,StardewValley.GameData.Machines.MachineData> MachineFixtures = new();
    static void Check(bool ok, string text) { Checks++; if (!ok) { Console.Error.WriteLine(JsonSerializer.Serialize(new { failed=text, prior_passes=Scenarios, runtime_logs=Recorder.Logs })); throw new Exception(text); } Scenarios.Add(text); }
    static bool Skip() => false;
    static bool Date(ref WorldDate __result){__result=new WorldDate(1,"spring",11);return false;}
    static bool Yes(ref bool __result) { __result = true; return false; }
    static bool No(ref bool __result) { __result = false; return false; }
    static bool Authority(ref bool __result) { __result = HostAuthority; return false; }
    static bool Master(ref Farmer __result) { __result = Host; return false; }
    static bool GetPlayer(long id, ref Farmer __result) { __result = id == Host.UniqueMultiplayerID ? Host : Guest; return false; }
    static bool Notice(HUDMessage message) { Notices.Add(message.message); return false; }
    static bool Peers(ref bool __result) { __result = true; return false; }
    static bool Farmers(ref IEnumerable<Farmer> __result){__result=Online;return false;}
    static List<Farmer> _online=new();
    static List<Farmer> Online {get=>_online;set{_online=value;Game1.otherFarmers.Clear();foreach(var f in value)if(f!=Game1.player)Game1.otherFarmers[f.UniqueMultiplayerID]=f;}}
    static bool AllLocations(Func<GameLocation, bool> action) { foreach (var l in Game1.locations) if (!action(l)) break; return false; }
    sealed class EmptyServices : IServiceProvider { public object? GetService(Type t) => null; }
    sealed class TestContent : LocalizedContentManager
    {
        internal TestContent() : base(new EmptyServices(), ".") { }
        public override T Load<T>(string name)
        {
            object data = name.Replace('\\', '/') switch
            {
                "LooseSprites/Cursors" => Texture(4096,4096), // dimensions only; no graphics or third-party pixels
                "Strings/StringsFromCSFiles" or "Data/Furniture" => new Dictionary<string, string>(),
                "Data/BigCraftables" => Game1.bigCraftableData,
                "Data/Locations" => new Dictionary<string, StardewValley.GameData.Locations.LocationData>(),
                "Data/Crops" => new Dictionary<string, StardewValley.GameData.Crops.CropData> { ["472"]=new() },
                "Data/WildTrees" => new Dictionary<string, StardewValley.GameData.WildTrees.WildTreeData>(),
                "Data/FruitTrees" => new Dictionary<string, StardewValley.GameData.FruitTrees.FruitTreeData>(),
                "Data/Objects" => Game1.objectData,
                "Data/Machines" => MachineFixtures,
                "Data/FloorsAndPaths" => new Dictionary<string,StardewValley.GameData.FloorsAndPaths.FloorPathData>(),
                "Data/ObjectContextTags" => new Dictionary<string,string>(),
                "Data/Fish" => new Dictionary<string,string>{["128"]="Fish/30"},
                _ => throw new InvalidOperationException("Unknown content fixture: " + name)
            };
            return (T)data;
        }
    }
    static Texture2D Texture(int width, int height)
    {
        var t = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D)); GC.SuppressFinalize(t);
        foreach (var f in typeof(Texture2D).GetFields(Any).Where(f => f.FieldType == typeof(int)))
        { if (f.Name.Contains("width", StringComparison.OrdinalIgnoreCase)) f.SetValue(t, width); if (f.Name.Contains("height", StringComparison.OrdinalIgnoreCase)) f.SetValue(t, height); }
        return t;
    }
    static Map Map()
    {
        var map = new Map("TerminalFixture"); var sheet = new TileSheet("fixture", map, "not-rendered", new Size(2, 2), new Size(16, 16)); map.AddTileSheet(sheet);
        var back = new Layer("Back", map, new Size(24, 20), new Size(64, 64)); var walls = new Layer("Buildings", map, new Size(24, 20), new Size(64, 64)); map.AddLayer(back); map.AddLayer(walls);
        for (int y = 0; y < 20; y++) for (int x = 0; x < 24; x++) back.Tiles[x, y] = new StaticTile(back, sheet, BlendMode.Alpha, 0);
        return map;
    }
    static void Put(Farmer who, GameLocation l, int x, int y)
    {
        who.currentLocation = l; Game1.game1.instanceGameLocation = l;
        typeof(Farmer).GetProperty("Position")!.SetValue(who, new Vector2(x * 64, y * 64 + 16));
        Who = who; typeof(Game1).GetField("_player", Any)!.SetValue(null, who);
    }
    static void Reset()
    {
        HostAuthority = true; Game1.multiplayerMode=0; typeof(Context).GetProperty("IsWorldReady")!.SetValue(null,true);
        Game1.currentLightSources.Clear(); Farm.objects.Clear(); Home.objects.Clear(); Home.furniture.Clear(); Farm.furniture.Clear();
        Farm.terrainFeatures.Clear(); Farm.warps.Clear(); Farm.map = Map(); Home.map = Map();
        Host.modData.Clear(); Guest.modData.Clear(); Host.mailReceived.Clear(); Host.mailbox.Clear(); Host.mailForTomorrow.Clear();
        Host.Items.Clear(); Host.MaxItems = 12; for (int i = 0; i < 12; i++) Host.Items.Add(null);
        Guest.Items.Clear(); Guest.MaxItems = 12; for (int i = 0; i < 12; i++) Guest.Items.Add(null);
        Host.CurrentToolIndex = Guest.CurrentToolIndex = 0;
        Game1.activeClickableMenu = null; Game1.eventUp = false; Game1.timeOfDay = 1200;
        Put(Host, Farm, 3, 4); Notices.Clear(); Recorder.Logs.Clear();
    }
    static SObject NativeMachine(string id,int x,int y)
    {
        var obj=ItemRegistry.Create<SObject>(id);obj.TileLocation=new(x,y);obj.owner.Value=Host.UniqueMultiplayerID;
        Farm.objects[obj.TileLocation]=obj;return obj;
    }
    static void Main()
    {
        Game1.game1 = (Game1)FormatterServices.GetUninitializedObject(typeof(Game1)); Game1.game1._locations = new List<GameLocation>();
        Game1.random = new Random(1110); Game1.otherFarmers = new();Game1.year=1;Game1.dayOfMonth=11;Game1.season=Season.Spring; Game1._locationLookup = new(StringComparer.OrdinalIgnoreCase);
        var h = new Harmony("BSV.Automation.NativeFixture");
        void Patch(Type t, string name, string callback, params Type[] types) => h.Patch(t.GetMethod(name, Any, null, types, null)!, prefix: new(typeof(Program), callback));
        Patch(typeof(GameLocation), "updateMap", nameof(Skip)); Patch(typeof(FarmHouse), "updateMap", nameof(Skip));
        Patch(typeof(GameLocation), "InIslandContext", nameof(No)); Patch(typeof(Furniture), "minutesElapsed", nameof(Skip), typeof(int));
        Patch(typeof(Farmer), "farmerInit", nameof(Skip)); Patch(typeof(AnimatedSprite), "LoadTexture", nameof(Skip), typeof(string), typeof(bool));
        Patch(typeof(StardewValley.BellsAndWhistles.PlayerStatusList), "AddSpriteDefinition", nameof(Skip), typeof(string), typeof(string), typeof(int), typeof(int), typeof(int), typeof(int));
        Patch(typeof(Context), "get_IsWorldReady", nameof(Yes)); Patch(typeof(Context), "get_IsPlayerFree", nameof(Yes)); Patch(typeof(Context), "get_IsMainPlayer", nameof(Authority));
        Patch(typeof(Context), "get_IsSplitScreen", nameof(No));
        Patch(typeof(Game1), "get_Date", nameof(Date));Patch(typeof(Game1), "get_HostPaused", nameof(No)); Patch(typeof(Game1), "get_IsMasterGame", nameof(Authority)); Patch(typeof(Game1), "get_MasterPlayer", nameof(Master));
        Patch(typeof(Game1), "GetPlayer", nameof(GetPlayer), typeof(long), typeof(bool)); Patch(typeof(Game1), "addHUDMessage", nameof(Notice), typeof(HUDMessage));
        Patch(typeof(Game1),"getAllFarmers",nameof(Farmers));
        Patch(typeof(Farmer), "OnItemReceived", nameof(Skip), typeof(Item), typeof(int), typeof(Item), typeof(bool));
        Game1.content = new TestContent();
        Game1.netWorldState=new Netcode.NetRoot<StardewValley.Network.NetWorldState>(new StardewValley.Network.NetWorldState());
        ItemRegistry.AddTypeDefinition(new ObjectDataDefinition()); ItemRegistry.AddTypeDefinition(new BigCraftableDataDefinition()); ItemRegistry.AddTypeDefinition(new ToolDataDefinition());
        Game1.bigCraftableData = new Dictionary<string, BigCraftableData> { ["130"] = new() { Name="Chest", DisplayName="Chest", Texture="fixture" } };
        Game1.objectData = new Dictionary<string, ObjectData> { ["390"] = new() { Name = "Stone", Texture = "fixture", DisplayName = "Stone", Type = "Basic" } };
        Game1.toolData = new Dictionary<string, ToolData> { ["Pickaxe"] = new() { ClassName = "Pickaxe", Name = "Pickaxe", DisplayName = "Pickaxe", Texture = "fixture" } };
        Game1.options = (Options)FormatterServices.GetUninitializedObject(typeof(Options)); Game1.currentLightSources = new(); Game1.lantern = Texture(64, 64);
        Farm = new Farm(); Farm.name.Value = "Farm"; Farm.map = Map(); Farm.IsOutdoors = true; Game1.locations.Add(Farm);
        Host = new Farmer(); Host.UniqueMultiplayerID = 1110; Host.buffs.SetOwner(Host); Host.buffs.Dirty = false;
        Guest = new Farmer(); Guest.UniqueMultiplayerID = 1111;Guest.teamRoot=Host.teamRoot;Online=new(){Host,Guest}; Guest.buffs.SetOwner(Guest); Guest.buffs.Dirty = false;
        Put(Host, Farm, 3, 4);
        Home = new FarmHouse(); Home.name.Value = "FarmHouse"; Home.map = Map(); Game1.locations.Add(Home);
        var helper = (IModHelper)Recorder.Make(typeof(IModHelper)); var monitor = (IMonitor)Recorder.Make(typeof(IMonitor));
        Game1.spriteBatch=(SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch));GC.SuppressFinalize(Game1.spriteBatch);
        Reset(); TotemVisual114(helper);
        Check(!Recorder.Logs.Any(l => l.Contains("AmbiguousMatchException")), "exact Harmony targets initialize without ambiguity");
        // Process isolation owns test detours, including constructors used by the XML serializer.
        Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks = Checks, scenarios = Scenarios, geometry = TotemGeometry, native_game_assembly = typeof(Game1).Assembly.GetName().Version!.ToString(), live_game = false, real_multiplayer = false,
            boundaries = "Exact production totem rendering source, native Object draw/placement and LightSource; synthetic maps, GPU draw interception, XML roundtrip and SMAPI event proxies. No live game, GPU rasterization or multiplayer sockets." }));
    }
}

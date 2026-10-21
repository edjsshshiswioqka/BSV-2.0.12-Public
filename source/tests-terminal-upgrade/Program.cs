using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Xml.Serialization;
using BetterStardewValley;
using BetterStardewValley.Orders;
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
    static Farm Farm = null!; static FarmHouse Home = null!; static TerminalWorld World = null!;
    static bool HostAuthority; static int Checks;
    static void Check(bool ok, string text) { Checks++; Console.Error.WriteLine((ok ? "PASS " : "FAIL ")+text); if (!ok) throw new Exception(text); Scenarios.Add(text); }
    static bool Skip() => false;
    static bool Yes(ref bool __result) { __result = true; return false; }
    static bool No(ref bool __result) { __result = false; return false; }
    static bool Authority(ref bool __result) { __result = HostAuthority; return false; }
    static bool Master(ref Farmer __result) { __result = Host; return false; }
    static bool GetPlayer(long id, ref Farmer __result) { __result = id == Host.UniqueMultiplayerID ? Host : Guest; return false; }
    static bool Notice(HUDMessage message) { Notices.Add(message.message); return false; }
    static bool Peers(ref bool __result) { __result = true; return false; }
    static bool AllLocations(Func<GameLocation, bool> action) { foreach (var l in Game1.locations) if (!action(l)) break; return false; }
    sealed class EmptyServices : IServiceProvider { public object? GetService(Type t) => null; }
    sealed class TestContent : LocalizedContentManager
    {
        internal TestContent() : base(new EmptyServices(), ".") { }
        public override T Load<T>(string name)
        {
            object data = name.Replace('\\', '/') switch
            {
                "Strings/StringsFromCSFiles" or "Data/Furniture" or "Data/BigCraftables" => new Dictionary<string, string>(),
                "Data/Locations" => new Dictionary<string, StardewValley.GameData.Locations.LocationData>(),
                "Data/Crops" => new Dictionary<string, StardewValley.GameData.Crops.CropData>(),
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
    static SObject Item() => ItemRegistry.Create<SObject>("(BC)" + OrderRules.TerminalId);
    static void Reset()
    {
        HostAuthority = true; Game1.multiplayerMode=0; typeof(Context).GetProperty("IsWorldReady")!.SetValue(null,true); IntegrationHost.Saving = false;
        World.Lights.Clear(); Game1.currentLightSources.Clear(); Farm.objects.Clear(); Home.objects.Clear(); Home.furniture.Clear(); Farm.furniture.Clear();
        Farm.terrainFeatures.Clear(); Farm.warps.Clear(); Farm.map = Map(); Home.map = Map();
        Host.modData.Clear(); Guest.modData.Clear(); Host.mailReceived.Clear(); Host.mailbox.Clear(); Host.mailForTomorrow.Clear();
        Host.Items.Clear(); Host.MaxItems = 12; for (int i = 0; i < 12; i++) Host.Items.Add(null);
        Guest.Items.Clear(); Guest.MaxItems = 12; for (int i = 0; i < 12; i++) Guest.Items.Add(null);
        Host.CurrentToolIndex = Guest.CurrentToolIndex = 0;
        Game1.activeClickableMenu = null; Game1.eventUp = false; Game1.timeOfDay = 1200;
        Put(Host, Farm, 3, 4); Notices.Clear(); Recorder.Logs.Clear();
    }
    static SObject Place(int x = 4, int y = 4)
    {
        string id = Guid.NewGuid().ToString("N"); var item = Item();
        Check(World.Place(Who.currentLocation, new(x, y), Who, id, item, () => { }) == null, "host places one physical 1x1 terminal with 2x2 artwork");
        return item;
    }
    static void Main(string[] args)
    {
        Game1.game1 = (Game1)FormatterServices.GetUninitializedObject(typeof(Game1)); Game1.game1._locations = new List<GameLocation>();
        Game1.random = new Random(1110); Game1.otherFarmers = new(); Game1._locationLookup = new(StringComparer.OrdinalIgnoreCase);
        var h = new Harmony("BSV.Terminal.Fixture");
        void Patch(Type t, string name, string callback, params Type[] types) => h.Patch(t.GetMethod(name, Any, null, types, null)!, prefix: new(typeof(Program), callback));
        Patch(typeof(GameLocation), "updateMap", nameof(Skip)); Patch(typeof(FarmHouse), "updateMap", nameof(Skip));
        Patch(typeof(GameLocation), "InIslandContext", nameof(No)); Patch(typeof(Furniture), "minutesElapsed", nameof(Skip), typeof(int));
        Patch(typeof(Farmer), "farmerInit", nameof(Skip)); Patch(typeof(AnimatedSprite), "LoadTexture", nameof(Skip), typeof(string), typeof(bool));
        Patch(typeof(StardewValley.BellsAndWhistles.PlayerStatusList), "AddSpriteDefinition", nameof(Skip), typeof(string), typeof(string), typeof(int), typeof(int), typeof(int), typeof(int));
        Patch(typeof(Context), "get_IsWorldReady", nameof(Yes)); Patch(typeof(Context), "get_IsPlayerFree", nameof(Yes)); Patch(typeof(Context), "get_IsMainPlayer", nameof(Authority));
        Patch(typeof(Game1), "get_HostPaused", nameof(No)); Patch(typeof(Game1), "get_IsMasterGame", nameof(Authority)); Patch(typeof(Game1), "get_MasterPlayer", nameof(Master));
        Patch(typeof(Game1), "GetPlayer", nameof(GetPlayer), typeof(long), typeof(bool)); Patch(typeof(Game1), "addHUDMessage", nameof(Notice), typeof(HUDMessage));
        Patch(typeof(TerminalNetwork), "PeerMatches", nameof(Peers), typeof(long));
        Patch(typeof(Farmer), "OnItemReceived", nameof(Skip), typeof(Item), typeof(int), typeof(Item), typeof(bool));
        Game1.content = new TestContent();
        ItemRegistry.AddTypeDefinition(new ObjectDataDefinition()); ItemRegistry.AddTypeDefinition(new BigCraftableDataDefinition()); ItemRegistry.AddTypeDefinition(new ToolDataDefinition());
        Game1.bigCraftableData = new Dictionary<string, BigCraftableData> { [OrderRules.TerminalId] = new() { Name = "BSV Order Terminal", DisplayName = "Terminal", Texture = "fixture", SpriteIndex = 0, CanBePlacedIndoors = true, CanBePlacedOutdoors = true }, ["130"] = new() { Name = "Chest", Texture = "fixture", DisplayName = "Chest" } };
        Game1.objectData = new Dictionary<string, ObjectData> { ["390"] = new() { Name = "Stone", Texture = "fixture", DisplayName = "Stone", Type = "Basic" } };
        Game1.toolData = new Dictionary<string, ToolData> { ["Pickaxe"] = new() { ClassName = "Pickaxe", Name = "Pickaxe", DisplayName = "Pickaxe", Texture = "fixture" } };
        Game1.options = (Options)FormatterServices.GetUninitializedObject(typeof(Options)); Game1.currentLightSources = new(); Game1.lantern = Texture(64, 64);
        Farm = new Farm(); Farm.name.Value = "Farm"; Farm.map = Map(); Farm.IsOutdoors = true; Game1.locations.Add(Farm);
        Host = new Farmer(); Host.UniqueMultiplayerID = 1110; Host.buffs.SetOwner(Host); Host.buffs.Dirty = false;
        Guest = new Farmer(); Guest.UniqueMultiplayerID = 1111; Guest.buffs.SetOwner(Guest); Guest.buffs.Dirty = false;
        Put(Host, Farm, 3, 4);
        Home = new FarmHouse(); Home.name.Value = "FarmHouse"; Home.map = Map(); Game1.locations.Add(Home);
        var helper = (IModHelper)Recorder.Make(typeof(IModHelper)); var monitor = (IMonitor)Recorder.Make(typeof(IMonitor));
        var harmony = new Harmony("BSV.Terminal.Production");
        _ = new OrderTerminal(helper, monitor, harmony);
        World = (TerminalWorld)typeof(TerminalWorld).GetField("Instance", Any)!.GetValue(null)!;
        Reset(); GeometryChecks(); CollisionRegressionChecks(); TerminalAfkRoutingChecks(); MigrationChecks(); NetworkChecks(); LightChecks(); DrawingChecks(h);
        Check(!Recorder.Logs.Any(l => l.Contains("AmbiguousMatchException")), "exact Harmony targets initialize without ambiguity");
        // Process-owned detours need no native teardown. Retain the bounded diagnostic
        // reproduction mode for the headless runtime's intermittent native cleanup crash.
        if(args.Contains("--probe-detour-cleanup"))
        { Console.Error.WriteLine("START fixture detour cleanup"); h.UnpatchAll(h.Id); Console.Error.WriteLine("START production detour cleanup"); harmony.UnpatchAll(harmony.Id); }
        Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks = Checks, scenarios = Scenarios, native_game_assembly = typeof(Game1).Assembly.GetName().Version!.ToString(), live_game = false, real_multiplayer = false,
            boundaries = "Synthetic maps/data and players; SMAPI context/event proxies; remote peer/version handshake; recorded SpriteBatch calls without GPU. Production footprint, collision, XML, transactions and native LightSource executed." }));
    }
}

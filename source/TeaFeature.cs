using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Machines;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace BetterStardewValley;

internal sealed partial class TeaFeature
{
    private static TeaFeature Instance = null!;
    private readonly IModHelper Helper;
    private readonly IMonitor Monitor;
    internal static bool IsInitialized { get; private set; }
    internal TeaFeature(IModHelper helper, IMonitor monitor)
    {
        Instance = this; Helper = helper; Monitor = monitor;
        var h = new Harmony(TeaAppleRules.Prefix + ".Tea");
        try
        {
        Patch(typeof(Bush), nameof(Bush.dayUpdate), nameof(Day), null);
        Patch(typeof(Bush), nameof(Bush.inBloom), nameof(Bloom), null);
        Patch(typeof(Bush), nameof(Bush.getAge), null, nameof(Age));
        Patch(typeof(Bush), nameof(Bush.setUpSourceRect), null, nameof(SourceRect));
        Patch(typeof(Bush), nameof(Bush.seasonUpdate), nameof(SeasonUpdate), null);
        Patch(typeof(Bush), nameof(Bush.shake), nameof(Harvest), null);
        Patch(typeof(Bush), nameof(Bush.tickUpdate), null, nameof(Tick));
        Patch(typeof(Bush), nameof(Bush.performToolAction), nameof(Tool), null);
        Patch(typeof(SObject), nameof(SObject.ApplySprinkler), null, nameof(Sprinkler));
        Patch(typeof(SObject), nameof(SObject.placementAction), nameof(PlantOutside), nameof(PlacedOutside));
        Patch(typeof(IndoorPot), nameof(IndoorPot.performObjectDropInAction), nameof(PlantPot), nameof(PlacedPot));
        Patch(typeof(IndoorPot), nameof(IndoorPot.Water), null, nameof(PotWater));
        h.Patch(AccessTools.Constructor(typeof(Bush), new[] { typeof(Vector2), typeof(int), typeof(GameLocation), typeof(int) }),
            postfix: new HarmonyMethod(typeof(TeaFeature), nameof(NewBush)));
        }
        catch { h.UnpatchAll(TeaAppleRules.Prefix + ".Tea"); throw; }
        GameStateQuery.Register(TeaAppleRules.SeasonQuery, (_, _) => TeaAppleRules.GrowingSeason((int)Game1.season));
        helper.Events.Content.AssetRequested += Assets;
        helper.Events.Content.LocaleChanged += (_, _) =>
        {
            foreach (string asset in new[] { "Data/Objects", "Data/CraftingRecipes", "Data/CookingRecipes", "Data/Events/Sunroom", "Data/mail" })
                helper.GameContent.InvalidateCache(asset);
        };
        helper.Events.GameLoop.SaveLoaded += (_, _) => { Unlock(); World(); };
        helper.Events.GameLoop.DayStarted += (_, _) => { Unlock(); World(); };
        helper.Events.GameLoop.OneSecondUpdateTicked += (_, _) => Unlock();
        helper.ConsoleCommands.Add("bsv_tea_status", BsvI18n.Get(helper, "cmd.tea"), (_, _) => Status());
        IsInitialized = true;
        void Patch(Type type, string method, string? prefix, string? postfix) => h.Patch(AccessTools.Method(type, method),
            prefix: prefix == null ? null : new HarmonyMethod(typeof(TeaFeature), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(TeaFeature), postfix));
    }

    private void Assets(object? sender, AssetRequestedEventArgs e)
    {
        foreach (var texture in TeaAppleData.Textures)
            if (e.NameWithoutLocale.IsEquivalentTo(TeaAppleData.TextureRoot + texture.Key))
            { e.LoadFromModFile<Texture2D>(texture.Value, AssetLoadPriority.Exclusive); return; }
        string Tr(string key) => BetterStardewValley.BsvI18n.Get(Helper, key).ToString();
        if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects")) e.Edit(a => TeaAppleData.Objects(a.AsDictionary<string, ObjectData>().Data, Tr), AssetEditPriority.Late);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops")) e.Edit(a => TeaAppleData.Shops(a.AsDictionary<string, ShopData>().Data), AssetEditPriority.Late);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Machines")) e.Edit(a => TeaAppleData.Machines(a.AsDictionary<string, MachineData>().Data), AssetEditPriority.Late);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes")) e.Edit(a => TeaAppleData.Crafting(a.AsDictionary<string, string>().Data, Tr), AssetEditPriority.Late);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/CookingRecipes")) e.Edit(a => TeaAppleData.Cooking(a.AsDictionary<string, string>().Data, Tr), AssetEditPriority.Late);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Events/Sunroom")) e.Edit(a => TeaAppleData.Events(a.AsDictionary<string, string>().Data, Tr), AssetEditPriority.Late);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/mail")) e.Edit(a => TeaAppleData.Mail(a.AsDictionary<string, string>().Data, Tr), AssetEditPriority.Late);
    }

    private void Unlock()
    {
        if (!Context.IsWorldReady) return;
        var who = Game1.player;
        // Old saves with the completed vanilla event get the same unlock. Never share personal flags.
        who.craftingRecipes.Remove("Tea Sapling");
        if (Game1.eventUp || !who.eventsSeen.Contains(TeaAppleRules.CarolineEvent)) return;
        if (!who.craftingRecipes.ContainsKey(TeaAppleRules.CupId)) who.craftingRecipes.Add(TeaAppleRules.CupId, 0);
        if (who.mailReceived.Add(TeaAppleRules.TeaUnlock))
            Game1.addHUDMessage(new HUDMessage(BetterStardewValley.BsvI18n.Get(Helper, "tea.unlocked"), 2));
    }

    private void World()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer) return;
        Utility.ForEachLocation(location =>
        {
            foreach (var bush in location.terrainFeatures.Values.OfType<Bush>()) Visit(bush);
            foreach (var bush in location.largeTerrainFeatures.OfType<Bush>()) Visit(bush);
            foreach (var pot in location.objects.Values.OfType<IndoorPot>())
                if (pot.bush.Value is { } bush) Visit(bush);
            return true;
        });
        void Visit(Bush b)
        {
            if (!Managed(b)) return;
            Initialize(b, false);
            if (!InSeason(b))
                Write(b, Read(b) with { Dead = true, Ready = false, LastDay = Today });
            else if (Sheltered(b) && Read(b).Dead)
                Write(b, Read(b) with { Dead = false, LastDay = Today }); // sheltered tea killed by an older BSV winter comes back
            if (b.Location.IsOutdoors && b.Location.IsRainingHere()) Water(b);
            b.setUpSourceRect();
        }
    }
    internal static int Today => Game1.Date.TotalDays;
    /// <summary>Greenhouse, Ginger Island, indoors and any garden pot: tea there ignores the seasons and never dies.</summary>
    internal static bool Sheltered(Bush b) => b.inPot.Value || b.Location is { } l && (!l.IsOutdoors || l.SeedsIgnoreSeasonsHere());
    internal static bool InSeason(Bush b) => Sheltered(b) || TeaAppleRules.GrowingSeason((int)Game1.season);
    internal static bool Managed(Bush? b) => b != null && b.size.Value == Bush.greenTeaBush
        && b.Location != null && b.Location.Name != "Sunroom";
    internal static int Get(Bush b, string key, int fallback = -1) =>
        TeaAppleRules.Number(b.modData.TryGetValue(key, out string? s) ? s : null, fallback);
    private static void Set(Bush b, string key, int n) => b.modData[key] = TeaAppleRules.Text(n);
    private static bool Dead(Bush b) => Get(b, TeaAppleRules.DeadKey, 0) == 1;
    internal static TeaAppleRules.Plant Read(Bush b) => new(Get(b, TeaAppleRules.GrowthKey, 0),
        Get(b, TeaAppleRules.RegrowKey, TeaAppleRules.RegrowDays), b.tileSheetOffset.Value == 1,
        Dead(b), Get(b, TeaAppleRules.DayKey));
    internal static void Write(Bush b, TeaAppleRules.Plant p)
    {
        Set(b, TeaAppleRules.GrowthKey, p.Growth); Set(b, TeaAppleRules.RegrowKey, p.Regrow);
        Set(b, TeaAppleRules.DeadKey, p.Dead ? 1 : 0); Set(b, TeaAppleRules.DayKey, p.LastDay);
        b.tileSheetOffset.Value = p.Ready ? 1 : 0;
    }
    private static void Initialize(Bush b, bool newlyPlanted)
    {
        if (!Managed(b) || b.modData.ContainsKey(TeaAppleRules.VersionKey)) return;
        int originalAge = Math.Max(0, (int)Game1.stats.DaysPlayed - b.datePlanted.Value);
        int growth = newlyPlanted ? 0 : TeaAppleRules.MigratedGrowth(originalAge);
        Write(b, new(growth, growth >= TeaAppleRules.GrowthDays ? TeaAppleRules.RegrowDays : 0,
            !newlyPlanted && b.tileSheetOffset.Value == 1 && growth >= TeaAppleRules.GrowthDays,
            !InSeason(b), Today));
        b.modData[TeaAppleRules.VersionKey] = "1";
        if (b.Location.IsOutdoors && b.Location.IsRainingHere()) Water(b);
    }
    private static void NewBush(Bush __instance) { if (Context.IsWorldReady) Initialize(__instance, true); }
    private static bool Day(Bush __instance)
    {
        if (!Managed(__instance)) return true;
        if (!Context.IsMainPlayer) return false; // host advances each planted entity once, independently of visitors
        Initialize(__instance, false);
        int watered = Get(__instance, TeaAppleRules.WaterKey) == Today - 1 || Get(__instance, TeaAppleRules.PreviousWaterKey) == Today - 1 ? Today - 1 : -999;
        Write(__instance, TeaAppleRules.Advance(Read(__instance), Today, Sheltered(__instance) ? 0 : (int)Game1.season, watered));
        __instance.health = 0;
        __instance.setUpSourceRect();
        return false;
    }
    private static bool Bloom(Bush __instance, ref bool __result)
    {
        if (!Managed(__instance)) return true;
        __result = !Dead(__instance) && InSeason(__instance) && __instance.tileSheetOffset.Value == 1;
        return false;
    }
    private static void Age(Bush __instance, ref int __result)
    {
        if (Managed(__instance) && __instance.modData.ContainsKey(TeaAppleRules.VersionKey))
            __result = Math.Clamp(Get(__instance, TeaAppleRules.GrowthKey, 0), 0, TeaAppleRules.GrowthDays) * 20 / TeaAppleRules.GrowthDays;
    }
    private static void SourceRect(Bush __instance)
    { if (Managed(__instance) && Dead(__instance)) __instance.sourceRect.Value = new Rectangle(96, 288, 16, 32); }
    private static bool SeasonUpdate(Bush __instance, ref bool __result)
    {
        if (!Managed(__instance)) return true;
        if (Context.IsMainPlayer && !InSeason(__instance))
            Write(__instance, Read(__instance) with { Dead = true, Ready = false, LastDay = Today });
        // Vanilla resets tileSheetOffset on every season transition. Preserve a ready leaf
        // across spring -> summer, and preserve the permanent dead marker across winter -> spring.
        __instance.setUpSourceRect(); __result = false; return false;
    }
    private static void Tick(Bush __instance)
    {
        if (!Managed(__instance)) return;
        __instance.uniqueSpawnMutex.Update(__instance.Location);
        // Like vanilla walnut bushes, network locks must keep ticking even when the sprite is still.
        __instance.NeedsUpdate = true;
    }
    private static void Water(Bush b)
    {
        if (!Managed(b) || Dead(b)) return;
        int lastWater = Get(b, TeaAppleRules.WaterKey);
        if (lastWater != Today) Set(b, TeaAppleRules.PreviousWaterKey, lastWater);
        Set(b, TeaAppleRules.WaterKey, Today);
    }
    private static bool Tool(Bush __instance, StardewValley.Tool t, ref bool __result)
    {
        if (!Managed(__instance)) return true;
        if (Dead(__instance) && t is Axe)
        { __instance.Location.playSound("axchop", __instance.Tile); __result = true; return false; }
        if (t is not WateringCan) return true;
        Water(__instance); __result = false; return false;
    }
    private static void PotWater(IndoorPot __instance) { if (__instance.bush.Value is { } b) Water(b); }
    private static void Sprinkler(SObject __instance, Vector2 tile)
    {
        var location = __instance.Location;
        if (location == null || location.doesTileHavePropertyNoNull((int)tile.X, (int)tile.Y, "NoSprinklers", "Back") == "T") return;
        if (location.terrainFeatures.TryGetValue(tile, out var feature) && feature is Bush b) Water(b);
        if (location.objects.TryGetValue(tile, out var obj) && obj is IndoorPot pot && pot.bush.Value is { } p) Water(p);
    }
    private static bool PlantOutside(SObject __instance, GameLocation location, int x, int y, out bool __state, ref bool __result)
    {
        __state = __instance.QualifiedItemId == TeaAppleRules.Sapling
            && location.terrainFeatures.TryGetValue(new Vector2(x / 64, y / 64), out var soil) && soil is HoeDirt dirt && dirt.isWatered();
        if (__instance.QualifiedItemId != TeaAppleRules.Sapling || TeaAppleRules.GrowingSeason((int)Game1.season)
            || !location.IsOutdoors || location.SeedsIgnoreSeasonsHere()) return true;
        Game1.showRedMessage(BetterStardewValley.BsvI18n.Get(Instance.Helper, "tea.wrong-season")); __result = false; return false;
    }
    private static void PlacedOutside(SObject __instance, GameLocation location, int x, int y, bool __state, bool __result)
    {
        if (__result && __state && location.terrainFeatures.TryGetValue(new Vector2(x / 64, y / 64), out var f) && f is Bush b) Water(b);
    }
    // Garden pots are sheltered: a tea sapling may go into a pot in any season, exactly like vanilla.
    private static bool PlantPot(Item dropInItem, bool probe, ref bool __result) => true;
    private static void PlacedPot(IndoorPot __instance, Item dropInItem, bool probe, bool __result)
    {
        if (!probe && __result && dropInItem.QualifiedItemId == TeaAppleRules.Sapling
            && __instance.bush.Value is { } b && __instance.hoeDirt.Value.isWatered()) Water(b);
    }
    private static bool Harvest(Bush __instance)
    {
        if (!Managed(__instance) || !__instance.inBloom()) return true;
        Bush bush = __instance;
        long farmer = Game1.player.UniqueMultiplayerID;
        // Reuse the native network mutex and native no-quality leaf drop. Simultaneous clicks
        // cannot harvest the same ready leaf twice. Watering has its own key and never rewrites growth.
        bush.NeedsUpdate = true;
        bush.uniqueSpawnMutex.RequestLock(() =>
        {
            try
            {
                if (!ConsumeHarvest(bush)) return;
                for (int n = 0; n < TeaAppleRules.LeavesPerHarvest; n++)
                    Game1.createObjectDebris(TeaAppleRules.Leaves, (int)bush.Tile.X, (int)bush.Tile.Y, farmer, bush.Location);
                bush.shake(bush.Tile, true); // ready flag is already cleared; this call only animates
            }
            finally { bush.uniqueSpawnMutex.ReleaseLock(); }
        });
        return false;
    }
    // Caller must own the native bush mutex. Shared by manual and automated harvests;
    // only the caller decides where to put the single leaf (debris or a connected chest).
    internal static bool ConsumeHarvest(Bush bush)
    {
        if (!Managed(bush) || !bush.inBloom()) return false;
        var before=Read(bush);
        try
        {
            Write(bush, TeaAppleRules.Harvest(before));
            bush.setUpSourceRect();
            return true;
        }
        catch
        {
            Write(bush,before);
            bush.setUpSourceRect();
            throw;
        }
    }
    private void Status()
    {
        if (!Context.IsWorldReady) { Monitor.Log(BsvI18n.Get(Helper,"diag.load-save"), LogLevel.Info); return; }
        Monitor.Log($"BSV tea: event={Game1.player.eventsSeen.Contains(TeaAppleRules.CarolineEvent)}, knowledge={Game1.player.mailReceived.Contains(TeaAppleRules.TeaUnlock)}, cupRecipe={Game1.player.craftingRecipes.ContainsKey(TeaAppleRules.CupId)}, host={Context.IsMainPlayer}; 15 watered days, 2-day regrowth, 400g sapling, 80g leaf, 5 leaves / 2 days / 750g cake.", LogLevel.Info);
        foreach (var b in Game1.currentLocation.terrainFeatures.Values.OfType<Bush>().Where(Managed))
            Monitor.Log($"Tea {b.Tile}: {Read(b)}; wateredDay={Get(b, TeaAppleRules.WaterKey)}, today={Today}", LogLevel.Info);
    }
}

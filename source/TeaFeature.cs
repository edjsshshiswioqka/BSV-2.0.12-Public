using System.Reflection;
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
        Patch(typeof(SObject), nameof(SObject.placementAction), nameof(PlantOutside), null);
        foreach (var check in typeof(SObject).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(SObject.canBePlacedHere) && m.DeclaringType == typeof(SObject) && m.ReturnType == typeof(bool)))
            h.Patch(check, prefix: new HarmonyMethod(typeof(TeaFeature), nameof(AsGameSapling)));
        Patch(typeof(IndoorPot), nameof(IndoorPot.performObjectDropInAction), nameof(PlantPot), null);
        if (AccessTools.Method(typeof(SObject), "IsTeaSapling") != null) Patch(typeof(SObject), "IsTeaSapling", null, nameof(IsTea));
        Patch(typeof(IndoorPot), nameof(IndoorPot.Water), null, nameof(PotWater));
        h.Patch(AccessTools.Constructor(typeof(Bush), new[] { typeof(Vector2), typeof(int), typeof(GameLocation), typeof(int) }),
            postfix: new HarmonyMethod(typeof(TeaFeature), nameof(NewBush)));
        }
        catch { h.UnpatchAll(TeaAppleRules.Prefix + ".Tea"); throw; }
        InstallWaterLook(h, helper, monitor); // cosmetic: a failure here never disables tea
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
        if (e.NameWithoutLocale.IsEquivalentTo(TeaAppleData.TextureRoot + "Apple"))
        { e.LoadFrom(GoldenAppleTexture, AssetLoadPriority.Exclusive); return; }
        foreach (var texture in TeaAppleData.Textures)
            if (e.NameWithoutLocale.IsEquivalentTo(TeaAppleData.TextureRoot + texture.Key))
            { e.LoadFromModFile<Texture2D>(texture.Value, AssetLoadPriority.Exclusive); return; }
        string Tr(string key) => BetterStardewValley.BsvI18n.Get(Helper, key).ToString();
        if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects")) e.Edit(a => TeaAppleData.Objects(a.AsDictionary<string, ObjectData>().Data, Tr), BetterStardewValley.BsvAssets.EditPriority);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops")) e.Edit(a => TeaAppleData.Shops(a.AsDictionary<string, ShopData>().Data), BetterStardewValley.BsvAssets.EditPriority);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Machines")) e.Edit(a => TeaAppleData.Machines(a.AsDictionary<string, MachineData>().Data), BetterStardewValley.BsvAssets.EditPriority);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes")) e.Edit(a => TeaAppleData.Crafting(a.AsDictionary<string, string>().Data, Tr), BetterStardewValley.BsvAssets.EditPriority);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/CookingRecipes")) e.Edit(a => TeaAppleData.Cooking(a.AsDictionary<string, string>().Data, Tr), BetterStardewValley.BsvAssets.EditPriority);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Events/Sunroom")) e.Edit(a => TeaAppleData.Events(a.AsDictionary<string, string>().Data, Tr), BetterStardewValley.BsvAssets.EditPriority);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/mail")) e.Edit(a => TeaAppleData.Mail(a.AsDictionary<string, string>().Data, Tr), BetterStardewValley.BsvAssets.EditPriority);
    }

    /// <summary>
    /// The Golden Apple is the player's own game apple sprite recoloured to gold at runtime, with a brighter highlight.
    /// No game art is shipped with BSV; if the apple sprite can't be read, BSV's own drawing is used instead.
    /// </summary>
    private Texture2D GoldenAppleTexture()
    {
        try
        {
            var data = ItemRegistry.GetDataOrErrorItem("(O)613");
            var source = data.GetSourceRect();var sheet = data.GetTexture();
            var px = new Color[source.Width * source.Height];
            sheet.GetData(0, source, px, 0, px.Length);
            var shades = px.Where(c => c.A > 0).Select(c => c with { A = 255 }).Distinct().OrderBy(Luma).ToList();
            Color[] ramp = { new(110, 52, 0), new(214, 128, 0), new(255, 190, 20), new(255, 228, 96), new(255, 255, 224) };
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].A == 0) continue;
                int rank = shades.IndexOf(px[i] with { A = 255 });
                var gold = ramp[(int)Math.Round(rank * (ramp.Length - 1) / (double)Math.Max(1, shades.Count - 1))];
                px[i] = new Color(gold.R, gold.G, gold.B, px[i].A);
            }
            if (source.Width == 16 && source.Height == 16) // extra highlight on the upper left of the fruit
                foreach (var (x, y) in new[] { (4, 6), (4, 7), (3, 8), (5, 6) })
                    if (px[y * 16 + x].A > 0 && px[y * 16 + x] != new Color(110, 52, 0)) px[y * 16 + x] = ramp[^1];
            var result = new Texture2D(Game1.graphics.GraphicsDevice, source.Width, source.Height);
            result.SetData(px);
            return result;
        }
        catch (Exception ex)
        {
            Monitor.Log("BSV Golden Apple: using the bundled sprite (" + ex.Message + ").", LogLevel.Trace);
            return Helper.ModContent.Load<Texture2D>(TeaAppleData.Textures["Apple"]);
        }
        static float Luma(Color c) => .3f * c.R + .59f * c.G + .11f * c.B;
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
            Release(b);
            Adopt(b);
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
    /// <summary>Custom Bush (furyx639.CustomBush) bushes are tea-sized too; they follow their own mod's rules, never BSV's tea.</summary>
    internal const string CustomBushKey = "furyx639.CustomBush/Id";
    internal static bool Managed(Bush? b) => b != null && b.size.Value == Bush.greenTeaBush
        && b.Location != null && b.Location.Name != "Sunroom" && !b.modData.ContainsKey(CustomBushKey)
        && b.modData.ContainsKey(TeaAppleRules.OwnedKey);
    /// <summary>A bush grown from BSV's own Tea Sapling. Older BSV versions managed every tea bush; those (already carrying
    /// BSV tea state and not a Custom Bush) become BSV tea once, so existing tea keeps BSV's rules.</summary>
    private static void Own(Bush b) => b.modData[TeaAppleRules.OwnedKey] = "1";
    private static void Adopt(Bush b)
    {
        if (b.size.Value == Bush.greenTeaBush && !b.modData.ContainsKey(CustomBushKey) && !b.modData.ContainsKey(TeaAppleRules.OwnedKey)
            && b.modData.ContainsKey(TeaAppleRules.VersionKey)) Own(b);
    }
    /// <summary>A Custom Bush bush an older BSV treated as tea: its BSV tea state is removed so only Custom Bush drives it.</summary>
    private static void Release(Bush b)
    {
        if (!b.modData.ContainsKey(CustomBushKey) || !b.modData.ContainsKey(TeaAppleRules.VersionKey)) return;
        foreach (string key in new[] { TeaAppleRules.GrowthKey, TeaAppleRules.RegrowKey, TeaAppleRules.DeadKey, TeaAppleRules.WaterKey,
            TeaAppleRules.PreviousWaterKey, TeaAppleRules.DayKey, TeaAppleRules.VersionKey })
            b.modData.Remove(key);
        b.setUpSourceRect();
    }
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
        // A withered tea plant can also be cut away with any scythe (scythe, golden or iridium).
        if (Dead(__instance) && t is MeleeWeapon blade && blade.isScythe())
        {
            var l = __instance.Location; var at = __instance.Tile * 64f;
            l.playSound("cut", __instance.Tile);
            // The burst of a cut dead crop (the game's own leaf animation), tinted withered brown, over the whole plant.
            var brown = new Color(168, 128, 72);
            l.temporarySprites.Add(new TemporaryAnimatedSprite(12, at, brown, 8, Game1.random.NextDouble() < .5, 50f));
            l.temporarySprites.Add(new TemporaryAnimatedSprite(12, at + new Vector2(0, -48), brown, 8, Game1.random.NextDouble() < .5, 50f) { delayBeforeAnimationStart = 60 });
            __result = true; return false;
        }
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
    /// <summary>BSV's Tea Sapling is planted by the game's own Tea Sapling code (a one-item stand-in), then the new bush is
    /// marked as BSV tea. Season, watered soil and pots work as before.</summary>
    private static SObject StandIn() => ItemRegistry.Create<SObject>(TeaAppleRules.VanillaSapling);
    private static bool Ours(Item? item) => item?.QualifiedItemId == TeaAppleRules.Sapling;
    private static void IsTea(SObject __instance, ref bool __result) { if (Ours(__instance)) __result = true; }
    private static bool AsGameSapling(SObject __instance, MethodBase __originalMethod, object[] __args, ref bool __result)
    {
        if (!Ours(__instance)) return true;
        __result = (bool)__originalMethod.Invoke(StandIn(), __args)!;
        return false;
    }
    private static bool PlantOutside(SObject __instance, GameLocation location, int x, int y, MethodBase __originalMethod, object[] __args, ref bool __result)
    {
        if (!Ours(__instance)) return true;
        if (!TeaAppleRules.GrowingSeason((int)Game1.season) && location.IsOutdoors && !location.SeedsIgnoreSeasonsHere())
        { Game1.showRedMessage(BetterStardewValley.BsvI18n.Get(Instance.Helper, "tea.wrong-season")); __result = false; return false; }
        var tile = new Vector2(x / 64, y / 64);
        bool wet = location.terrainFeatures.TryGetValue(tile, out var soil) && soil is HoeDirt dirt && dirt.isWatered();
        __result = (bool)__originalMethod.Invoke(StandIn(), __args)!;
        if (__result && location.terrainFeatures.TryGetValue(tile, out var f) && f is Bush b && b.size.Value == Bush.greenTeaBush)
        {
            Own(b); Initialize(b, true);
            if (wet) Water(b);
        }
        return false;
    }
    // Garden pots are sheltered: a tea sapling may go into a pot in any season, exactly like vanilla.
    private static bool PlantPot(IndoorPot __instance, Item dropInItem, bool probe, MethodBase __originalMethod, object[] __args, ref bool __result)
    {
        if (!Ours(dropInItem)) return true;
        var args = (object[])__args.Clone(); args[0] = StandIn();
        bool wet = __instance.hoeDirt.Value?.isWatered() == true;
        __result = (bool)__originalMethod.Invoke(__instance, args)!;
        if (!probe && __result && __instance.bush.Value is { } b)
        {
            Own(b); Initialize(b, true);
            if (wet) Water(b);
        }
        return false;
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

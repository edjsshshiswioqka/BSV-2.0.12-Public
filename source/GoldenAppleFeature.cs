using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using SObject = StardewValley.Object;

namespace BetterStardewValley;

internal sealed partial class GoldenAppleFeature
{
    private static GoldenAppleFeature Instance = null!;
    private readonly IModHelper Helper;
    private readonly IMonitor Monitor;
    private readonly Dictionary<long, Window> Windows = new();
    private readonly Dictionary<long, string> LastVisualMessage = new();
    private double LastTick = -1;
    private bool DamageReady;
    private sealed class Window { public double Remaining; public long Serial; public double CreatedTick; }
    internal sealed class TriggerNotice { public long Farmer; public string Token = ""; }

    internal GoldenAppleFeature(IModHelper helper, IMonitor monitor)
    {
        Instance = this; Helper = helper; Monitor = monitor;
        var h = new Harmony(TeaAppleRules.Prefix + ".GoldenApple");
        void Patch(Type t, string method, string? prefix = null, string? postfix = null) => h.Patch(AccessTools.Method(t, method),
            prefix: prefix == null ? null : new HarmonyMethod(typeof(GoldenAppleFeature), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(GoldenAppleFeature), postfix));
        try
        {
        Patch(typeof(Farmer), nameof(Farmer.eatObject), nameof(BeforeEat), nameof(AfterEat));
        Patch(typeof(Farmer), nameof(Farmer.doneEating), postfix: nameof(DoneEating));
        Patch(typeof(SObject), nameof(SObject.staminaRecoveredOnConsumption), postfix: nameof(Energy));
        Patch(typeof(SObject), nameof(SObject.healthRecoveredOnConsumption), postfix: nameof(Health));
        Patch(typeof(CraftingRecipe), nameof(CraftingRecipe.createItem), postfix: nameof(CraftedQuality));
        h.Patch(AccessTools.Method(typeof(CraftingPage), "clickCraftingRecipe"),
            prefix: new HarmonyMethod(typeof(GoldenAppleFeature), nameof(BeforeCraft)),
            transpiler: new HarmonyMethod(typeof(GoldenAppleFeature), nameof(SeasoningTranspiler)));
        h.Patch(AccessTools.Method(typeof(Farmer), nameof(Farmer.takeDamage)),
            prefix: new HarmonyMethod(typeof(GoldenAppleFeature), nameof(BeforeDamage)),
            postfix: new HarmonyMethod(typeof(GoldenAppleFeature), nameof(AfterDamage)) { priority = Priority.Last },
            transpiler: new HarmonyMethod(typeof(GoldenAppleFeature), nameof(DamageTranspiler)));
        Patch(typeof(Farmer), nameof(Farmer.CanBeDamaged), postfix: nameof(CanBeDamaged));
        h.Patch(AccessTools.Method(typeof(FarmerRenderer), "draw", new[] { typeof(SpriteBatch), typeof(FarmerSprite.AnimationFrame),
            typeof(int), typeof(Rectangle), typeof(Vector2), typeof(Vector2), typeof(float), typeof(int), typeof(Color), typeof(float), typeof(float), typeof(Farmer) }),
            prefix: new HarmonyMethod(typeof(GoldenAppleFeature), nameof(Tint)));
        h.Patch(AccessTools.Method(typeof(Farmer), "draw", new[] { typeof(SpriteBatch) }),
            prefix: new HarmonyMethod(typeof(GoldenAppleFeature), nameof(BeforeDraw)),
            postfix: new HarmonyMethod(typeof(GoldenAppleFeature), nameof(AfterDraw)));
        }
        catch { h.UnpatchAll(TeaAppleRules.Prefix + ".GoldenApple"); throw; }
        helper.Events.GameLoop.SaveLoaded += (_, _) => Reconcile();
        helper.Events.GameLoop.DayStarted += (_, _) => Reconcile();
        helper.Events.GameLoop.TimeChanged += (_, _) => Reconcile();
        helper.Events.GameLoop.OneSecondUpdateTicked += (_, _) => Reconcile();
        helper.Events.GameLoop.UpdateTicked += Tick;
        helper.Events.Content.LocaleChanged += (_, _) =>
        {
            if (!Context.IsWorldReady) return;
            Game1.player.buffs.Remove(TeaAppleRules.Tenacity);
            Reconcile();
        };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { Windows.Clear(); LastVisualMessage.Clear(); LastTick = -1; };
        helper.Events.Multiplayer.ModMessageReceived += Message;
        helper.ConsoleCommands.Add("bsv_apple_status", BsvI18n.Get(helper, "cmd.apple"), (_, _) =>
        {
            if (!Context.IsWorldReady) { monitor.Log("Load a save first.", LogLevel.Info); return; }
            monitor.Log($"BSV Golden Apple: hook={DamageReady}, player={Game1.player.Name}, today={TeaFeature.Today}, lastEaten={Read(Game1.player, TeaAppleRules.EatenKey)}, armed={Armed(Game1.player)}, icon={Game1.player.hasBuff(TeaAppleRules.Tenacity)}, window={Active(Game1.player)}; invincibility={TeaAppleRules.InvincibleMilliseconds}ms; expiry=2600.", LogLevel.Info);
        });
        DamageReady = true;
    }
    internal static int Read(Farmer who, string key) =>
        TeaAppleRules.Number(who.modData.TryGetValue(key, out string? s) ? s : null);
    internal static bool Armed(Farmer who) => TeaAppleRules.IsArmed(Read(who, TeaAppleRules.EatenKey),
        Read(who, TeaAppleRules.ArmedKey), TeaFeature.Today, Game1.timeOfDay);
    private void Say(string key) => Game1.addHUDMessage(new HUDMessage(BetterStardewValley.BsvI18n.Get(Helper, key), 2));
    internal static bool Active(Farmer who) => Instance.Windows.TryGetValue(who.UniqueMultiplayerID, out var w) && w.Remaining > 0;
    private long Serial(Farmer who) => Windows.TryGetValue(who.UniqueMultiplayerID, out var w) ? w.Serial : 0;

    private static bool BeforeEat(Farmer __instance, SObject o)
    {
        if (o?.QualifiedItemId != TeaAppleRules.GoldenApple || !__instance.IsLocalPlayer) return true;
        if (Instance.DamageReady && TeaAppleRules.CanEat(Read(__instance, TeaAppleRules.EatenKey), TeaFeature.Today)) return true;
        // eatHeldObject consumes only when isEating remains true after eatObject returns.
        // Cancel before that native decrement, never "refund" a copy afterwards.
        __instance.isEating = false;
        __instance.completelyStopAnimatingOrDoingAction();
        __instance.forceCanMove();
        Instance.Say(Instance.DamageReady ? "apple.already-eaten" : "apple.unavailable");
        return false;
    }
    private static void AfterEat(Farmer __instance, SObject o, bool __runOriginal)
    {
        if (!__runOriginal || o?.QualifiedItemId != TeaAppleRules.GoldenApple || !__instance.IsLocalPlayer || !__instance.isEating) return;
        // The native eat call accepted the consumption. Reserve the allowance immediately so even
        // a second call during the animation can't consume another apple. This is per Farmer NetModData.
        __instance.modData[TeaAppleRules.EatenKey] = TeaAppleRules.Text(TeaFeature.Today);
        if (Game1.timeOfDay < TeaAppleRules.ExpiryTime)
            __instance.modData[TeaAppleRules.ArmedKey] = TeaAppleRules.Text(TeaFeature.Today);
        else __instance.modData.Remove(TeaAppleRules.ArmedKey);
    }
    private static void DoneEating(Farmer __instance)
    { if (__instance.IsLocalPlayer && __instance.itemToEat?.QualifiedItemId == TeaAppleRules.GoldenApple) Instance.Reconcile(); }
    private void Reconcile()
    {
        if (!Context.IsWorldReady) return;
        Farmer who = Game1.player;
        if (Utility.getHomeOfFarmer(who)?.upgradeLevel >= 1 && !who.cookingRecipes.ContainsKey(TeaAppleRules.AppleId))
            who.cookingRecipes.Add(TeaAppleRules.AppleId, 0);
        if (!Armed(who))
        {
            who.buffs.Remove(TeaAppleRules.Tenacity);
            who.modData.Remove(TeaAppleRules.ArmedKey);
            return; // keep LastEatenDay until the calendar changes, including after 2AM
        }
        if (!who.hasBuff(TeaAppleRules.Tenacity) && !who.isEating)
            who.applyBuff(new Buff(TeaAppleRules.Tenacity, TeaAppleRules.AppleId, BetterStardewValley.BsvI18n.Get(Helper, "apple.name"), Buff.ENDLESS,
                Helper.GameContent.Load<Texture2D>(TeaAppleData.TextureRoot + "Tenacity"), 0,
                displayName: BetterStardewValley.BsvI18n.Get(Helper, "tenacity.name"), description: BetterStardewValley.BsvI18n.Get(Helper, "tenacity.description", new { seconds = TeaAppleRules.InvincibleMilliseconds / 1000d })));
    }
    private static void Energy(SObject __instance, ref int __result)
    {
        if (__instance.QualifiedItemId == TeaAppleRules.Cup) __result = TeaAppleRules.CupEnergy;
        else if (__instance.QualifiedItemId == TeaAppleRules.GoldenApple) __result = TeaAppleRules.AppleEnergy;
    }
    private static void Health(SObject __instance, ref int __result)
    {
        if (__instance.QualifiedItemId == TeaAppleRules.Cup) __result = TeaAppleRules.CupHealth;
        else if (__instance.QualifiedItemId == TeaAppleRules.GoldenApple) __result = TeaAppleRules.AppleHealth;
    }
    private static void CraftedQuality(Item __result)
    { if (__result?.QualifiedItemId is TeaAppleRules.Cup or TeaAppleRules.GoldenApple) __result.Quality = 0; }
    internal static bool InOwnKitchen(CraftingPage page, Farmer who)
    {
        var home = Utility.getHomeOfFarmer(who);
        return TeaAppleRules.OwnKitchen(page.cooking, home != null && ReferenceEquals(who.currentLocation, home)
                && home.owner?.UniqueMultiplayerID == who.UniqueMultiplayerID,
            home?.upgradeLevel ?? 0, home?.GetFridge() is { } fridge
                && page._materialContainers?.Any(c => ReferenceEquals(c, fridge.Items)) == true);
    }
    private static bool BeforeCraft(CraftingPage __instance, ClickableTextureComponent c)
    {
        var recipe = __instance.pagesOfCraftingRecipes[__instance.currentCraftingPage][c];
        if (recipe.name == "Tea Sapling") { Instance.Say("tea.buy-saplings"); return false; }
        if (recipe.name != TeaAppleRules.AppleId || InOwnKitchen(__instance, Game1.player)) return true;
        Instance.Say("apple.kitchen-required"); return false;
    }
    internal static int SeasoningQuality(Item item) => item.QualifiedItemId == TeaAppleRules.GoldenApple ? -1 : item.Quality;

    private static void BeforeDamage(Farmer __instance, out long __state) => __state = Instance.Serial(__instance);
    internal static void SetDamageHealth(Farmer who, int value)
    {
        if (who.IsLocalPlayer && TeaAppleRules.Lethal(who.health, value, Armed(who)))
        {
            // Called in place of ONLY the native final damage assignment, after defense/randomness
            // and before trinkets, Phoenix Ring and all death handling.
            who.health = 1;
            who.modData.Remove(TeaAppleRules.ArmedKey);
            who.buffs.Remove(TeaAppleRules.Tenacity);
            long serial = Instance.Serial(who) + 1;
            Instance.Windows[who.UniqueMultiplayerID] = new Window { Serial = serial, Remaining = TeaAppleRules.InvincibleMilliseconds,
                CreatedTick = Game1.currentGameTime.TotalGameTime.TotalMilliseconds };
            Instance.Say("tenacity.triggered");
            if (Context.IsMultiplayer)
            {
                try { Instance.Helper.Multiplayer.SendMessage(new TriggerNotice { Farmer = who.UniqueMultiplayerID, Token = Guid.NewGuid().ToString("N") },
                    TeaAppleRules.TriggerMessage, new[] { TeaAppleRules.Prefix }); }
                catch (Exception ex) { Instance.Monitor.Log($"BSV Tenacity visual broadcast failed (protection is active): {ex.Message}", LogLevel.Warn); }
            }
        }
        else if (!who.IsLocalPlayer || !Active(who) || value >= who.health) who.health = value;
    }
    private static void AfterDamage(Farmer __instance, long __state)
    {
        if (Instance.Serial(__instance) == __state) return;
        __instance.health = 1;
        // CanBeDamaged owns the apple window. Native hit/ring protection remains independent.
    }
    private static void CanBeDamaged(Farmer __instance, ref bool __result)
    { if (__instance.IsLocalPlayer && Active(__instance)) __result = false; }
    private static void Tint(Farmer who, ref Color overrideColor)
    { if (Active(who)) overrideColor = new Color(255, 211, 71); }
    private static void BeforeDraw(Farmer __instance, out bool __state)
    {
        __state = __instance.flashDuringThisTemporaryInvincibility;
        if (Active(__instance)) __instance.flashDuringThisTemporaryInvincibility = false;
    }
    private static void AfterDraw(Farmer __instance, bool __state) => __instance.flashDuringThisTemporaryInvincibility = __state;
    private void Tick(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.currentGameTime == null) return;
        double tick = Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
        if (tick == LastTick) return; // split-screen shares this service, never age a window twice
        LastTick = tick;
        if (!Game1.shouldTimePass()) return; // native world pause; multiplayer menus alone do not pause the world
        double elapsed = Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
        foreach (var pair in Windows)
        {
            var w = pair.Value;
            if (tick == w.CreatedTick || w.Remaining <= 0) continue;
            w.Remaining = Math.Max(0, w.Remaining - elapsed);

        }
    }
    private void Message(object? sender, ModMessageReceivedEventArgs e)
    {
        if (!Context.IsWorldReady || e.FromModID != TeaAppleRules.Prefix || e.Type != TeaAppleRules.TriggerMessage) return;
        try
        {
            var notice = e.ReadAs<TriggerNotice>();
            if (notice.Farmer != e.FromPlayerID || !Guid.TryParseExact(notice.Token, "N", out _)
                || Game1.GetPlayer(notice.Farmer, true) is not { IsLocalPlayer: false }
                || LastVisualMessage.GetValueOrDefault(notice.Farmer) == notice.Token) return;
            LastVisualMessage[notice.Farmer] = notice.Token;
            Windows[notice.Farmer] = new Window { Remaining = TeaAppleRules.InvincibleMilliseconds,
                CreatedTick = Game1.currentGameTime.TotalGameTime.TotalMilliseconds };
        }
        catch (Exception ex) { Monitor.Log($"BSV ignored an invalid Tenacity visual message: {ex.Message}", LogLevel.Trace); }
    }
}

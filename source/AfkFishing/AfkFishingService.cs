using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace BetterStardewValley.AfkFishing;

internal sealed partial class AfkFishingService
{
    internal const string SaveKey = "Eddie.BetterStardewValley/AfkFishing.v1";
    internal const string ChestKey = "Eddie.BetterStardewValley/AfkChestId.v1";
    internal const string QueuePrefix = "Eddie.BetterStardewValley/AfkOutput/";
    private static AfkFishingService? Instance;
    private readonly IModHelper Helper;
    private readonly IMonitor Monitor;
    private readonly ModConfig Config;
    private readonly PerScreen<Session> Sessions = new(() => new Session());
    private Session Current => Sessions.Value;
    private sealed class Session
    {
        internal AfkSave Data = new();
        internal FishingRod? Rod;
        internal SObject? BaitTemplate;
        internal Chest? Chest;
        internal readonly AfkCatchLedger Catches = new();
        internal AfkArea? Area;
        internal List<Chest> Nearby = new();
        internal HashSet<Chest> Standby = new();
        internal double AreaAt;
        internal int LockToken;
        internal List<RouteStep>? Route;
        internal AfkRouteSearch? Search;
        internal readonly AfkNavigationMemory Navigation = new();
        internal int RouteIndex, CaptureDepth, Generation, WalkGeneration;
        internal bool LockPending, Enabling, AutoCasting, HomeIntent, Saving, Exiting;
        internal double ResumeAt, RetryAt, NoticeAt, ChestScanAt;
        internal TileNode? WalkTarget;
        internal bool WalkExit, WalkActive, WalkRefreshed;
        internal Vector2 WalkBefore, WalkVector;
        internal int RouteFailures, RecoveryCount, SmoothedIndex = -1;
        internal TileNode? RecoveryOrigin;
        internal Rectangle? ActorWaitBox;
        internal string WalkMap = "";
        internal double WalkSetAt;
        internal AfkRouteSearch? HomeSearch;
        internal double HomeEstimate = double.PositiveInfinity, HomeCheckAt;
        internal bool HomeEstimateReady;
        internal int WakeReadyFrames;
        internal bool WakePrepared, RunOwned, PreviousRunning;
        internal double RecoveryStartedAt = -1;
        internal TileNode? BedExit;
        internal bool WakeOwnsSleep, WakeControlReleased, WakeWaitWarned;
        internal string WakeWaitReason = "";
        internal double WakeWaitAt;
        internal string LastNotice = "";
        internal TileNode? Goal, ActorWaitTile, HomeOrigin;
        internal List<RouteStep>? HomeEstimateRoute;
        internal float MoveLimit;
        internal WalkTrace? Trace;
        internal bool NativePetPermission; internal NPC? NativePet; internal double NativePetSince,OverlapSince=-1;
        internal bool WorldAssetsDirty;
        internal string? ExternalControlWait;
    }

    internal AfkFishingService(IModHelper helper, IMonitor monitor, IManifest manifest, ModConfig config)
    {
        Helper = helper; Monitor = monitor; Config = config; Instance = this;
        string owner = manifest.UniqueID + ".AfkFishing";
        var harmony = new Harmony(owner);
        try { InstallPatches(harmony); }
        catch { harmony.UnpatchAll(owner); Instance = null; throw; }
        helper.Events.Input.ButtonPressed += OnButton;
        helper.Events.GameLoop.UpdateTicked += (_, _) => Guard(Tick);
        helper.Events.Content.AssetsInvalidated += (_, e) =>
        {
            if (e.NamesWithoutLocale.Any(n => n.IsEquivalentTo("Data/Objects") || n.IsEquivalentTo("Data/Crops")
                || n.IsEquivalentTo("Data/Fish") || n.IsEquivalentTo("Data/CookingRecipes"))) AfkFood.Invalidate();
            if (e.NamesWithoutLocale.Any(n => n.Name.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase)
                || n.IsEquivalentTo("Data/Locations") || n.IsEquivalentTo("Data/Buildings"))) InvalidateWorldAssets();
        };
        helper.Events.World.LocationListChanged += (_, _) => InvalidateWorldAssets();
        helper.Events.GameLoop.SaveLoaded += (_, _) => Guard(Load);
        helper.Events.GameLoop.DayStarted += (_, _) => Guard(NewDay);
        helper.Events.GameLoop.Saving += (_, _) => { if (Context.IsWorldReady) { Persist(); Current.Saving = true; ClearObstacleIntent(); StopWalking(); } };
        helper.Events.GameLoop.Saved += (_, _) => Current.Saving = false;
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            foreach (var pair in Sessions.GetActiveValues()) FinishWalkTrace(pair.Value, "returned to title");
            Current.Generation++; AfkFood.Invalidate(); Sessions.ResetAllScreens();
        };
        helper.Events.Player.Warped += (_, e) =>
        {
            if (!e.IsLocalPlayer || !Current.Data.Enabled) return;
            ClearObstacleIntent();Current.HomeSearch = null; Current.HomeCheckAt = 0;
            InvalidatePath(0.15);
        };
        helper.Events.World.ObjectListChanged += (_, e) => TopologyChanged(e.Location);
        helper.Events.World.TerrainFeatureListChanged += (_, e) => TopologyChanged(e.Location);
        helper.Events.World.BuildingListChanged += (_, e) => TopologyChanged(e.Location);
        helper.Events.World.LargeTerrainFeatureListChanged += (_, e) => TopologyChanged(e.Location);
        helper.Events.World.FurnitureListChanged += (_, e) => TopologyChanged(e.Location);
        helper.Events.Display.RenderedHud += Draw;
        helper.Events.Display.RenderedWorld += DrawRange;
        helper.ConsoleCommands.Add("bsv_afk_status", Tr("afk.command"), (_, _) =>
        {
            if (!Context.IsWorldReady) { monitor.Log(Tr("diag.load-save"), LogLevel.Info); return; }
            monitor.Log(Tr("afk.status", new { enabled = Current.Data.Enabled, state = Tr("afk.state." + Current.Data.Phase),
                pending = Pending(false)?.Count ?? 0, key = config.AfkFishing.ToggleKey }), LogLevel.Info);
            monitor.Log(RuntimeState(), LogLevel.Info);
            RefreshArea(force: true);
            monitor.Log(AreaText(), LogLevel.Info);
            if (Current.Area != null)
                foreach (var cell in Current.Area.Cells)
                {
                    var obj = Game1.player.currentLocation.getObjectAtTile(cell.Tile.X, cell.Tile.Y);
                    if (obj != null) monitor.Log($"AFK nearby ({cell.Tile.X},{cell.Tile.Y}): {obj.QualifiedItemId}; type={obj.GetType().FullName}; ground={cell.Ground}; supportedChest={AfkWorld.EligibleChest(obj)}", LogLevel.Info);
                }
        });
        monitor.Log(Tr("afk.loaded"), LogLevel.Debug);
        RegisterWalkTrace();
    }
    private static double Now => Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
    private string Tr(string key, object? tokens = null) => BsvI18n.Get(Helper, key, tokens);
    private void Notice(string key)
    {
        if (Current.LastNotice == key && Now - Current.NoticeAt < 10) return;
        Current.LastNotice = key; Current.NoticeAt = Now;
        Game1.addHUDMessage(new HUDMessage(Tr(key), 2));
    }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            Monitor.Log("BSV AFK action paused; native output inventory retained.\n" + ex, LogLevel.Error);
            Current.Data.Enabled = false; Current.Data.Phase = AfkPhase.Disabled;
            Current.Data.ReturnToSpot = false; Current.Data.WakePending = false; Current.Route = null; Current.Search = null;
            ClearObstacleIntent(); StopWalking();
            Current.Enabling = false; Current.Generation++;
            if (Context.IsWorldReady) { Game1.player.Halt(); Persist(); Notice("afk.error"); }
        }
    }
    private void Persist()
    {
        if (Context.IsWorldReady && Game1.player.IsLocalPlayer)
            Game1.player.modData[SaveKey] = JsonConvert.SerializeObject(Current.Data);
    }
    private Inventory? Pending(bool create)
    {
        string key = QueuePrefix + Game1.player.UniqueMultiplayerID;
        if (Game1.player.team.globalInventories.TryGetValue(key, out var value)) return value;
        return create ? Game1.player.team.GetOrCreateGlobalInventory(key) : null;
    }
    private void Load()
    {
        if (!Context.IsWorldReady) return;
        FinishWalkTrace(Current, "save loaded");
        Current.Generation++; StopWalking(); Sessions.Value = new Session(); AfkFood.Invalidate();
        if (Game1.player.modData.TryGetValue(SaveKey, out string? json))
        {
            var state = JsonConvert.DeserializeObject<AfkSave>(json, new JsonSerializerSettings { MaxDepth = 16, TypeNameHandling = TypeNameHandling.None });
            if (state?.Schema == 1) Current.Data = state;
        }
        if (Current.Data.PlayerId != 0 && Current.Data.PlayerId != Game1.player.UniqueMultiplayerID)
        { Current.Data.Enabled = false; Current.Data.SleepRequestedDay = -1; }
        Current.Data.PlayerId = Game1.player.UniqueMultiplayerID;
        Current.Data.OutputInventory = QueuePrefix + Game1.player.UniqueMultiplayerID;
        if (!Current.Data.Enabled || !ValidSnapshot(Current.Data.Spot)) { Current.Data.Enabled = false; Persist(); return; }
        // Turning off tomorrow's return must not discard ownership of yesterday's sleep.
        Current.WakeOwnsSleep = Current.Data.SleepRequestedDay >= 0 && Current.Data.SleepRequestedDay < CurrentDay
            || Current.Data.WakePending && Current.Data.SessionDay < CurrentDay && Current.Data.Phase == AfkPhase.Sleeping;
        int savedDay = Current.Data.SessionDay >= 0 ? Current.Data.SessionDay : Current.Data.SafetyDay;
        bool nextDay = savedDay >= 0 && savedDay < CurrentDay;
        Current.HomeIntent = !nextDay && SavedHomeIntent(Current.Data);
        if (nextDay) { Current.Data.RestReason = AfkRestReason.None; Current.Data.RestDay = -1; }
        else if (Current.HomeIntent && Current.Data.RestReason == AfkRestReason.None)
        { Current.Data.RestReason = AfkRestReason.Bedtime; Current.Data.RestDay = CurrentDay; }
        RestoreSafetyDay();
        Current.Data.SessionDay = CurrentDay; Current.Data.ReturnToSpot = Config.AfkFishing.ReturnToFishingSpotNextDay;
        Current.Data.WakePending = !Current.HomeIntent && (nextDay || Game1.player.currentLocation == Utility.getHomeOfFarmer(Game1.player));
        ClearChest();
        Current.Data.Phase = AfkPhase.ResumeCountdown; Current.ResumeAt = Now + 3;
        Persist();
        Notice("afk.resume");
    }
    private void NewDay()
    {
        Current.Saving = false; ClearObstacleIntent(); AfkFood.Invalidate();
        if (!Current.Data.Enabled || Current.Data.Spot == null) return;
        // SMAPI also raises DayStarted after loading a save. A same-day restored rest
        // intent must not be mistaken for completion of that night's sleep.
        if (Current.Data.SessionDay == CurrentDay) return;
        var previous = Current.Data.Phase;
        Current.WakeOwnsSleep = previous == AfkPhase.Sleeping || Current.Data.SleepRequestedDay >= 0 && Current.Data.SleepRequestedDay < CurrentDay;
        Current.WakeControlReleased = false; Current.WakeWaitReason = "";
        Current.WakePrepared = false; Current.BedExit = null; Current.RecoveryStartedAt = -1;
        StopWalking();
        Current.Generation++; Current.LockToken++; Current.LockPending = false; ClearChest();
        Current.Area = null; Current.Nearby.Clear(); Current.Standby.Clear(); Current.AreaAt = 0;
        Current.Route = null; Current.RouteIndex = 0; Current.Goal = null; Current.Exiting = false; Current.SmoothedIndex = -1;
        Current.Rod = null; Current.AutoCasting = false; Current.HomeIntent = false;
        Current.RetryAt = 0; Current.Search = null; Current.Navigation.Clear(); Current.RouteFailures = 0;
        Current.RecoveryCount = 0; Current.RecoveryOrigin = null; Current.ActorWaitTile = null; Current.ActorWaitBox = null;
        Current.HomeSearch = null; Current.HomeCheckAt = 0; Current.HomeEstimate = double.PositiveInfinity;
        Current.HomeEstimateReady = false;
        Current.WakeReadyFrames = 0; Current.Data.WakePending = true; Current.Data.ReturnToSpot = Config.AfkFishing.ReturnToFishingSpotNextDay;
        Current.Data.RestReason = AfkRestReason.None; Current.Data.RestDay = -1; Current.Data.SessionDay = CurrentDay;
        RestoreSafetyDay();
        // SaveLoaded already gave a cancellation window; don't erase it on the first DayStarted.
        if (Current.Data.Phase != AfkPhase.ResumeCountdown) Current.Data.Phase = AfkPhase.WakingUp;
        Persist(); TraceState(previous, "new day ready: waiting for world/player initialization");
    }

    private bool KeyConflict(SButton button) => button == SButton.None || button == HorseFeature.CallKey

        || button is SButton.T or SButton.ControllerX or SButton.W or SButton.A or SButton.S or SButton.D or SButton.Escape
        or SButton.MouseLeft or SButton.MouseRight || button.IsActionButton() || button.IsUseToolButton();
    internal static bool StopsAfk(SButton button)
    {
        if (button is SButton.MouseLeft or SButton.MouseRight or SButton.MouseMiddle
            or SButton.W or SButton.A or SButton.S or SButton.D or SButton.Up or SButton.Down or SButton.Left or SButton.Right
            or SButton.DPadUp or SButton.DPadDown or SButton.DPadLeft or SButton.DPadRight
            or SButton.LeftThumbstickUp or SButton.LeftThumbstickDown or SButton.LeftThumbstickLeft or SButton.LeftThumbstickRight) return true;
        // The player's own remapped movement keys.
        var o = Game1.options;
        return o != null && button.TryGetKeyboard(out var key)
            && new[] { o.moveUpButton, o.moveDownButton, o.moveLeftButton, o.moveRightButton }.Any(list => list?.Any(b => b.key == key) == true);
    }
    private void OnButton(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady) return;
        if (MultiplayerRescue.RescueService.SuspendsAfk(Game1.player)) return;
        Guard(() =>
        {
            if (e.Button == Config.AfkFishing.ToggleKey)
            {
                if (Current.Data.Enabled || Current.Enabling) { Helper.Input.Suppress(e.Button); Stop(); return; }
                if (!Config.AfkFishing.Enabled) return;
                if (KeyConflict(e.Button) || Helper.Input.IsSuppressed(e.Button)) { Notice("afk.key-conflict"); return; }
                Helper.Input.Suppress(e.Button); Enable(); return;
            }
            if (!(Current.Data.Enabled || Current.Enabling)) return;
            // Only the four movement directions and mouse clicks hand control back. Every other key
            // (menus, chat, hotbar, Esc...) keeps AFK running; moving the mouse or the wheel does nothing.
            if (StopsAfk(e.Button)) { Stop(); return; }
            // Tool/action keys would reel in or swing the rod and break the running cast: ignored while AFK.
            if (Game1.activeClickableMenu == null && (e.Button.IsUseToolButton() || e.Button.IsActionButton())) Helper.Input.Suppress(e.Button);
        });
    }
    private bool SafeWorld => Context.IsWorldReady && !Current.Saving && !Game1.eventUp && !Game1.isFestival()
        && Game1.currentMinigame == null && !Game1.player.passedOut && !Game1.fadeToBlack && !Game1.newDay
        && ExternalControlReason() == null;
    private void Enable()
    {
        var who = Game1.player;
        if (!SafeWorld || Game1.activeClickableMenu != null || !who.IsLocalPlayer || who.isRidingHorse()
            || who.ignoreCollisions || who.UsingTool || who.isEating || !who.CanMove || who.freezePause > 0)
        { Notice("afk.start-busy"); return; }
        int slot = FindRodSlot(who);
        if (slot < 0) { Notice("afk.no-rod"); return; }
        var rod = (FishingRod)who.Items[slot];
        if (!TryMaximumLanding(who.currentLocation, who.StandingPixel, who.FacingDirection, rod, who, out var water, out var area))
        { Notice("afk.invalid-cast"); return; }
        var world = new AfkWorld(who); var start = AfkWorld.At(who); var bed = world.Bed();
        if (!bed.HasValue || !AfkWorld.Supported(who.currentLocation)) { Notice("afk.unsupported"); return; }
        var homeRoute = world.Route(start, bed.Value);
        double estimate = EstimateRoute(homeRoute);
        var policy = AfkReturnRules.ClassifyManual(Game1.timeOfDay, TimeRemaining(), estimate);
        if (policy != AfkNightPolicy.LateManual && (homeRoute == null || world.Route(bed.Value, start) == null))
        { Notice("afk.unsupported"); return; }
        // Select an existing inventory slot only. Never replace, clone or recreate either item.
        who.CurrentToolIndex = slot;
        Current.Generation++; Current.LockPending = false; Current.Enabling = false;
        Current.Catches.Clear(); Current.Area = null; Current.Nearby.Clear(); Current.Standby.Clear();
        Current.Rod = rod; Current.Chest = null; Current.ChestScanAt = 0; Current.RetryAt = 0;
        Current.Data.Spot = new FishingSpot { Location = start.Map, X = start.X, Y = start.Y, Facing = who.FacingDirection,
            WaterX = water.X, WaterY = water.Y, FishArea = area, CastPower = MaximumCastPower,
            StandingOffsetX = who.StandingPixel.X - start.X * 64, StandingOffsetY = who.StandingPixel.Y - start.Y * 64,
            RodId = rod.QualifiedItemId, RodSlot = slot };
        Current.Data.PlayerId = who.UniqueMultiplayerID;
        Current.Data.Enabled = true; Current.Data.OutputInventory = QueuePrefix + who.UniqueMultiplayerID;
        Current.Data.Phase = AfkPhase.Fishing; Current.HomeIntent = false;
        Current.Data.NightPolicy = policy; Current.Data.SafetyDay = CurrentDay;
        Current.Data.ActivationDay = CurrentDay; Current.Data.ActivationTime = Game1.timeOfDay;
        Current.Data.RestReason = AfkRestReason.None; Current.Data.RestDay = -1; Current.Data.SessionDay = CurrentDay;
        Current.Data.WakePending = false; Current.Data.ReturnToSpot = false;
        Current.BaitTemplate = null; Current.Data.BaitPreference = "";
        Current.HomeEstimate = estimate; Current.HomeSearch = null; Current.HomeCheckAt = Now + 3;
        Current.HomeEstimateReady = true;
        RememberBait(); Pending(true); Reconnect(); Persist(); Notice("afk.enabled");
        if (policy == AfkNightPolicy.LateManual)
        { StartHome(); Notice("afk.late-manual"); Tick(); return; }
        // The same cycle gate serves first cast, resumed casts, and next-day casts.
        // A missing/busy chest cannot veto activation.
        Tick();
    }
    internal static int FindRodSlot(Farmer who)
    {
        if (who.CurrentTool is FishingRod) return who.CurrentToolIndex;
        for (int i = 0; i < who.Items.Count; i++) if (who.Items[i] is FishingRod) return i;
        return -1;
    }
    private void Stop()
    {
        var previous = Current.Data.Phase;
        ReleaseOwnedWakeControl();
        StopWalking();ClearObstacleIntent();
        Current.Generation++; Current.Enabling = false; Current.LockPending = false;
        ClearChest(); Current.Area = null; Current.Nearby.Clear(); Current.Standby.Clear(); Current.Catches.Clear();
        Current.Data.Enabled = false; Current.Data.Phase = AfkPhase.Disabled;
        Current.BaitTemplate = null; Current.Data.BaitPreference = "";
        Current.Data.ReturnToSpot = false; Current.Data.WakePending = false;
        Current.Data.RestReason = AfkRestReason.None; Current.Data.RestDay = -1;
        Current.Route = null; Current.Search = null; Current.Navigation.Clear(); Current.RouteFailures = 0; Current.Goal = null; Current.AutoCasting = false; Current.HomeIntent = false;
        if (Game1.player.controller == null) Game1.player.Halt();
        Persist(); TraceState(previous, "manual stop: automatic rest/resume canceled"); Notice("afk.disabled");
        // Do not cancel/roll back a fish, eating animation or sleep transaction already committed.
        // Deferred outputs remain in the native queue and will return to free backpack slots.
    }
    private void BindChest(Chest chest)
    {
        Current.Chest = chest;
        Current.Data.ChestId = chest.modData.TryGetValue(ChestKey, out string? id) ? id : "";
        Current.Data.ChestMap = chest.Location.NameOrUniqueName;
        Current.Data.ChestX = (int)chest.TileLocation.X; Current.Data.ChestY = (int)chest.TileLocation.Y;
    }
    private bool ChestLive(Chest chest) => Config.AfkFishing.EnableChestConnection && AfkChestArea.Accessible(chest, Game1.player);
    private void ClearChest()
    {
        Current.Chest = null; Current.Data.ChestId = ""; Current.Data.ChestMap = "";
        Current.Data.ChestX = 0; Current.Data.ChestY = 0;
    }
    private bool Reconnect()
    {
        RefreshArea(force: true);
        Current.ChestScanAt = Now + 0.25;
        return true; // optional; only individual valid cells participate in chest linking.
    }
    private void WithChest(Chest chest, Action work)
    {
        if (Current.LockPending || !Current.Data.Enabled || !ChestLive(chest) || !SafeTransfer()) return;
        var mutex = chest.GetMutex();
        if (mutex.IsLocked()) return;
        var session = Current; int generation = session.Generation, token = ++session.LockToken;
        long farmer = Game1.player.UniqueMultiplayerID;
        session.LockPending = true;
        mutex.RequestLock(() =>
        {
            try
            {
                if (Current == session && session.Generation == generation && session.LockToken == token
                    && session.Data.Enabled && Config.AfkFishing.Enabled && Config.AfkFishing.EnableChestConnection
                    && Context.IsWorldReady && Game1.player.UniqueMultiplayerID == farmer
                    && SafeTransfer() && mutex.IsLockHeld()
                    && AfkChestArea.Accessible(chest, Game1.player, ownsLock: true)) work();
            }
            catch (Exception ex) { Guard(() => throw new InvalidOperationException("AFK chest transaction", ex)); }
            finally
            {
                if (session.LockToken == token) session.LockPending = false;
                if (mutex.IsLockHeld()) mutex.ReleaseLock();
            }
        }, () => { if (session.LockToken == token) { session.LockPending = false; session.RetryAt = Now + 0.5; } });
        mutex.Update(chest.Location);
    }
    private bool SafeTransfer() => SafeWorld && Game1.player.IsLocalPlayer && !Current.Saving && !Current.WorldAssetsDirty
        && !Traveling(Current.Data.Phase) && !Current.HomeIntent && !Game1.player.UsingTool && !Game1.player.isEating
        && !CommittedCatch(Current.Rod) && Current.Rod?.isFishing != true && Game1.activeClickableMenu == null;

    private void Pause(AfkPhase phase, string message)
    {
        Current.Data.Phase = phase; Current.Route = null; Current.RetryAt = Now + 2;
        Game1.player.Halt(); Persist(); Notice(message);
    }
    private void Tick()
    {
        CaptureWalkTrace();
        if (!Context.IsWorldReady || Current.Saving || !Game1.player.IsLocalPlayer || Game1.player.currentLocation?.Map == null) return;
        if (MultiplayerRescue.RescueService.SuspendsAfk(Game1.player)) { YieldForRescue(); return; }
        var s = Current; var who = Game1.player;
        if (!Config.AfkFishing.Enabled && (s.Data.Enabled || s.Enabling)) Stop();
        if(!Config.AfkFishing.ReturnToFishingSpotNextDay && s.Data.Enabled && !s.HomeIntent && !s.Data.WakePending
            && s.Data.Phase is AfkPhase.ReturningToSpot or AfkPhase.ResumingFishing)Stop();
        RefreshWorldAssets();
        if (s.Data.Enabled) RefreshArea();
        if (!s.Data.Enabled)
        {
            // A canceled morning trip still owes cleanup of its own completed sleep,
            // but must not take over native menus, events or another controller.
            ReleaseOwnedWakeControl();
            if (Game1.activeClickableMenu == null && !Game1.eventUp && !who.UsingTool && !who.isEating && Now >= s.RetryAt)
            { s.RetryAt = Now + 1; DrainBackpack(); }
            return;
        }
        // Date reconciliation precedes the Sleeping early return. A missed or delayed
        // DayStarted cannot strand the previous day's session behind that gate.
        if (s.Data.SessionDay >= 0 && s.Data.SessionDay < CurrentDay && !Game1.showingEndOfNightStuff && !Game1.newDay) NewDay();
        if (s.Data.Phase == AfkPhase.Sleeping || Game1.showingEndOfNightStuff) { HandleNightMenus(); return; }
        if (s.Data.Phase == AfkPhase.ResumeCountdown)
        {
            if (Now < s.ResumeAt) return;
            s.Data.Phase = s.Data.WakePending ? AfkPhase.WakingUp : s.HomeIntent ? AfkPhase.ReturningHome : AfkPhase.ReturningToSpot;
        }
        // Total recovery deadline is independent of the in-game clock, path retries and blockers.
        if (s.Data.WakePending && !RecoveryWithinDeadline()) return;
        string? control = ExternalControlReason();
        if (control != null)
        {
            StopWalking();
            if (s.ExternalControlWait != control)
                Monitor.Log("BSV AFK yielding control: " + control, LogLevel.Debug);
            s.ExternalControlWait = control;
            return;
        }
        if (s.ExternalControlWait != null)
        {
            s.ExternalControlWait = null;
            s.HomeCheckAt = 0; s.HomeSearch = null; s.AreaAt = 0;
            InvalidatePath(0.15);
            Monitor.Log("BSV AFK external control released; revalidating the saved task.", LogLevel.Debug);
        }
        if (s.Data.WakePending && !ResumeAfterWake()) return;
        // Observe before menu/catch/storage gates: even a full bag or an in-flight
        // result cannot defer the five-percent latch and allow another cast.
        if (!s.HomeIntent && AfkRules.LowEnergy(who.Stamina, who.MaxStamina, Config.AfkFishing.ReturnStaminaThreshold)) RequestRest(AfkRestReason.LowEnergy);
        if (s.HomeIntent && Game1.activeClickableMenu is DialogueBox && who.currentLocation == Utility.getHomeOfFarmer(who)
            && who.currentLocation.lastQuestionKey == "Sleep" && BedFurniture.IsBedHere(who.currentLocation, who.TilePoint.X, who.TilePoint.Y))
        { Game1.exitActiveMenu(); Sleep(); return; }
        if (!SafeWorld || (Game1.activeClickableMenu != null && !IsOwnedFishingMenu()))
        { StopWalking(); return; }
        if (who.isEating) { s.Data.Phase = AfkPhase.Eating; return; }
        if (who.ignoreCollisions || who.isRidingHorse()) { Stop(); return; }
        if (s.Data.Phase == AfkPhase.PausedMissingRod)
        {
            if (Now < s.RetryAt) return;
            s.RetryAt = Now + 2;
            if (!RestoreSavedRod()) return;
            s.Data.Phase = s.Data.ReturnToSpot ? AfkPhase.ReturningToSpot : AfkPhase.Fishing;
            InvalidatePath(); Persist();
        }
        // The cutoff is latched: a menu, a full chest or an in-flight result can't start a new cast.
        UpdateReturnBudget();
        if (ReturnDue() && !s.HomeIntent)
        { RequestRest(AfkRestReason.Bedtime); }
        if (Traveling(s.Data.Phase))
        {
            if (TryEmergencyReturn()) return;
            s.WalkRefreshed = false; Travel();
            if (!s.WalkRefreshed) StopWalking();
            return;
        }
        var rod = s.Rod ?? who.CurrentTool as FishingRod;
        if (rod != null && ReferenceEquals(who.CurrentTool, rod))
        {
            if (rod.fishCaught) { s.Data.Phase = s.HomeIntent ? AfkPhase.WaitingForSafeEnd : AfkPhase.ResolvingCatch; rod.doneHoldingFish(who); return; }
            if (rod.hit || rod.isReeling || rod.pullingOutOfWater || rod.showingTreasure || rod.castedButBobberStillInAir
                || rod.isCasting || rod.isTimingCast) { s.Data.Phase = s.HomeIntent ? AfkPhase.WaitingForSafeEnd : AfkPhase.ResolvingCatch; return; }
            if (rod.isFishing)
            {
                if (s.HomeIntent || AfkWorld.DynamicBlocked(who.currentLocation,who.GetBoundingBox(),who)) { rod.doneFishing(who, false); who.completelyStopAnimatingOrDoingAction(); s.AutoCasting = false; return; }
                if (rod.isNibbling) { s.Data.Phase = AfkPhase.ResolvingCatch; rod.DoFunction(who.currentLocation, 0, 0, 1, who); }
                else s.Data.Phase = AfkPhase.WaitingForBite;
                return;
            }
        }
        if (who.UsingTool || who.freezePause > 0) return;
        s.AutoCasting = false;
        if (s.HomeIntent) { StartHome(); return; }
        if(TryAvoidOverlap())return;
        if (Now < s.RetryAt) return;
        if (s.LockPending) return; // no cast/tool switch while a container callback is outstanding.
        Reconnect();
        // Capacity can have changed since this committed output was queued (including
        // while paused or across a reload). Always retry native backpack stacking first.
        DrainBackpack();
        // Backpack remains the first destination at capture. Later, only credited catches
        // and one committed overflow result may move to a live chest, one lock at a time.
        var candidates = AfkChestArea.Candidates(who, s.Chest);
        var chest = candidates.FirstOrDefault(c => CanStoreOutput(c));
        if (chest != null)
        {
            WithChest(chest, () =>
            {
                BindChest(chest);
                var items = chest.GetItemsForPlayer(who.UniqueMultiplayerID);
                s.Data.Phase = AfkPhase.StoringOutput;
                var pending = Pending(false);
                if (pending?.Count > 0)
                    AfkTransfer.Move<Item>(pending, items, chest.GetActualCapacity(), i => i.Stack,
                        (i, n) => i.Stack = n, (a, b) => a.canStackWith(b), i => i.maximumStackSize(), i => i.getOne());
                s.Catches.Store(who.Items, items, chest.GetActualCapacity());
                Persist();
            });
            return; // capacity and candidates refreshed next tick; partial leftovers choose a backup.
        }
        if (AfkRules.Hungry(who.Stamina, who.MaxStamina))
        {
            // Local inventory belongs to this player; do not acquire a container lock
            // or switch the selected rod to eat a carried meal.
            if (FoodSlot(who.Items) >= 0) { TryEatFrom(who.Items); return; }
            chest = candidates.FirstOrDefault(c => FoodSlot(c.GetItemsForPlayer(who.UniqueMultiplayerID)) >= 0);
            if (chest != null)
            {
                WithChest(chest, () =>
                {
                    // Inventory and energy may change while waiting for the mutex.
                    // Preserve backpack priority and re-evaluate the food, not a stale slot.
                    if (FoodSlot(who.Items) >= 0) TryEatFrom(who.Items);
                    else if (TryEat(chest)) BindChest(chest);
                });
                return;
            }
        }
        RememberBait();
        if (NeedsBait()) TryTopUpBaitFrom(who.Items);
        chest = s.Chest;
        if (chest != null && NeedsBait())
        {
            WithChest(chest, () => TryTopUpBait(chest));
            if (s.LockPending) return;
        }
        ContinueCycle();
    }
    private bool CanStoreOutput(Chest chest)
    {
        var pending = Pending(false);
        var output = Game1.player.Items.Where(i => Current.Catches.Allowed(i) > 0)
            .Concat(pending ?? Enumerable.Empty<Item>());
        var items = chest.GetItemsForPlayer(Game1.player.UniqueMultiplayerID);
        return output.Any(item => item != null && AfkTransfer.HasRoom(items, chest.GetActualCapacity(), item,
            i => i.Stack, (a, b) => a.canStackWith(b), i => i.maximumStackSize()));
    }
    private static int FoodSlot(IList<Item> inventory)
    {
        var who = Game1.player;
        for (int slot = 0; slot < inventory.Count; slot++)
        {
            var item = inventory[slot];
            if (item != null && item.Stack > 0 && Food(item)
                && !who.hasBuff(Game1.objectData.TryGetValue(item.ItemId, out var data) && data.IsDrink ? "7" : "6"))
                return slot;
        }
        return -1;
    }
    private bool TryEat(Chest chest) => TryEatFrom(chest.GetItemsForPlayer(Game1.player.UniqueMultiplayerID));
    private bool TryEatFrom(IList<Item> inventory)
    {
        var s = Current; var who = Game1.player;
        if (!s.Data.Enabled || !SafeTransfer() || s.HomeIntent || Game1.timeOfDay >= AfkRules.HomeTime
            || !AfkRules.Hungry(who.Stamina, who.MaxStamina) || AfkRules.LowEnergy(who.Stamina, who.MaxStamina, Config.AfkFishing.ReturnStaminaThreshold)) return false;
        int slot = FoodSlot(inventory);
        if (slot < 0 || inventory[slot] is not SObject edible) return false;
        var single = (SObject)edible.getOne(); single.Stack = 1;
        int before = edible.Stack;
        edible.Stack--; if (edible.Stack == 0) inventory[slot] = null!;
        try
        {
            who.eatObject(single, overrideFullness: false);
            if (!who.isEating) { edible.Stack = before; inventory[slot] = edible; s.RetryAt = Now + 1; }
            else s.Data.Phase = AfkPhase.Eating;
        }
        catch { if (!who.isEating) { edible.Stack = before; inventory[slot] = edible; } throw; }
        return who.isEating;
    }

    private void ContinueCycle()
    {
        var s = Current; var who = Game1.player;
        if (!s.Data.Enabled || who.UsingTool || who.isEating || !SafeWorld || Game1.activeClickableMenu != null) return;
        // Exhaustion and the existing bedtime latch still win over full inventories.
        if (!s.HomeIntent && UnsafeNextCast()) RequestRest(AfkRestReason.LowEnergy);
        if (s.HomeIntent || AfkRules.GoHome(Game1.timeOfDay, who.Stamina, who.MaxStamina, Config.AfkFishing.ReturnStaminaThreshold)) { StartHome(); return; }
        if (Pending(false)?.Count > 0 || (!BackpackMayAcceptCatch(who) && !ChestMayAcceptCatch()))
        { Pause(AfkPhase.PausedStorageFull, "afk.storage-full"); return; }
        Cast();
    }
    private bool ChestMayAcceptCatch() => AfkChestArea.Candidates(Game1.player, Current.Chest).Any(ChestHasCapacityHint);
    private static bool ChestHasCapacityHint(Chest chest)
    {
        var items = chest.GetItemsForPlayer(Game1.player.UniqueMultiplayerID);
        return items.Count < chest.GetActualCapacity() || items.Any(i => i == null || i.Stack < i.maximumStackSize());
    }
    internal static bool BackpackMayAcceptCatch(Farmer who) => who.Items.Count < who.MaxItems
        || who.Items.Any(i => i == null || i.Stack < i.maximumStackSize());
    // A free/partial stack is only a capacity hint: if the next native catch doesn't fit,
    // RouteNewOutput retains that exact item and ContinueCycle blocks all subsequent casts.
    internal static bool Food(Item? item) => AfkFood.Allowed(item);
    private void DrainBackpack()
    {
        var output = Pending(false); if (output == null) return;
        for (int i = output.Count - 1; i >= 0; i--)
        {
            var item = output[i]; if (item == null) { output.RemoveAt(i); continue; }
            // Native stacking/OnItemReceived and collection hooks still run. Partial remainder
            // remains the same Item, including quality, modData, flavored products and equipment.
            var before = AfkCatchLedger.Snapshot(Game1.player.Items);
            var remainder = Game1.player.addItemToInventory(item);
            if (Current.Data.Enabled) Current.Catches.Received(Game1.player.Items, before);
            if (remainder == null) output.RemoveAt(i); else output[i] = remainder;
        }
    }
    private void Draw(object? sender, RenderedHudEventArgs e)
    {
        if (!Context.IsWorldReady || !Current.Data.Enabled 
            || Game1.activeClickableMenu != null || Game1.eventUp || !Game1.displayHUD) return;
        string text = Tr("afk.indicator", new { state = Tr("afk.state." + Current.Data.Phase),
            energy = (int)(100 * Game1.player.Stamina / Math.Max(1, Game1.player.MaxStamina)), key = Config.AfkFishing.ToggleKey });
        text = (Config.AfkFishing.ShowIndicator ? text + "\n" : "") + AreaText();
        var measured = Game1.smallFont.MeasureString(text);
        float scale = Math.Min(0.8f, (Game1.uiViewport.Width - 24) / Math.Max(1f, measured.X));
        var size = measured * scale;
        int x = Math.Max(8, Game1.uiViewport.Width / 2 - (int)size.X / 2);
        e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle(x - 6, 4, (int)size.X + 12, (int)size.Y + 8), Color.Black * 0.7f);
        e.SpriteBatch.DrawString(Game1.smallFont, text, new Vector2(x, 8), Color.Wheat, 0, Vector2.Zero, scale, SpriteEffects.None, 1);
    }
}

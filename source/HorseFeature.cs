using System.Diagnostics;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Menus;

namespace BetterStardewValley;

internal sealed partial class HorseFeature
{
    private const string Tag = "Eddie.BetterStardewValley/";
    private const string NameKey = Tag + "HorseName", SelectedKey = Tag + "SelectedHorse", RunKey = Tag + "HorseRun";
    private const string RequestType = "BSV.Horse.Request.v1", ReplyType = "BSV.Horse.Receipt.v1", ViewType = "BSV.Horse.View.v1";
    private static HorseFeature? Instance;
    private static readonly AccessTools.FieldRef<Character, bool> Frozen = AccessTools.FieldRefAccess<Character, bool>("freezeMotion");
    private readonly IModHelper Helper;
    private readonly IMonitor Monitor;
    private readonly string ModId;
    private readonly ModConfig Config;
    private readonly HashSet<string> Warnings = new();
    private readonly PerScreen<LocalState> Local = new(() => new LocalState());
    private readonly Dictionary<Guid, Summon> Active = new();
    private readonly Dictionary<long, (HorseViewport View, double Time)> Views = new();
    private readonly Dictionary<long, HorseReceipt> Replies = new();
    private readonly Dictionary<long, HorseRequest> Incoming = new();
    private readonly HorseRequestGate Calls = new(), Interactions = new();
    private SoundEffect? Whistle, Dismiss;
    private bool Ready;
    internal bool Initialized => Ready;
    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    private sealed class LocalState
    {
        internal double NextCall, NextView, PendingSince;
        internal string Pending = "";
        internal string LastReceipt = "";
        internal bool Busy;
        internal readonly Dictionary<Guid, (string Token, Vector2 Position, double Phase)> Animations = new();
        internal readonly HashSet<Guid> CoatOffered = new();
        internal double CarpenterSeen, NextCoatCheck;
    }

    internal static bool ScaleEligible(Horse horse) => Instance is { Ready: true } mod && mod.Config.EnableLargeHorses && IsRealManagedHorse(horse);

    internal HorseFeature(IModHelper helper, IMonitor monitor, string modId, ModConfig config)
    {
        Helper = helper; Monitor = monitor; ModId = modId; Config = config; Instance = this;
        // Cooldown between a horse leaving and the next fresh call. Turning a running horse around is never delayed.
        Config.CallHorseCooldownSeconds = double.IsFinite(Config.CallHorseCooldownSeconds) ? Math.Clamp(Config.CallHorseCooldownSeconds, .25, 3) : 1;
        Config.HorseStopDistance = Math.Clamp(Config.HorseStopDistance, 1, 8);
        Config.HorseSpawnOutsideViewportMinTiles = Math.Clamp(Config.HorseSpawnOutsideViewportMinTiles, 2, 12);
        Config.HorseSpawnOutsideViewportMaxTiles = Math.Clamp(Config.HorseSpawnOutsideViewportMaxTiles, Config.HorseSpawnOutsideViewportMinTiles, 16);
        Config.HorseCallBlacklist ??= Array.Empty<string>();
        var harmony = new Harmony(modId + ".Horses");
        try
        {
            void Patch(Type type, string method, string? before = null, string? after = null, Type[]? args = null)
            {
                var target = AccessTools.Method(type, method, args) ?? throw new MissingMethodException(type.FullName, method);
                harmony.Patch(target, before is null ? null : new HarmonyMethod(typeof(HorseFeature), before) { priority = Priority.First },
                    after is null ? null : new HarmonyMethod(typeof(HorseFeature), after) { priority = Priority.Last });
            }
            Patch(typeof(Horse), nameof(Horse.checkAction), nameof(BeforeAction));
            Patch(typeof(Farmer), nameof(Farmer.getMovementSpeed), after: nameof(AfterMovementSpeed));
            Patch(typeof(Horse), nameof(Horse.update), after: nameof(AfterHorseUpdate), args: new[] { typeof(GameTime), typeof(GameLocation) });
            Patch(typeof(Stable), nameof(Stable.updateHorseOwnership), nameof(BeforeOwnership), nameof(AfterOwnership));
            Patch(typeof(Stable), nameof(Stable.grabHorse), nameof(BeforeGrab), nameof(AfterOwnership));
            Patch(typeof(Stable), nameof(Stable.performActionOnDemolition), nameof(BeforeDemolition));
            Patch(typeof(Building), nameof(Building.doAction), nameof(BeforeStableAction), args: new[] { typeof(Vector2), typeof(Farmer) });
            HorseSkins.Register(helper, monitor);
            HorseScale.Install(harmony, monitor);
            Ready = true;
        }
        catch (Exception ex)
        {
            harmony.UnpatchAll(modId + ".Horses");
            Monitor.Log("BSV horse feature could not initialize: " + ex, LogLevel.Error);
            return;
        }
        helper.Events.Input.ButtonPressed += OnButton;
        helper.Events.GameLoop.UpdateTicked += OnTick;
        helper.Events.Display.RenderedWorld += DrawNames;
        helper.Events.Multiplayer.ModMessageReceived += OnMessage;
        helper.Events.World.BuildingListChanged += (_, e) =>
        {
            if (Context.IsMainPlayer) foreach (var stable in e.Removed.OfType<Stable>()) CleanupStable(stable);
            // The player placing a stable at Robin's is its builder: they choose the coat when it is finished.
            if (Game1.activeClickableMenu is CarpenterMenu || Now - Local.Value.CarpenterSeen < 3)
                foreach (var stable in e.Added.OfType<Stable>().Where(s => !IsTractorStable(s))) MarkBuilder(stable);
        };
        helper.Events.GameLoop.SaveLoaded += (_, _) => { ResetLocal(); if (Context.IsMainPlayer) RecoverAll(); };
        helper.Events.GameLoop.DayStarted += (_, _) => { if (Context.IsMainPlayer) RecoverAll(); Local.Value.CoatOffered.Clear(); };
        helper.Events.GameLoop.GameLaunched += (_, _) => FindKeyConflicts();
        // Told once per player: while another mod uses the call key, BSV listens to J instead.
        helper.Events.GameLoop.DayStarted += (_, _) =>
        {
            if (CallKey == Config.CallHorseKey || Game1.player.modData.ContainsKey(KeyNoticeKey)) return;
            Game1.player.modData[KeyNoticeKey] = "1";
            Game1.drawObjectDialogue(T("horse.key-moved", new { mod = KeyConflictMod, key = Config.CallHorseKey.ToString() }));
        };
        helper.Events.GameLoop.Saving += (_, _) => { if (Context.IsMainPlayer) StopAll(); };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { ResetLocal(); if (Context.IsMainPlayer) ClearHost(); };
        helper.Events.Multiplayer.PeerDisconnected += (_, e) =>
        {
            if (!Context.IsMainPlayer) return;
            Views.Remove(e.Peer.PlayerID);
            foreach (var state in Active.Values.Where(s => s.Actor == e.Peer.PlayerID).ToArray()) Finish(state, "");
        };
        helper.ConsoleCommands.Add("bsv_horse_status", BsvI18n.Get(helper, "cmd.horse-status"), (_, _) =>
        {
            if (!Context.IsWorldReady) return;
            AuditTractors();
            foreach (var stable in Stables())
            {
                var h = stable.getStableHorse();
                Monitor.Log($"Stable {stable.HorseId}: owner={stable.owner.Value}; horseOwner={h?.ownerId.Value}; name={h?.Name}; active={Active.ContainsKey(stable.HorseId)}; unclaimed={stable.owner.Value == 0 && (h?.ownerId.Value ?? 0) == 0}", LogLevel.Info);
            }
        });
        helper.ConsoleCommands.Add("bsv_horse_name", BsvI18n.Get(helper, "cmd.horse-name"), (_, args) =>
        {
            if (!Context.IsPlayerFree) return;
            var h = PickOwnedHorse(false);
            if (h != null) { if (args.Length == 0) OpenNaming(h, false); else SendAction(h, "Name", string.Join(" ", args)); }
        });
        helper.ConsoleCommands.Add("bsv_horse_choose", BsvI18n.Get(helper, "cmd.horse-choose"), (_, _) =>
        { if (Context.IsPlayerFree) CallLocal(forceChoice: true); });
    }

    // Read-only support audit; never repair tractor/garage metadata automatically.
    private void AuditTractors()
    {
        var seen = new HashSet<Horse>();
        void Inspect(Horse horse)
        {
            if (!IsTractorHorse(horse) || !seen.Add(horse)) return;
            Monitor.Log($"[Tractor audit; read-only] horse={horse.HorseId}; owner={horse.ownerId.Value}; name={horse.Name}; BSV keys={string.Join(",", horse.modData.Keys.Where(k => k.StartsWith(Tag, StringComparison.Ordinal)))}", LogLevel.Info);
        }
        Utility.ForEachLocation(location =>
        {
            foreach (var horse in location.characters.OfType<Horse>()) Inspect(horse);
            foreach (var stable in location.buildings.OfType<Stable>().Where(IsTractorStable))
            {
                Monitor.Log($"[Tractor audit; read-only] garage={stable.HorseId}; type={stable.buildingType.Value}; owner={stable.owner.Value}; BSV keys={string.Join(",", stable.modData.Keys.Where(k => k.StartsWith(Tag, StringComparison.Ordinal)))}", LogLevel.Info);
                if (stable.getStableHorse() is { } horse) Inspect(horse);
            }
            return true;
        });
        foreach (var farmer in Game1.getAllFarmers()) if (farmer.mount is { } horse) Inspect(horse);
    }

    // Apply once to the native final speed; preserve diagonal movement and other buffs.
    private static void AfterMovementSpeed(Farmer __instance, ref float __result)
    {
        if (Instance is null || !IsRealManagedHorse(__instance.mount)) return;
        var effective=Configuration.SharedPreferences.Effective(Instance!.Config);
        if (__instance.isRidingHorse() && effective.EnableHorseSpeedBonus) __result *= (float)(1 + effective.HorseSpeedBonus);
    }

    private string T(string key, object? tokens = null) => BetterStardewValley.BsvI18n.Get(Helper, key, tokens).ToString();
    private void Message(string key, object? tokens = null) { if (key.Length > 0) Game1.showRedMessage(T(key, tokens)); }
    private void WarnOnce(string key, Exception ex)
    {
        if (Warnings.Add(key)) Monitor.Log("BSV horses [" + key + "]: " + ex.Message, LogLevel.Warn);
    }
    private void ResetLocal() => Local.Value = new LocalState();
    private void ClearHost() { StopAll(); Views.Clear(); Replies.Clear(); Incoming.Clear(); Calls.Clear(); Interactions.Clear(); }
    private void StopAll() { foreach (var s in Active.Values.ToArray()) Finish(s, ""); }

    private const string KeyNoticeKey = "Eddie.BetterStardewValley/HorseKeyConflictNotice";
    private static readonly HashSet<SButton> ForeignKeys = new();
    private static string KeyConflictMod = "";
    /// <summary>The configured key (H by default), or J while another mod also uses that key.</summary>
    internal static SButton CallKey => Instance is { } mod && ForeignKeys.Contains(mod.Config.CallHorseKey) && !ForeignKeys.Contains(SButton.J) ? SButton.J
        : Instance?.Config.CallHorseKey ?? SButton.H;
    /// <summary>UI Info Suite 2 opens its quest board with H by default; read its actual setting when possible.</summary>
    private void FindKeyConflicts()
    {
        ForeignKeys.Clear();
        try
        {
            const string id = "Annosz.UiInfoSuite2";
            var info = Helper.ModRegistry.Get(id);
            if (info == null) return;
            KeyConflictMod = info.Manifest.Name;
            string? dir = info.GetType().GetProperty("DirectoryPath")?.GetValue(info) as string;
            string? raw = dir != null && File.Exists(Path.Combine(dir, "config.json")) ? File.ReadAllText(Path.Combine(dir, "config.json")) : null;
            var keys = new List<string>();
            if (raw != null)
            {
                var json = Newtonsoft.Json.Linq.JObject.Parse(raw);
                foreach (string field in new[] { "OpenQuestBoardKeybind", "OpenCalendarKeybind" })
                    if (json[field]?.ToString() is { } list) keys.AddRange(list.Split(','));
            }
            else keys.AddRange(new[] { "H", "B" }); // its defaults
            foreach (string key in keys)
                if (!key.Contains('+') && Enum.TryParse<SButton>(key.Trim(), true, out var button)) ForeignKeys.Add(button);
            if (ForeignKeys.Contains(Config.CallHorseKey))
                Monitor.Log($"BSV horses: {KeyConflictMod} also uses {Config.CallHorseKey}; call your horse with J while it is installed.", LogLevel.Info);
        }
        catch (Exception ex) { Monitor.Log("BSV horses: could not read other mods' keys: " + ex.Message, LogLevel.Trace); }
    }
    private void OnButton(object? sender, ButtonPressedEventArgs e)
    {
        if (!Ready || !Context.IsPlayerFree) return;
        var key = CallKey;
        if (key == SButton.None || e.Button != key || Helper.Input.IsSuppressed(e.Button)) return;
        Helper.Input.Suppress(e.Button);
        CallLocal();
    }
    private void CallLocal(bool forceChoice = false)
    {
        if (forceChoice)
        {
            var selected = PickOwnedHorse(true, forceChoice: true);
            if (IsRealManagedHorse(selected)) SendAction(selected!, "Select");
            return;
        }
        var local = Local.Value;
        double now = Now;
        if (now < local.NextCall) return;
        local.NextCall = now + .3; // debounce only; the host decides call / turn around / send away
        if (!Allowed(Game1.currentLocation)) { PlayWhistle(false); Message("horse.call.unavailable_location"); return; }
        if (Game1.player.mount != null) { Message("horse.call.already_here"); return; }
        var horse = PickOwnedHorse(true, forceChoice);
        if (horse != null) SendAction(horse, "Call");
    }

    /// <summary>Call whistle (rising) or "go home" whistle (falling). Both are BSV's own synthesized sounds (MIT).</summary>
    private void PlayWhistle(bool dismiss)
    {
        try
        {
            ref SoundEffect? sound = ref dismiss ? ref Dismiss : ref Whistle;
            if (sound is null)
            {
                using var stream = File.OpenRead(Path.Combine(Helper.DirectoryPath, "assets", dismiss ? "horse-dismiss.wav" : "horse-whistle.wav"));
                sound = SoundEffect.FromStream(stream);
            }
            sound.Play(Math.Clamp(Game1.options.soundVolumeLevel * .65f, 0, 1), 0, 0);
        }
        catch (Exception ex) { WarnOnce(dismiss ? "dismiss-whistle" : "whistle", ex); }
    }

    private static bool Allowed(GameLocation? location)
    {
        if (location?.Map?.Layers.Count is not > 0 || location.Map.GetLayer("Back") is null || location.Map.GetLayer("Buildings") is null) return false;
        var types = new List<string>();
        for (Type? t = location.GetType(); t != null; t = t.BaseType) types.Add(t.Name);
        bool denied = location.Map.Properties.TryGetValue("BSVNoHorseCall", out var value) && value.ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
        return !denied && HorseRules.Allowed(location.IsOutdoors, location.IsTemporary, location.currentEvent != null,
            location.Name, types, Instance?.Config.HorseCallBlacklist ?? Array.Empty<string>());
    }
}

using System.Diagnostics;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace BetterStardewValley.MultiplayerRescue;

internal sealed partial class RescueService
{
    internal const string ModId = "Eddie.BetterStardewValley";
    private const string Channel = "Rescue.v1", LocalDebtKey = ModId + "/RescuePending.v1",
        ReceiptKey = ModId + "/RescueReceipt.v1", LedgerKey = ModId + "/RescueLedger.v1";
    internal static RescueService? Instance;
    private readonly IModHelper Helper;
    private readonly IMonitor Monitor;
    private readonly string Version;
    private readonly Stopwatch Clock = Stopwatch.StartNew();
    private RescueModel Model = new();
    private RescueClock RemoteClock = new();
    private readonly Dictionary<long, PeerState> Peers = new();
    private readonly Dictionary<long, RescueRecord> View = new();
    private readonly HashSet<long> BadPackets = new();
    private Dictionary<long, RescueRecord> Ledger = new();
    private string Epoch = "", Pending = "";
    private long Sequence, SnapshotSequence, ReceivedSequence, DamageSerial, BroadcastRevision = -1;
    private double PendingAt, LastSnapshot, LastHostTime, NextPulse, NextBroadcast, PendingRescueUntil;
    private bool Healthy = true, FaultLogged, Released, NativeStarted;
    private string AppliedEvent = "";
    private const string ReceiptOutcomeKey = ModId + "/RescueReceiptOutcome.v2";
    private string ActiveEvent="", TerminatedEvent="", AppliedAttempt="";
    private double LocalDeadline=double.PositiveInfinity, NextSafetySend;
    private bool ReceiptConfirmed;
    private double StandAt = -100, ReleasedAt, BeginSentAt = -100;
    private readonly List<SButton> HeldButtons = new();
    private int LastDay = -1;
    private string LastOutcome = "";
    private sealed class PeerState
    { public long Sequence; public bool Busy; public bool Alive; public long DamageSerial; public double SeenAt; }
    private double Now => Clock.Elapsed.TotalSeconds;
    private long LocalId => Game1.player.UniqueMultiplayerID;
    private static bool World => Context.IsWorldReady && Game1.player?.currentLocation != null;
    private bool Multiplayer => World && Context.IsMultiplayer && !Context.IsSplitScreen && Healthy;
    private bool Host => Context.IsMainPlayer;
    private double HostNow => Host ? Now : RemoteClock.Estimate(Now, LastHostTime);
    internal static bool Controls(Farmer? farmer) => farmer != null && Instance?.ControlsLocal(farmer) == true;
    internal static bool SuspendsAfk(Farmer? farmer) => Controls(farmer) || farmer != null && Instance is { } s
        && World && farmer == Game1.player && (s.Released && farmer.modData.ContainsKey(LocalDebtKey) || s.Now-s.StandAt < .30 || s.Now<s.PendingRescueUntil);
    private bool ControlsLocal(Farmer farmer)
    {
        if (!World || farmer != Game1.player || !farmer.IsLocalPlayer || !Healthy || Context.IsSplitScreen
            || Game1.timeOfDay >= 2600 || Game1.newDay || Game1.eventUp || Game1.currentMinigame != null) return false;
        if (!Host && Now - LastSnapshot > 6) return false;
        return !Released && Pending.Length > 0 && Now - PendingAt < 5 && Now<LocalDeadline || View.Values.Any(r => r.Outcome == RescueOutcome.Downed
            && (CanGiveUp && Now<LocalDeadline && r.EventId==ActiveEvent && r.Target == farmer.UniqueMultiplayerID || r.Rescuer == farmer.UniqueMultiplayerID && r.AttemptId != CancelledAttempt));
    }
    private bool ProtectDeath(Farmer who) => ControlsLocal(who) && !Released && Now<LocalDeadline
        && (Pending.Length > 0 || View.TryGetValue(who.UniqueMultiplayerID, out var r) && r.Outcome == RescueOutcome.Downed);
    private bool Busy => Game1.activeClickableMenu != null || Game1.overlayMenu != null || Game1.eventUp
        || Game1.player.currentLocation.currentEvent != null || Game1.currentMinigame != null
        || Game1.player.UsingTool || Game1.player.isEating || Game1.player.controller != null
        || Game1.player.isMoving() || Game1.freezeControls || Game1.player.passedOut || Game1.newDay || Game1.timeOfDay >= 2600;
    private string Tr(string key, object? tokens = null) => BsvI18n.Get(Helper, "rescue." + key, tokens);
    internal RescueService(IModHelper helper, IMonitor monitor, IManifest manifest)
    {
        Helper = helper; Monitor = monitor; Version = manifest.Version.ToString();
        var harmony = new Harmony(ModId + ".MultiplayerRescue");
        Instance = this;
        try { Install(harmony); }
        catch { harmony.UnpatchAll(harmony.Id); Instance = null; throw; }
        helper.Events.GameLoop.SaveLoaded += (_, _) => Safe(Load);
        helper.Events.GameLoop.DayStarted += (_, _) => Safe(NewDay);
        helper.Events.GameLoop.UpdateTicked += (_, _) => Safe(Tick);
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => Reset();
        helper.Events.Multiplayer.ModMessageReceived += Message;
        helper.Events.Multiplayer.PeerDisconnected += (_, e) => Safe(() =>
        {
            Peers.Remove(e.Peer.PlayerID);
            if (Host) { Model.Disconnect(e.Peer.PlayerID, Now); Publish(); }
            else if (e.Peer.PlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
            { ReleaseToNative("host-disconnected"); Epoch="";ReceivedSequence=0;RemoteClock=new(); }
        });
        helper.Events.Player.Warped += (_, e) => { if (e.IsLocalPlayer) Safe(() => Interrupt("warp")); };
        helper.Events.Input.ButtonPressed += Input;
        helper.Events.Input.ButtonsChanged += (_, e) =>
        {
            HeldButtons.Clear(); HeldButtons.AddRange(e.Held);
            if (World && ProtectDeath(Game1.player)) foreach (var button in e.Held) if (!ChatKey(button)) helper.Input.Suppress(button);
        };
        helper.Events.Display.RenderedWorld += (_, e) => Safe(() => DrawWorld(e.SpriteBatch));
        helper.Events.Display.RenderedHud += (_, e) => Safe(() => DrawHud(e.SpriteBatch));
        helper.ConsoleCommands.Add("bsv_rescue_status", Tr("command"), (_, _) =>
        {
            monitor.Log($"BSV rescue: initialized={Healthy}; multiplayer={Context.IsMultiplayer}; splitScreenExcluded={Context.IsSplitScreen}; host={Context.IsMainPlayer}; protocol=2; epoch={Epoch}; pending={Pending}; nativeReleased={Released}; peers={Peers.Count}.", LogLevel.Info);
            foreach (var r in View.Values) monitor.Log($"Rescue target={r.Target}; event={r.EventId}; outcome={r.Outcome}; rescuer={r.Rescuer}; attempt={r.AttemptId}; reason={r.Reason}; remaining={Math.Max(0,r.Deadline-HostNow):F2}s.", LogLevel.Info);
        });
        monitor.Log("BSV Multiplayer Rescue initialized (split screen excluded; real multiplayer validation pending).", LogLevel.Debug);
        RegisterTrace();
    }
    private void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            Healthy = false;
            // Latch before any fallible cleanup. Recovery ticks remain enabled.
            Released=true;TerminatedEvent=ActiveEvent;Pending="";GiveUpBounds=Rectangle.Empty;
            try { ReleaseToNative("module-fault"); } catch { /* native guards are already open */ }
            if (!FaultLogged) { FaultLogged = true; Monitor.Log("BSV rescue disabled after an error; native damage/death and other BSV modules remain active.\n" + ex, LogLevel.Error); }
        }
    }
    private void Reset()
    {
        Healthy = true; FaultLogged = false; // a fault never outlives the session it happened in
        Model = new(); RemoteClock = new(); Peers.Clear(); View.Clear(); Ledger.Clear(); Epoch = Pending = AppliedEvent = "";
        Sequence = SnapshotSequence = ReceivedSequence = DamageSerial = 0;
        BroadcastRevision = -1; NextPulse = NextBroadcast = 0; Released = NativeStarted = false; GiveUpBounds = Rectangle.Empty;
        CancelledAttempt = LastOutcome = ""; LastDay = -1;
        BadPackets.Clear();
        PendingRescueUntil = 0;
        ActiveEvent=TerminatedEvent=AppliedAttempt="";LocalDeadline=double.PositiveInfinity;NextSafetySend=0;
        ReceiptConfirmed=false;
        TraceUntil = 0; TraceRows.Clear(); Bars.Clear(); BeginSentAt = -100;
    }
    private void Load()
    {
        Reset(); if (!World) return;
        LastDay = Game1.Date.TotalDays;
        if (Host)
        {
            Epoch = Guid.NewGuid().ToString("N");
            if (Game1.MasterPlayer.modData.TryGetValue(LedgerKey, out var json))
            {
                try { Ledger = JsonConvert.DeserializeObject<Dictionary<long, RescueRecord>>(json,
                    new JsonSerializerSettings { MaxDepth = 12, TypeNameHandling = TypeNameHandling.None }) ?? new(); }
                catch(Exception ex) { Healthy=false;Monitor.Log("BSV rescue ledger invalid; original record preserved, no new downed protection: "+ex,LogLevel.Error); }
            }
            // Stopwatch timestamps are never resumed across process/save epochs.
            foreach (var r in Ledger.Values)
                if (r.Outcome is RescueOutcome.Downed or RescueOutcome.ReviveAuthorized) { r.Outcome = RescueOutcome.VanillaDeath; r.Reason = "interrupted-session"; }
            if(Healthy)SaveLedger();
        }
        if (Game1.player.modData.TryGetValue(LocalDebtKey, out var pending))
        {
            ActiveEvent=pending;ReleaseToNative("load-recovery");
            // Old debt cannot prove an additional native penalty is owed. Never
            // kill a healthy loaded farmer merely because a receipt was lost.
            if(Game1.player.health>0)Acknowledge(pending);
        }
        else if(Game1.player.modData.TryGetValue(ReceiptKey,out var receipt)&&RescueModel.ValidId(receipt))
        {
            // A saved receipt may have preceded a lost network acknowledgement.
            // Retry it without replaying either health or native penalty effects.
            ActiveEvent=receipt;Released=true;TerminatedEvent=receipt;
        }
        if (Host && Ledger.Count > 0) Publish();
    }
    private void NewDay()
    {
        if (!World) return;
        int day = Game1.Date.TotalDays;
        if (day <= LastDay) return; // SaveLoaded also raises DayStarted: it is not an overnight settlement.
        LastDay = day;
        if (Host && Model.Records.Any(r => r.Outcome is RescueOutcome.Downed or RescueOutcome.ReviveAuthorized))
        { Model.Tick(Now, Actor, true); Publish(); }
        // A real new day, after native wake-up, completes a pending native fallback.
        if (Game1.player.modData.TryGetValue(LocalDebtKey, out var id) && Game1.player.health > 0 && Game1.timeOfDay < 2600)
        { Acknowledge(id); Pending = ""; Released = NativeStarted = false; }
        GiveUpBounds = Rectangle.Empty;
    }
    private static bool ChatKey(SButton button) => button.TryGetKeyboard(out var key) && Game1.options.doesInputListContain(Game1.options.chatButton, key);
    private RescueActor? CurrentTarget(long id)
    {
        var f = Game1.GetPlayer(id, onlyOnline: true);
        return f?.currentLocation == null ? null : new RescueActor { Id = id, Location = f.currentLocation.NameOrUniqueName, X = f.StandingPixel.X, Y = f.StandingPixel.Y };
    }
    private bool CompatiblePeer(long id) => Helper.Multiplayer.GetConnectedPlayer(id)?.GetMod(ModId)?.Version.ToString() == Version;
    private RescueActor? Actor(long id)
    {
        Farmer? f = Game1.GetPlayer(id, onlyOnline: true);
        if (f?.currentLocation == null) return null;
        if (id != LocalId && (!Peers.TryGetValue(id, out var p) || Now-p.SeenAt > 6)) return null; // same allowance as the farmhand's host-silence timeout
        bool busy = id == LocalId ? Busy : Peers[id].Busy;
        // Native damage belongs to its owning client. Position/location are read from the
        // host's replicated Farmer, never accepted from request payload coordinates.
        return new RescueActor { Id = id, Location = f.currentLocation.NameOrUniqueName, X = f.StandingPixel.X,
            Y = f.StandingPixel.Y, Available = !busy, Alive = id == LocalId ? f.health > 0 : Peers[id].Alive,
            DamageSerial = id == LocalId ? DamageSerial : Peers[id].DamageSerial };
    }
    private void Tick()
    {
        if (!World || IntegrationHost.Saving) return; // nothing is written while the game serializes the save
        RecoveryTick();
        if (!Healthy) return;
        FinishTrace();
        if (Context.IsSplitScreen) { if (ControlsLocal(Game1.player) || Pending.Length > 0) ReleaseToNative("split-screen-excluded"); return; }
        if (!Multiplayer)
        {
            if (Pending.Length > 0 && Now-PendingAt >= 5) ReleaseToNative("single-player-recovery");
            ObserveNativeRecovery(); return;
        }
        if (Host)
        {
            Model.Tick(Now, Actor, Game1.timeOfDay >= 2600 || Game1.newDay);
            if (Model.Revision != BroadcastRevision || Now >= NextBroadcast) Publish();
        }
        if (Now >= NextPulse)
        {
            NextPulse = Now + (Pending.Length > 0 || View.Values.Any(r => r.Outcome == RescueOutcome.Downed) ? .5 : 1);
            Send(new RescueCommand { Kind = "pulse", Busy = Busy, DamageSerial = DamageSerial });
            if (Pending.Length > 0 && Now-PendingAt < 5 && (!View.TryGetValue(LocalId,out var acknowledged)
                || acknowledged.EventId != Pending || acknowledged.Outcome != RescueOutcome.Downed))
                Send(new RescueCommand { Kind = "down", EventId = Pending });
        }
        if (Pending.Length > 0 && Now-PendingAt >= 5) ReleaseToNative("host-decision-timeout");
        if (!Host && View.Values.Any(r => r.Outcome == RescueOutcome.Downed) && Now-LastSnapshot > 6)
            ReleaseToNative("host-heartbeat-timeout");
        if (ControlsLocal(Game1.player) && Busy) Interrupt("busy");
        if (View.Values.Any(r => r.Outcome == RescueOutcome.Downed && r.Rescuer == LocalId && r.AttemptId != CancelledAttempt))
            AfkFishing.AfkFishingService.YieldForRescue();
        ObserveNativeRecovery();
    }
    private void ObserveNativeRecovery()
    {
        if (!Released || !Game1.player.modData.TryGetValue(LocalDebtKey, out var id)) return;
        if (Game1.killScreen || Game1.player.passedOut || Game1.player.FarmerSprite.isPassingOut()) NativeStarted = true;
        if (NativeStarted && !Game1.killScreen && !Game1.player.passedOut && !Game1.player.FarmerSprite.isPassingOut() && Game1.player.health > 0)
        { Acknowledge(id); Released = NativeStarted = false; }
        // Healed by something else after the handoff: the game will never pass out, so close the record now.
        else if (!NativeStarted && Game1.player.health > 0 && Now - ReleasedAt > 2 && !Game1.killScreen && !Game1.player.passedOut)
        { Acknowledge(id); Released = false; }
    }
    private void SaveLedger()
    { if (World && Host && !IntegrationHost.Saving) Game1.MasterPlayer.modData[LedgerKey] = JsonConvert.SerializeObject(Ledger); }
    private void Publish(long? only = null, double echo = 0)
    {
        if (!Host || !World || !Healthy || IntegrationHost.Saving) return;
        foreach (var r in Model.Records)
        {
            if(r.Acknowledged){Ledger.Remove(r.Target);continue;}
            // Keep terminal verdicts until the owning client confirms it applied one.
            if (!Ledger.TryGetValue(r.Target, out var old) || old.EventId != r.EventId || old.Outcome != r.Outcome)
            {
                var farmer = Game1.GetPlayer(r.Target, onlyOnline: true);
                bool received = farmer != null && farmer.modData.TryGetValue(ReceiptKey, out var receipt) && receipt == r.EventId;
                if (!received) Ledger[r.Target] = r.Copy();
            }
        }
        SaveLedger();
        var snapshot = new RescueSnapshot { Epoch = Epoch, Sequence = ++SnapshotSequence, HostNow = Now, Echo = echo,
            Records = Model.Records.Select(r => r.Copy()).ToList() };
        foreach (var r in Ledger.Values)
            if (!snapshot.Records.Any(x => x.EventId == r.EventId)) snapshot.Records.Add(r.Copy());
        Apply(snapshot);
        long[] recipients = only.HasValue ? new[] { only.Value } : Peers.Keys.ToArray();
        if (recipients.Length > 0) Helper.Multiplayer.SendMessage(snapshot, Channel + ".snapshot", new[] { ModId }, recipients);
        BroadcastRevision = Model.Revision; NextBroadcast = Now + 1;
    }
    private void Send(RescueCommand c)
    {
        if (!World) return;
        c.Epoch = Epoch; c.Sequence = ++Sequence; c.SentAt = Now; c.Alive = Game1.player.health > 0;
        if (Host) Receive(LocalId, c);
        else Helper.Multiplayer.SendMessage(c, Channel, new[] { ModId }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }
    private void Message(object? sender, ModMessageReceivedEventArgs e)
    {
        if (e.FromModID != ModId || !World || Context.IsSplitScreen || IntegrationHost.Saving) return; // resent by the next pulse
        try
        {
            if (Host && e.Type == Channel && CompatiblePeer(e.FromPlayerID)) Receive(e.FromPlayerID, e.ReadAs<RescueCommand>());
            else if (!Host && e.Type == Channel + ".snapshot" && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID
                && CompatiblePeer(e.FromPlayerID)) Apply(e.ReadAs<RescueSnapshot>());
        }
        catch (JsonException ex)
        {
            if (BadPackets.Add(e.FromPlayerID)) Monitor.Log($"BSV rescue ignored an invalid packet from {e.FromPlayerID}: {ex.Message}",LogLevel.Warn);
        }
        catch (Exception ex) { Safe(() => throw new InvalidOperationException("Rescue network handler failed.",ex)); }
    }
    private void Receive(long sender, RescueCommand c)
    {
        if (!Host || c == null || c.Protocol != 2 || c.Sequence <= 0 || c.Kind == null || c.Kind.Length > 20
            || c.EventId == null || c.AttemptId == null || c.Epoch == null || c.Epoch.Length > 64
            || c.EventId.Length > 64 || c.AttemptId.Length > 64 || !double.IsFinite(c.SentAt)) return;
        if (c.Epoch.Length > 0 && c.Epoch != Epoch) return;
        if (sender != LocalId)
        {
            if (!Peers.TryGetValue(sender, out var peer)) Peers[sender] = peer = new();
            if (c.Sequence <= peer.Sequence) return;
            // A reconnect starts a fresh transport sequence only after PeerDisconnected removed it.
            peer.Sequence = c.Sequence;
            if (c.Kind == "pulse") { peer.Busy = c.Busy; peer.Alive = c.Alive; peer.DamageSerial = Math.Max(peer.DamageSerial, c.DamageSerial); peer.SeenAt = Now; }
        }
        if (c.Kind == "pulse")
        {
            if (sender != LocalId) Publish(sender, c.SentAt);
            return;
        }
        if (c.Epoch != Epoch) return;
        var actor = Actor(sender);
        if (c.Kind == "down" && Healthy && actor != null && Game1.timeOfDay < 2600 && !Game1.newDay)
        {
            // A fatal blow can happen during a sword swing or an already committed
            // catch. Do not require the victim's tool to be idle to accept the hit.
            var victim = Game1.GetPlayer(sender, onlyOnline: true);
            // Only the victim's own cutscene matters; the host watching an event must not disable rescue.
            actor.Available = victim?.currentLocation is { currentEvent: null };
            // A player who can be hit again is alive: any older record of theirs is finished.
            // Replace it instead of refusing, so one lost message can't disable rescue for them.
            if (!Model.IsDown(sender))
            {
                if (Ledger.Remove(sender)) SaveLedger();
                Model.Down(actor, c.EventId, Now);
            }
        }
        else if (c.Kind == "begin" && Healthy && actor != null) Model.Begin(actor, c.Target, c.EventId, c.AttemptId, Now, CurrentTarget(c.Target));
        else if (c.Kind == "cancel") Model.Cancel(sender, c.EventId, c.AttemptId, "owner-interrupted");
        else if (c.Kind == "give-up")
        {
            if(!Model.GiveUp(sender,c.EventId,Now) && Model.Find(sender)?.EventId!=c.EventId)
                Model.TerminateUnknown(sender,c.EventId,Now);
            if(Ledger.TryGetValue(sender,out var pendingRecord)&&pendingRecord.EventId==c.EventId
                && pendingRecord.Outcome is RescueOutcome.Downed or RescueOutcome.ReviveAuthorized)
            {pendingRecord.Outcome=RescueOutcome.VanillaDeath;pendingRecord.Reason="owner-terminated";}
        }
        else if(c.Kind=="revive-applied")
        {
            Model.ConfirmRevived(sender,c.EventId,c.AttemptId,Now);
            if(Model.Find(sender) is { Outcome:RescueOutcome.Revived } done && done.EventId==c.EventId)
            { Ledger.Remove(sender);SaveLedger(); }
            else if(Ledger.TryGetValue(sender,out var persisted)&&persisted.EventId==c.EventId
                &&persisted.AttemptId==c.AttemptId&&persisted.AuthorizedAt>0&&RescueModel.ValidId(c.AttemptId))
            {
                // Host reload discarded monotonic timers, not the authorization identity.
                // The owning target's matching application receipt only clears old debt.
                Ledger.Remove(sender);SaveLedger();
            }
        }
        else if (c.Kind == "ack")
        {
            // The native completion receipt can arrive before a lost/reordered
            // give-up request. Never require that request to release old debt.
            var record=Model.Find(sender);
            if(record?.EventId!=c.EventId)Ledger.TryGetValue(sender,out record);
            if(record==null || record.EventId!=c.EventId || record.Outcome==RescueOutcome.Revived)return;
            var owner = Game1.GetPlayer(sender, onlyOnline: true);
            if (owner == null) return;
            Model.GiveUp(sender,c.EventId,Now);
            Model.AcknowledgeNative(sender,c.EventId);
            Ledger.Remove(sender); owner.modData[ReceiptKey] = c.EventId; SaveLedger();
        }
        Publish();
    }
    private void Apply(RescueSnapshot snapshot)
    {
        if (snapshot == null || snapshot.Protocol != 2 || snapshot.Records == null || snapshot.Records.Count > 64
            || !RescueModel.ValidId(snapshot.Epoch) || !double.IsFinite(snapshot.HostNow) || !double.IsFinite(snapshot.Echo)) return;
        if (snapshot.Records.Any(r => r == null || !RescueModel.ValidId(r.EventId) || r.Location == null || r.Location.Length > 500
            || !double.IsFinite(r.DownAt) || !double.IsFinite(r.Deadline) || !double.IsFinite(r.StartedAt)
            || !double.IsFinite(r.X) || !double.IsFinite(r.Y) || !double.IsFinite(r.ResolvedAt) || !double.IsFinite(r.AuthorizedAt)
            || !Enum.IsDefined(typeof(RescueOutcome),r.Outcome)
            || (r.Outcome==RescueOutcome.ReviveAuthorized && (!RescueModel.ValidId(r.AttemptId)||r.AuthorizedAt<=0)))) return;
        if (Epoch.Length > 0 && snapshot.Epoch != Epoch) return;
        if (snapshot.Sequence <= ReceivedSequence) return;
        Epoch = snapshot.Epoch; ReceivedSequence = snapshot.Sequence; LastSnapshot = Now; LastHostTime = snapshot.HostNow;
        if (!Host && snapshot.Echo > 0) RemoteClock.Observe(snapshot.HostNow, snapshot.Echo, Now);
        View.Clear(); foreach (var r in snapshot.Records) View[r.Target] = r;
        if (!View.TryGetValue(LocalId, out var mine))
        { if(ActiveEvent.Length>0 && Game1.player.modData.TryGetValue(ReceiptKey,out var done)&&done==ActiveEvent)ReceiptConfirmed=true;return; }
        // Snapshots are not permission to resurrect an old or unrelated local event.
        if(mine.EventId!=ActiveEvent)return;
        if(Game1.player.modData.TryGetValue(ReceiptKey,out var receipt)&&receipt==mine.EventId)
        {ReceiptConfirmed=mine.Acknowledged;return;}
        RecoveryTick(false);
        if (mine.Outcome == RescueOutcome.Downed)
        {
            if (Released || TerminatedEvent==mine.EventId) return;
            Pending = ""; Game1.player.modData[LocalDebtKey] = mine.EventId;
            AfkFishing.AfkFishingService.YieldForRescue();
            return;
        }
        if (AppliedEvent == mine.EventId) return;
        Pending = "";
        if (mine.Outcome == RescueOutcome.Night)
        {
            // A delayed night snapshot may arrive after native dayupdate restored HP.
            // Never turn that morning into a second injury death.
            if (Game1.timeOfDay < 2600 && Game1.player.health > 0)
            { Acknowledge(mine.EventId); Released = NativeStarted = false; }
            else ReleaseToNative(mine.Reason,preserveHealth:true);
        }
        else if (mine.Outcome == RescueOutcome.ReviveAuthorized && Healthy && !Released && TerminatedEvent!=mine.EventId
            && Now<LocalDeadline+1 && HostNow<mine.Deadline+1 && !Game1.killScreen && !Game1.player.passedOut
            && !Game1.player.FarmerSprite.isPassingOut() && Game1.timeOfDay < 2600 && !Game1.newDay
            && !Game1.eventUp && Game1.currentMinigame==null && Game1.player.currentLocation.currentEvent==null)
        {
            AppliedEvent=mine.EventId;AppliedAttempt=mine.AttemptId;
            Game1.player.health = Math.Max(Game1.player.health, RescueModel.RestoredHealth(Game1.player.maxHealth));
            StandAt = Now; Game1.player.Halt(); Acknowledge(mine.EventId,revived:true);
            Game1.addHUDMessage(new HUDMessage(Tr("revived"), 2));
        }
        else ReleaseToNative(mine.Reason);
        string outcome = mine.EventId + ":" + mine.Outcome;
        if (LastOutcome != outcome) Monitor.Log($"BSV rescue resolved: event={mine.EventId}; result={mine.Outcome}; reason={mine.Reason}.", LogLevel.Debug);
        LastOutcome = outcome;
    }
    private void Acknowledge(string id,bool revived=false)
    {
        // Write receipt before network I/O. Lost acknowledgements retry without HP writes.
        Game1.player.modData[ReceiptOutcomeKey]=revived?"revived:"+AppliedAttempt:"native";
        Game1.player.modData[ReceiptKey] = id; Game1.player.modData.Remove(LocalDebtKey);
        NextSafetySend=0;
        Send(new RescueCommand { Kind = revived?"revive-applied":"ack", EventId = id,AttemptId=revived?AppliedAttempt:"" });
    }
    private void ReleaseToNative(string reason, bool preserveHealth = false)
    {
        bool first=!Released || TerminatedEvent!=ActiveEvent;
        TerminatedEvent=ActiveEvent;
        Pending = ""; Released = true; GiveUpBounds = Rectangle.Empty; if (first) ReleasedAt = Now;
        PendingRescueUntil=0;
        // Actual lethal health was never increased while downed. Opening DeathHealth
        // is sufficient: no repeated zeroing, forced warp, tired callback or penalty.
        // A healthy loaded/external-revived farmer is NEVER killed for stale debt.
        if(first)Monitor.Log("BSV rescue native handoff requested: event="+ActiveEvent+"; reason=" + reason, LogLevel.Debug);
    }
    private void Interrupt(string reason)
    {
        if (!World) return;
        var r = View.Values.FirstOrDefault(x => x.Outcome == RescueOutcome.Downed && x.Rescuer == LocalId);
        if (r != null) { CancelledAttempt = r.AttemptId; Send(new RescueCommand { Kind = "cancel", EventId = r.EventId, AttemptId = r.AttemptId }); }
        if ((reason == "warp" || Game1.eventUp) && ProtectDeath(Game1.player))
        {
            string id = Pending.Length > 0 ? Pending : View[LocalId].EventId;
            ReleaseToNative(reason);
            Send(new RescueCommand { Kind = "give-up", EventId = id });
        }
    }
    private void Damaged(Farmer who, int before)
    {
        if (!World || who != Game1.player || !who.IsLocalPlayer) return;
        DamageSerial++; Interrupt("damage");
        if (!Multiplayer || who.health > 0 || before <= 0 || Game1.eventUp || who.currentLocation.currentEvent != null
            || Game1.currentMinigame != null || Game1.timeOfDay >= 2600 || Game1.newDay || who.passedOut
            || Game1.killScreen || Pending.Length > 0 || ProtectDeath(who)) return;
        if (Host ? Peers.Count == 0 : Epoch.Length == 0 || Now-LastSnapshot > 3) return;
        if (who.CurrentTool is StardewValley.Tools.FishingRod activeRod && who.UsingTool
            && !AfkFishing.AfkFishingService.OwnsRescueCatch(activeRod))
        {
            // A manual, unclaimed native fish/treasure result has no BSV-owned escrow.
            // Do not reset it or silently automate the player's manual catch.
            if (activeRod.fishCaught || activeRod.hit || activeRod.isReeling || activeRod.pullingOutOfWater || activeRod.showingTreasure)
            { Monitor.Log("BSV rescue deferred to vanilla: a manual fishing result is still owned by the native catch UI.",LogLevel.Debug); return; }
            activeRod.doneFishing(who,false);
            who.completelyStopAnimatingOrDoingAction();
        }
        Released = NativeStarted = false; AppliedEvent = "";
        Pending = Guid.NewGuid().ToString("N"); PendingAt = Now;
        ActiveEvent=Pending;TerminatedEvent=AppliedAttempt="";ReceiptConfirmed=false;
        LocalDeadline=PendingAt+RescueModel.DownSeconds;NextSafetySend=0;
        who.modData[LocalDebtKey] = Pending;
        AfkFishing.AfkFishingService.YieldForRescue(); who.Halt();
        // A key already held when the hit lands never raises a new ButtonsChanged: suppress it now.
        foreach (var button in HeldButtons) if (!ChatKey(button)) Helper.Input.Suppress(button);
        // Native action stop resets a FishingRod's pending result, so don't call it
        // for a rod. Its already committed result remains in the native/AFK pipeline.
        // A sword/slingshot action can end normally without changing inventories.
        if (who.CurrentTool is not StardewValley.Tools.FishingRod) who.completelyStopAnimatingOrDoingAction();
        Send(new RescueCommand { Kind = "pulse", Busy = Busy, DamageSerial = DamageSerial });
        Send(new RescueCommand { Kind = "down", EventId = Pending });
        Monitor.Log($"BSV rescue lethal hit: player={who.UniqueMultiplayerID}; event={Pending}.", LogLevel.Debug);
    }
}

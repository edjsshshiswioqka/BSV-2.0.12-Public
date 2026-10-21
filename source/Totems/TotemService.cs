using BetterStardewValley.Transactions;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace BetterStardewValley.Totems;

internal sealed record WorshipRequest(string Id, long Actor, TotemKind Kind, int X, int Y, int Day);
internal sealed record WorshipReply(string Id, long Actor, string Key, int Days = 0);

// A — one main-thread host queue; a confirmed interaction never consumes an item.
internal sealed class TotemService
{
    private const string RequestType = "TotemWorship.v1", ReplyType = "TotemReply.v1";
    private static TotemService Instance = null!;
    private readonly IModHelper Helper;
    private readonly IMonitor Monitor;
    private readonly string ModId, Version;
    private readonly Dictionary<long, WorshipRequest> Queue = new();
    private readonly Dictionary<long, WorshipReply> Notices = new();
    private readonly Dictionary<long, WorshipRequest> Pending = new();
    private readonly Dictionary<long, WorshipReply> Completed = new();
    private readonly Dictionary<long, long> SentAt = new();
    private bool Suspended;
    internal TotemService(IModHelper helper, IMonitor monitor, IManifest manifest)
    {
        Instance = this; Helper = helper; Monitor = monitor; ModId = manifest.UniqueID; Version = manifest.Version.ToString();
        var h = new Harmony(ModId + ".Totems");
        try
        {
            _ = new TotemArt(helper, h);
            h.Patch(AccessTools.Method(typeof(SObject), nameof(SObject.checkForAction)), prefix: new HarmonyMethod(typeof(TotemService), nameof(Interact)));
        }
        catch { h.UnpatchAll(h.Id); throw; }
        helper.Events.Multiplayer.ModMessageReceived += Message;
        helper.Events.GameLoop.UpdateTicked += (_, e) => { if (e.IsMultipleOf(6)) Tick(); };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { Queue.Clear(); Notices.Clear(); Pending.Clear(); SentAt.Clear(); Completed.Clear(); Suspended = false; TotemStorage.Reset(); };
        helper.Events.GameLoop.SaveLoaded += (_, _) => TotemStorage.Reset();
        helper.Events.GameLoop.DayStarted += (_, _) => Cleanup();
        _ = new TotemLighting(helper, monitor);
#if DEBUG
        helper.ConsoleCommands.Add("bsv_totem_spawn", Tr("totem.command"), Spawn);
#endif
    }
    private string Tr(string key, object? tokens = null) => BsvI18n.Get(Helper, key, tokens);
    private static bool Interact(SObject __instance, Farmer who, bool justCheckingForActivity, ref bool __result)
    {
        if (!TotemRules.TryKind(__instance.QualifiedItemId, out var kind)) return true;
        __result = true;
        if (justCheckingForActivity || who.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID) return false;
        Instance.Confirm(__instance, who, kind); return false;
    }
    private void Confirm(SObject obj, Farmer actor, TotemKind kind)
    {
        if (!Context.IsWorldReady || IntegrationHost.Saving || Game1.activeClickableMenu != null || Game1.eventUp) return;
        if (!ReferenceEquals(actor.currentLocation, Game1.getFarm())) { Game1.showRedMessage(Tr("totem.farm-only")); return; }
        try
        {
            int left = TotemStorage.Read().Remaining(kind, TotemStorage.Season, Game1.Date.TotalDays);
            if (left > 0) { Game1.drawObjectDialogue(Tr(kind == TotemKind.Thunderstorm ? "totem.cooldown" : "totem.used", new { days = left })); return; }
            var request = new WorshipRequest(Guid.NewGuid().ToString("N"), actor.UniqueMultiplayerID, kind, (int)obj.TileLocation.X, (int)obj.TileLocation.Y, Game1.Date.TotalDays);
            Game1.currentLocation.createQuestionDialogue(Tr("totem.confirm", new { name = obj.DisplayName }),
                new[] { new Response("Yes", Tr("totem.yes")), new Response("No", Tr("totem.no")) },
                (farmer, answer) => { if (answer == "Yes" && farmer.UniqueMultiplayerID == request.Actor) Submit(request); });
        }
        catch (Exception ex) { Warn(ex); Game1.showRedMessage(Tr("totem.error")); }
    }
    private void Submit(WorshipRequest request)
    {
        if (Pending.ContainsKey(request.Actor)) return;
        if (!Context.IsMainPlayer && !Context.IsSplitScreen && Helper.Multiplayer.GetConnectedPlayer(Game1.MasterPlayer.UniqueMultiplayerID)?.GetMod(ModId)?.Version.ToString() != Version)
        { Game1.showRedMessage(Tr("totem.host-version")); return; }
        Pending[request.Actor] = request; Send(request);
    }
    private void Send(WorshipRequest request)
    {
        SentAt[request.Actor] = Environment.TickCount64;
        if (Context.IsMainPlayer || Context.IsSplitScreen) Queue[request.Actor] = request;
        else Helper.Multiplayer.SendMessage(request, RequestType, new[] { ModId }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }
    private void Message(object? sender, ModMessageReceivedEventArgs e)
    {
        if (!Context.IsWorldReady || e.FromModID != ModId) return;
        try
        {
            if (Context.IsMainPlayer && e.Type == RequestType)
            {
                var request = e.ReadAs<WorshipRequest>();
                if (request.Actor != e.FromPlayerID || !Guid.TryParseExact(request.Id, "N", out _) || !Enum.IsDefined(request.Kind)) return;
                Queue[request.Actor] = request;
            }
            else if (e.Type == ReplyType && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
            {
                var reply = e.ReadAs<WorshipReply>();
                if (Pending.TryGetValue(reply.Actor, out var pending) && pending.Id == reply.Id) Notices[reply.Actor] = reply;
            }
        }
        catch (Exception ex) { Warn(ex); }
    }
    private void Tick()
    {
        if (!Context.IsWorldReady || IntegrationHost.Saving) return;
        long actor = Game1.player.UniqueMultiplayerID;
        if (Notices.Remove(actor, out var notice))
        {
            Pending.Remove(actor); SentAt.Remove(actor);
            Game1.addHUDMessage(new HUDMessage(Tr(notice.Key, new { days = notice.Days }), notice.Key == "totem.success" ? 2 : 3));
        }
        if (Pending.TryGetValue(actor, out var pending) && Environment.TickCount64 - SentAt[actor] >= 2000)
        {
            if (pending.Day != Game1.Date.TotalDays)
            { Pending.Remove(actor); SentAt.Remove(actor); Game1.showRedMessage(Tr("totem.unavailable")); }
            else Send(pending);
        }
        if (!Context.IsMainPlayer) return;
        foreach (var pair in Queue.ToArray())
        {
            Queue.Remove(pair.Key);
            var reply = Execute(pair.Value);
            if (reply.Key == "totem.success") Completed[pair.Key] = reply;
            if (Pending.ContainsKey(pair.Key) || Game1.player.UniqueMultiplayerID == pair.Key) Notices[pair.Key] = reply;
            else Helper.Multiplayer.SendMessage(reply, ReplyType, new[] { ModId }, new[] { pair.Key });
        }
    }
    private WorshipReply Execute(WorshipRequest request)
    {
        WorshipReply Reply(string key, int days = 0) => new(request.Id, request.Actor, key, days);
        try
        {
            if (!IntegrationHost.CanMutate || Suspended) return Reply("totem.unavailable");
            if (Completed.TryGetValue(request.Actor, out var receipt) && receipt.Id == request.Id) return receipt;
            var farm = Game1.getFarm();
            var actor = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == request.Actor);
            var tile = new Vector2(request.X, request.Y);
            bool Valid() => IntegrationHost.CanMutate && !Suspended && request.Day == Game1.Date.TotalDays
                && !Game1.eventUp && actor != null && ReferenceEquals(actor.currentLocation, farm)
                && Math.Abs(actor.Tile.X - tile.X) <= 1 && Math.Abs(actor.Tile.Y - tile.Y) <= 1
                && farm.objects.TryGetValue(tile, out var obj) && obj.QualifiedItemId == "(BC)" + TotemRules.Id(request.Kind);
            if (!Valid()) return Reply("totem.unavailable");
            var state = TotemStorage.Read();
            int season = TotemStorage.Season, day = Game1.Date.TotalDays, left = state.Remaining(request.Kind, season, day);
            if (left > 0) return Reply(request.Kind == TotemKind.Thunderstorm ? "totem.cooldown" : "totem.used", left);
            var machines = request.Kind == TotemKind.Processing ? TotemEffects.Machines(farm) : Array.Empty<Chest>();
            if (machines.Length > 128) return Reply("totem.busy");
            bool completed = false;
            WithLocks(farm, machines, () =>
            {
                if (!Valid()) throw new TotemRefusal("totem.unavailable");
                var plan = TotemEffects.Prepare(request.Kind, farm, machines);
                farm.modData.TryGetValue(TotemRules.SharedKey, out string before);
                string after = JsonConvert.SerializeObject(state.Used(request.Kind, season, day));
                plan.Steps.Add(new(() => farm.modData[TotemRules.SharedKey] = after,
                    () => { if (before == null) farm.modData.Remove(TotemRules.SharedKey); else farm.modData[TotemRules.SharedKey] = before; }));
                AtomicChange.Execute(() => Valid() && plan.Valid() && season == TotemStorage.Season, plan.Steps);
                completed = true;
            });
            return Reply(completed ? "totem.success" : "totem.busy");
        }
        catch (TotemRefusal refusal) { return Reply(refusal.Key); }
        catch (AggregateException ex) { Suspended = true; Warn(ex); return Reply("totem.error"); }
        catch (Exception ex) { Warn(ex); return Reply("totem.error"); }
    }
    private static void WithLocks(GameLocation farm, IReadOnlyList<Chest> machines, Action run)
    {
        // Keep work INSIDE all callbacks: the game's off-map mutex releases after a callback returns.
        void Acquire(int index)
        {
            if (index == machines.Count) { run(); return; }
            var mutex = machines[index].GetMutex(); mutex.Update(farm);
            if (mutex.IsLocked()) return;
            bool active = true; Exception? error = null;
            mutex.RequestLock(() =>
            {
                try { if (active && IntegrationHost.CanMutate && mutex.IsLockHeld()) Acquire(index + 1); }
                catch (Exception ex) { error = ex; }
                finally { if (mutex.IsLockHeld()) mutex.ReleaseLock(); }
            });
            try { mutex.Update(farm); } finally { active = false; }
            if (error != null) throw error;
        }
        Acquire(0);
    }
    private void Cleanup()
    {
        if (!IntegrationHost.CanMutate) return;
        TotemStorage.Reset();
        // Removed crops take their modData with them. No tile-indexed or global ghost identities exist.
        // Dead/replaced planted crops may still exist until cleared; discard only their invalid marker.
        var farm = Game1.getFarm();
        var soils = farm.terrainFeatures.Values.OfType<StardewValley.TerrainFeatures.HoeDirt>()
            .Concat(farm.objects.Values.OfType<IndoorPot>().Select(p => p.hoeDirt.Value));
        foreach (var soil in soils)
            if (soil.crop is { } crop && crop.modData.ContainsKey(TotemRules.BlessingKey) && !TotemStorage.Blessed(crop, farm)) crop.modData.Remove(TotemRules.BlessingKey);
    }
    private void Spawn(string command, string[] args)
    {
        if (!Context.IsWorldReady || args.Length is < 1 or > 2 || !Enum.TryParse<TotemKind>(args[0], true, out var kind)
            || !Enum.IsDefined(kind) || (args.Length == 2 && (!int.TryParse(args[1], out _) || int.Parse(args[1]) is < 1 or > 99)))
        { Monitor.Log(Tr("totem.command"), LogLevel.Info); return; }
        Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(BC)" + TotemRules.Id(kind), args.Length == 2 ? int.Parse(args[1]) : 1));
    }
    private void Warn(Exception ex) => Monitor.Log(Tr("totem.error") + "\n" + ex, LogLevel.Error);
}

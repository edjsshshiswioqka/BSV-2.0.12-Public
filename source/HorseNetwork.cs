using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;

namespace BetterStardewValley;

internal sealed partial class HorseFeature
{
    private HorseViewport CurrentView() => new()
    {
        Sequence = System.Diagnostics.Stopwatch.GetTimestamp(),
        Actor = Game1.player.UniqueMultiplayerID, Location = Game1.currentLocation.NameOrUniqueName,
        X = Game1.viewport.X, Y = Game1.viewport.Y, Width = Game1.viewport.Width, Height = Game1.viewport.Height
    };
    private void SendAction(Horse horse, string action, string name = "", string intent = "")
    {
        if (!Context.IsWorldReady || !IsRealManagedHorse(horse)) return;
        if (!Context.IsMainPlayer && !Context.IsSplitScreen
            && Helper.Multiplayer.GetConnectedPlayer(Game1.MasterPlayer.UniqueMultiplayerID)?.GetMod(ModId)?.Version.ToString()
                != Helper.ModRegistry.Get(ModId)?.Manifest.Version.ToString())
        { Message("horse.call.host_required"); return; }
        var r = new HorseRequest { Id = Guid.NewGuid().ToString("N"), Actor = Game1.player.UniqueMultiplayerID,
            Horse = horse.HorseId.ToString("N"), Location = Game1.currentLocation.NameOrUniqueName, Action = action,
            Name = name, Intent = intent, View = CurrentView() };
        Local.Value.Pending = r.Id; Local.Value.PendingSince = Now; Local.Value.Busy = true;
        if (Context.IsMainPlayer || Context.IsSplitScreen) Incoming[r.Actor] = r;
        else Helper.Multiplayer.SendMessage(r, RequestType, new[] { ModId }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }
    private void OnMessage(object? sender, ModMessageReceivedEventArgs e)
    {
        if (!Context.IsWorldReady || e.FromModID != ModId || !Ready) return;
        try
        {
            if (e.Type == ReplyType && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
            {
                var reply = e.ReadAs<HorseReceipt>();
                if (reply.Actor == Game1.player.UniqueMultiplayerID) Replies[reply.Actor] = reply;
            }
            else if (Context.IsMainPlayer && e.Type == RequestType)
            {
                var request = e.ReadAs<HorseRequest>();
                if (HorseRules.ValidEnvelope(e.FromPlayerID, request.Actor, request.Id, request.Horse, request.Location))
                    Incoming[request.Actor] = request; // bounded to one request per actual sender per update
            }
            else if (Context.IsMainPlayer && e.Type == ViewType) RememberView(e.FromPlayerID, e.ReadAs<HorseViewport>());
        }
        catch (Exception ex) { WarnOnce("message", ex); }
    }
    private void RememberView(long sender, HorseViewport? view)
    {
        if (view == null || sender != view.Actor) return;
        if (Views.TryGetValue(sender, out var prior) && view.Sequence <= prior.View.Sequence) return;
        var who = Game1.GetPlayer(sender, onlyOnline: true);
        if (who?.currentLocation?.NameOrUniqueName != view.Location
            || !HorseRules.ValidViewport(Bounds(view), (int)who.Position.X, (int)who.Position.Y)) return;
        Views[sender] = (view, Now);
    }
    private static HorseRules.Bounds Bounds(HorseViewport v) => new(v.X, v.Y, v.Width, v.Height);

    private void Reply(HorseRequest request, string key = "", bool running = false, string sound = "", string horseName = "")
    {
        var receipt = new HorseReceipt { Id = request.Id, Actor = request.Actor, Message = key, Running = running, Sound = sound, HorseName = horseName };
        Replies[request.Actor] = receipt;
        // The shared local inbox also handles split-screen players without changing Game1.player.
        if (Game1.GetPlayer(request.Actor, onlyOnline: true) is { IsLocalPlayer: false })
            Helper.Multiplayer.SendMessage(receipt, ReplyType, new[] { ModId }, new[] { request.Actor });
    }
    private void ApplyReceipt()
    {
        var local = Local.Value;
        if (!Replies.TryGetValue(Game1.player.UniqueMultiplayerID, out var r) || r.Id != local.Pending) return;
        string signature = r.Id + ":" + r.Running + ":" + r.Message;
        if (local.LastReceipt == signature) return;
        local.LastReceipt = signature; local.Busy = r.Running;
        if (r.Running) local.PendingSince = Now;
        if (r.Sound.Length > 0)
        {
            PlayWhistle(r.Sound == "dismiss");
            // Bottom-left corner note: "Your horse / <name> is coming" or "... is leaving".
            string name = r.HorseName.Trim();
            string key = (r.Sound == "dismiss" ? "horse.hud.leaving" : "horse.hud.coming") + (name.Length > 0 ? "_named" : "");
            Game1.addHUDMessage(HUDMessage.ForCornerTextbox(T(key, new { horseName = name })));
        }
        Message(r.Message);
    }
    private void OnTick(object? sender, UpdateTickedEventArgs e)
    {
        if (!Ready || !Context.IsWorldReady) return;
        double now = Now;
        var local = Local.Value;
        TrackCallCounter(local);
        if (now >= local.NextView)
        {
            local.NextView = now + .25;
            var view = CurrentView();
            if (Context.IsMainPlayer || Context.IsSplitScreen) RememberView(view.Actor, view);
            else Helper.Multiplayer.SendMessage(view, ViewType, new[] { ModId }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }
        if (Context.IsMainPlayer)
        {
            foreach (var request in Incoming.Values.ToArray())
            {
                Incoming.Remove(request.Actor);
                try { Process(request); }
                catch (Exception ex) { WarnOnce("request", ex); Reply(request, "horse.call.no_path"); }
            }
            foreach (var state in Active.Values.ToArray())
            {
                try { Advance(state, now); }
                catch (Exception ex) { WarnOnce("movement", ex); Finish(state, "horse.call.no_path"); }
            }
        }
        ApplyReceipt();
        OfferCoat();
        if (local.Busy && now - local.PendingSince > 50)
        { local.Busy = false; local.Pending = ""; Message("horse.call.timeout"); }
    }

    private void Process(HorseRequest r)
    {
        if (!Context.IsMainPlayer || !HorseRules.ValidEnvelope(r.Actor, r.Actor, r.Id, r.Horse, r.Location)) return;
        var who = Game1.GetPlayer(r.Actor, onlyOnline: true);
        if (who?.currentLocation == null || who.currentLocation.NameOrUniqueName != r.Location) { Reply(r, "horse.call.unavailable_location"); return; }
        if (r.Action != "Call" && r.Action != "Name" && r.Action != "Claim" && r.Action != "Select" && r.Action != "Builder" && r.Action != "Skin") return;
        if (Replies.TryGetValue(r.Actor, out var previous) && previous.Id == r.Id)
        { Reply(r, previous.Message, previous.Running, previous.Sound, previous.HorseName); return; }
        var id = Guid.Parse(r.Horse);
        // Turning a running horse around (coming <-> leaving) is immediate: no cooldown.
        bool turnaround = r.Action == "Call" && Active.TryGetValue(id, out var running) && running.Actor == r.Actor;
        if (!turnaround && !(r.Action == "Call" ? Calls : Interactions).Accept(r.Actor, r.Id, Now, r.Action == "Call" ? Config.CallHorseCooldownSeconds : .5))
        { Reply(r, "horse.call.cooldown"); return; }
        if (r.Action is "Builder" or "Skin") { HandleStable(r, id); return; }
        var stable = StableFor(id);
        var horse = stable?.getStableHorse();
        if (stable == null || stable.daysOfConstructionLeft.Value > 0 || !IsRealManagedHorse(horse) || horse!.HorseId != id)
        { Reply(r, "horse.call.no_horse"); return; }
        if (r.Action == "Claim")
        {
            if (!HorseRules.CanClaim(stable.owner.Value, horse.ownerId.Value) || !InReach(who, horse)
                || horse.rider != null || horse.mounting.Value || horse.dismounting.Value || horse.mutex.IsLocked())
            { Reply(r, "horse.claim.unavailable"); return; }
            string claimName = HorseRules.CleanName(r.Name);
            if (claimName.Length == 0) { Reply(r, "horse.naming.invalid"); return; }
            // One serialized host transaction. A second claimant sees both native owners already set.
            stable.owner.Value = who.UniqueMultiplayerID;
            horse.ownerId.Value = who.UniqueMultiplayerID;
            SaveName(stable, horse, who, claimName);
            Reply(r, "horse.claim.success");
            return;
        }
        if (!HorseRules.Owns(r.Actor, stable.owner.Value, horse.ownerId.Value))
        { Reply(r, "horse.call.no_horse"); return; }
        // Selection is a host-authorized preference, independent of a successful route.
        if (r.Action == "Call" || r.Action == "Select") who.modData[SelectedKey] = id.ToString("N");
        if (r.Action == "Select") { Reply(r); return; }
        if (r.Action == "Call" && Active.TryGetValue(id, out var moving))
        {
            if (horse.rider != null || who.mount != null || horse.mounting.Value || horse.dismounting.Value)
            { Reply(r, "horse.call.already_here"); return; }
            // Press again while it runs: it turns around (coming -> leaving, leaving -> coming) from where it is.
            moving.Departing = r.Intent switch { "Leave" => true, "Come" => false, _ => !moving.Departing };
            moving.Started = false; moving.Route.Clear(); moving.BlockedSince = 0;
            moving.Start = moving.LastTick = Now - 1; moving.DepartStart = Now;
            Reply(r, "", sound: moving.Departing ? "dismiss" : "call", horseName: moving.Horse.Name ?? ""); return;
        }
        if (Active.ContainsKey(id) || Active.Values.Any(s => s.Actor == r.Actor)) { Reply(r, "horse.call.busy"); return; }
        if (r.Action == "Name")
        {
            string name = HorseRules.CleanName(r.Name);
            if (!InReach(who, horse) || horse.rider != null || name.Length == 0) { Reply(r, "horse.naming.invalid"); return; }
            SaveName(stable, horse, who, name); Reply(r, "horse.naming.saved"); return;
        }
        if (!Allowed(who.currentLocation)) { Reply(r, "horse.call.unavailable_location"); return; }
        if (who.mount != null || horse.rider != null || horse.mounting.Value || horse.dismounting.Value || horse.mutex.IsLocked())
        { Reply(r, "horse.call.already_here"); return; }
        if (horse.currentLocation?.currentEvent != null || horse.controller != null || Frozen(horse))
        { Reply(r, "horse.call.busy"); return; }
        RememberView(r.Actor, r.View);
        if (!Views.TryGetValue(r.Actor, out var view) || nowStale(view.Time) || view.View.Location != r.Location)
        { Reply(r, "horse.call.no_path"); return; }
        // Horse already with the player (on screen in the same place): send it away. Anywhere else, even on
        // another map, it comes: it appears just outside the player's view and runs to them.
        // The caller's press counter decides; an older client without one falls back to where the horse is.
        bool present = r.Intent switch
        {
            "Leave" => true,
            "Come" => false,
            _ => horse.currentLocation == who.currentLocation && VisualBounds(horse.Position).Intersects(Bounds(view.View).Inflate(64)),
        };
        Active[id] = new Summon(r, horse, who.currentLocation, stable, Now) { Departing = present, DepartStart = Now };
        Reply(r, "", sound: present ? "dismiss" : "call", horseName: horse.Name ?? "");
        static bool nowStale(double time) => Now - time > 1;
    }
    private static void TrackCallCounter(LocalState local)
    {
        var who = Game1.player;
        bool mounted = who.mount != null;
        if (mounted) local.LastMount = who.mount!.HorseId;
        // Dismounting: the horse is right here, so the next press sends it away.
        if (local.WasMounted && !mounted) { local.NextLeave = true; local.AfterDismount = true; }
        local.WasMounted = mounted;
        // A new map (indoors included) starts the counter over: the next press calls.
        string here = Game1.currentLocation?.NameOrUniqueName ?? "";
        if (here != local.CounterLocation) { local.CounterLocation = here; local.NextLeave = false; local.AfterDismount = false; return; }
        // Walked away from the horse after dismounting: the next press calls it again.
        if (local.AfterDismount && local.NextLeave)
        {
            var horse = Game1.currentLocation?.characters.OfType<Horse>().FirstOrDefault(h => h.HorseId == local.LastMount);
            if (horse == null || Vector2.Distance(horse.Tile, who.Tile) > CallCounterReach) { local.NextLeave = false; local.AfterDismount = false; }
        }
    }
    private static bool InReach(Farmer who, Horse horse) => who.currentLocation == horse.currentLocation
        && Vector2.Distance(who.Position, horse.Position) <= 4 * 64;
    private static void SaveName(StardewValley.Buildings.Stable stable, Horse horse, Farmer who, string name)
    {
        if (IsTractorStable(stable) || !IsRealManagedHorse(horse)) return;
        name = NativeName(name);
        stable.modData[NameKey] = name;
        horse.Name = name; horse.displayName = name.Trim();
        // Keep vanilla single-horse integrations functional without making this the per-horse store.
        if (string.IsNullOrWhiteSpace(who.horseName.Value) || OwnedHorses(who.UniqueMultiplayerID).Count == 1) who.horseName.Value = name;
        who.modData[SelectedKey] = horse.HorseId.ToString("N");
    }
}

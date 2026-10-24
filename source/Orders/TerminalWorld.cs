using BetterStardewValley.Transactions;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace BetterStardewValley.Orders;

internal sealed class TerminalWorld
{
    private static TerminalWorld Instance = null!;
    private readonly IModHelper Helper;
    private readonly IMonitor Monitor;
    private readonly HashSet<GameLocation> Dirty = new();
    private readonly HashSet<string> Errors = new();
    private readonly HashSet<string> MigrationNotices = new();
    internal readonly TerminalNetwork Network;
    internal readonly TerminalLighting Lights = new();
    internal TerminalWorld(IModHelper helper, IMonitor monitor, Harmony harmony)
    {
        Instance = this; Helper = helper; Monitor = monitor; Network = new(helper, this);
        void Patch(string target, string prefix, Type result, params Type[] args) => harmony.Patch(
            DrawPatchTargets.Require(typeof(SObject), target, result, args), prefix: new(typeof(TerminalWorld), prefix));
        Patch(nameof(SObject.canBePlacedHere), nameof(CanPlacePatch), typeof(bool), typeof(GameLocation), typeof(Vector2), typeof(CollisionMask), typeof(bool));
        Patch(nameof(SObject.placementAction), nameof(PlacePatch), typeof(bool), typeof(GameLocation), typeof(int), typeof(int), typeof(Farmer));
        Patch(nameof(SObject.performToolAction), nameof(ToolPatch), typeof(bool), typeof(Tool));
        Patch(nameof(SObject.onExplosion), nameof(ExplosionPatch), typeof(bool), typeof(Farmer));
        Patch(nameof(SObject.drawPlacementBounds), nameof(Preview), typeof(void), typeof(SpriteBatch), typeof(GameLocation));
        Patch(nameof(SObject.GetBoundingBoxAt), nameof(BoundsPatch), typeof(Rectangle), typeof(int), typeof(int));
        Patch(nameof(SObject.isPassable), nameof(PassablePatch), typeof(bool));
        helper.Events.Input.ButtonPressed += Button;
        helper.Events.GameLoop.UpdateTicked += (_, e) => { if (e.IsMultipleOf(12)) Tick(); };
        helper.Events.GameLoop.SaveLoaded += (_, _) => AllDirty();
        helper.Events.GameLoop.DayStarted += (_, _) => AllDirty();
        helper.Events.GameLoop.TimeChanged += (_, _) => SafeLightUpdate();
        helper.Events.World.ObjectListChanged += (_, e) => { Dirty.Add(e.Location); SafeLightUpdate(); };
        helper.Events.World.FurnitureListChanged += (_, e) => Dirty.Add(e.Location);
        helper.Events.World.TerrainFeatureListChanged += (_, e) => Dirty.Add(e.Location);
        helper.Events.World.BuildingListChanged += (_, e) => Dirty.Add(e.Location);
        helper.Events.Player.Warped += (_, e) => { if (e.IsLocalPlayer) { Lights.Clear(); SafeLightUpdate(); } };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { Lights.Clear(); Dirty.Clear(); Errors.Clear(); MigrationNotices.Clear(); };
    }
    internal void Notice(string key) { if (!string.IsNullOrEmpty(key)) Game1.addHUDMessage(new HUDMessage(BsvI18n.Get(Helper, key), 3)); }
    internal void Error(Exception ex)
    {
        string text = ex.ToString();
        if (Errors.Add(text)) Monitor.Log("BSV Order Terminal: action retained safely. " + text, LogLevel.Error);
    }
    private void AllDirty()
    {
        if (!Context.IsWorldReady) return;
        Utility.ForEachLocation(l => { Dirty.Add(l); return true; }, includeInteriors: true, includeGenerated: true);
        Tick();
    }
    private void SafeLightUpdate() { try { Lights.Update(); } catch (Exception ex) { Error(ex); } }
    private void Tick()
    {
        if (!Context.IsWorldReady || IntegrationHost.Saving) return;
        if (Context.IsMainPlayer)
            foreach (var l in Dirty.ToArray())
            {
                Dirty.Remove(l);
                try { Repair(l); } catch (Exception ex) { Error(ex); }
            }
        else Dirty.Clear(); // only the host repairs; a farmhand must not keep every visited level alive
        Network.Tick(); SafeLightUpdate();
    }
    internal string? Place(GameLocation l, Vector2 tile, Farmer who, string id, SObject item, Action commit)
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady || IntegrationHost.Saving || who.currentLocation != l) return "orders.return-terminal";
        if (!TerminalEntity.Allowed(l, who)) return "orders.terminal-map";
        if (!TerminalEntity.CanPlace(l, tile, who)) return "orders.terminal-blocked";
        TerminalEntity.Stamp(item, l, tile, tile, id, false); item.owner.Value = who.UniqueMultiplayerID;
        var entries = TerminalEntity.Tiles(tile).Select(t => (Tile: t, Item: t == tile ? item : TerminalEntity.NewPart(l, t, item))).ToArray();
        // Native placement clears grass under a new object; do the same so no blades poke through the desk.
        var grass = TerminalEntity.Tiles(tile).Where(t => l.terrainFeatures.TryGetValue(t, out var f) && f is StardewValley.TerrainFeatures.Grass)
            .Select(t => (Tile: t, Feature: l.terrainFeatures[t])).ToArray();
        var steps = grass.Select(g => new AtomicStep(() => l.terrainFeatures.Remove(g.Tile), () => { if (!l.terrainFeatures.ContainsKey(g.Tile)) l.terrainFeatures.Add(g.Tile, g.Feature); })).ToList();
        steps.AddRange(entries.Select(p => new AtomicStep(() => l.objects.Add(p.Tile, p.Item), () =>
        {
            if (l.objects.TryGetValue(p.Tile, out var current) && ReferenceEquals(current, p.Item)) l.objects.Remove(p.Tile);
        })));
        Commit(who, steps, commit);
        Dirty.Add(l); SafeLightUpdate(); return null;
    }
    internal string? Pickup(GameLocation l, Vector2 tile, Farmer who, string id, Action<string> commit)
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady || IntegrationHost.Saving || who.currentLocation != l
            || who.CurrentTool is not (Axe or Pickaxe)) return "orders.return-terminal";
        if (!TerminalEntity.Resolve(l, tile, out var anchor) || TerminalEntity.Id(anchor) != id) return "orders.terminal-gone";
        if (!TerminalEntity.InReach(who, anchor.TileLocation, TerminalEntity.Pending(anchor))) return "orders.return-terminal";
        // Like a native chest: the order box must be emptied first (this also keeps confirmed goods in place).
        if (!OrderBox.IsEmpty(OrderBox.Key(id)) || OrderBox.Mutex(OrderBox.Key(id)).IsLocked()) return "orders.box-not-empty";
        // Pickup also works in a legacy/externally moved illegal map, so the player can recover it.
        string packed = TerminalItem.Pack(anchor);
        var entries = l.objects.Pairs.Where(p => OrderTerminal.IsTerminal(p.Value) && TerminalEntity.Id(p.Value) == id).ToArray();
        if (entries.Count(p => !TerminalEntity.IsPart(p.Value)) != 1) return "orders.terminal-blocked";
        var steps = entries.Select(p => new AtomicStep(() => l.objects.Remove(p.Key), () => l.objects[p.Key] = p.Value)).ToList();
        Commit(who, steps, () => commit(packed));
        Dirty.Add(l); SafeLightUpdate(); return null;
    }
    private static void Commit(Farmer who, List<AtomicStep> steps, Action receipt)
    {
        string key = TerminalNetwork.ReceiptKey(who.UniqueMultiplayerID);
        string? old = Game1.MasterPlayer.modData.GetValueOrDefault(key);
        steps.Add(new(receipt, () => { if (old == null) Game1.MasterPlayer.modData.Remove(key); else Game1.MasterPlayer.modData[key] = old; }));
        AtomicChange.Execute(() => Context.IsMainPlayer && Context.IsWorldReady && !IntegrationHost.Saving, steps);
    }
    internal void Repair(GameLocation l)
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady || IntegrationHost.Saving) return;
        var anchors = l.objects.Pairs.Where(p => OrderTerminal.IsTerminal(p.Value) && !TerminalEntity.IsPart(p.Value)).ToArray();
        foreach (var pair in anchors)
        {
            var anchor = pair.Value; string id = TerminalEntity.Id(anchor);
            if (!Guid.TryParseExact(id, "N", out _)) id = Guid.NewGuid().ToString("N");
            TerminalEntity.Stamp(anchor, l, pair.Key, pair.Key, id, false);
            if (anchor.owner.Value == 0) anchor.owner.Value = l is FarmHouse house ? house.OwnerId : Game1.MasterPlayer.UniqueMultiplayerID;
            // Mark prior physical delivery before migration; never use a new mail gift to recover a blocked terminal.
            var owner = Game1.GetPlayer(anchor.owner.Value);
            if (owner != null) owner.modData[OrderTerminal.LegacyDeliveryKey] = "received-before-mail";
            var ownParts = l.objects.Pairs.Where(p => TerminalEntity.IsPart(p.Value) && TerminalEntity.Id(p.Value) == id).ToArray();
            bool allowed = TerminalEntity.SupportedMap(l);
            bool room = allowed && TerminalEntity.Tiles(pair.Key).All(t => TerminalEntity.ClearTile(l, t, id));
            if (room && TerminalEntity.Complete(l, anchor) && ownParts.Length == TerminalEntity.Width - 1
                && ownParts.All(p => TerminalEntity.Tiles(pair.Key).Contains(p.Key))) continue;
            // Remove only reservations belonging to this anchor. An external move or a blocked legacy
            // expansion leaves the one original object, available for a single safe pickup.
            // In a blocked 2x1 migration, keep its existing valid reservation instead of
            // shrinking the old device or claiming the conflicted expansion cells.
            var removedParts = room ? ownParts : ownParts.Where(p => !allowed || !TerminalEntity.Tiles(pair.Key).Contains(p.Key)
                || p.Value.TileLocation != p.Key || p.Value.boundingBox.Value != TerminalEntity.CellBounds(p.Key)
                || p.Value.isTemporarilyInvisible || !TerminalEntity.ClearTile(l, p.Key, id)).ToArray();
            var steps = removedParts.Select(p => new AtomicStep(() => l.objects.Remove(p.Key), () => l.objects[p.Key] = p.Value)).ToList();
            string? oldPending = anchor.modData.GetValueOrDefault(TerminalEntity.PendingKey);
            var oldBounds = anchor.boundingBox.Value;
            steps.Add(new(() =>
            {
                if (room) anchor.modData.Remove(TerminalEntity.PendingKey); else anchor.modData[TerminalEntity.PendingKey] = "reposition";
                anchor.boundingBox.Value = room ? TerminalEntity.Bounds(pair.Key) : TerminalEntity.CellBounds(pair.Key);
            }, () =>
            {
                if (oldPending == null) anchor.modData.Remove(TerminalEntity.PendingKey); else anchor.modData[TerminalEntity.PendingKey] = oldPending;
                anchor.boundingBox.Value = oldBounds;
            }));
            if (room)
                foreach (var tile in TerminalEntity.Tiles(pair.Key).Where(t => t != pair.Key))
                {
                    var part = TerminalEntity.NewPart(l, tile, anchor);
                    steps.Add(new(() => l.objects.Add(tile, part), () => { if (l.objects.TryGetValue(tile, out var value) && ReferenceEquals(value, part)) l.objects.Remove(tile); }));
                }
            AtomicChange.Execute(() => Context.IsMainPlayer && Context.IsWorldReady && !IntegrationHost.Saving, steps);
            if (!room && ReferenceEquals(l, Game1.currentLocation) && MigrationNotices.Add(l.NameOrUniqueName + "/" + id))
                Notice("orders.terminal-migrate");
        }
        // Anchor removal/destruction must not leave collision or an independently recoverable extra item.
        foreach (var pair in l.objects.Pairs.Where(p => TerminalEntity.IsPart(p.Value)).ToArray())
            if (!TerminalEntity.Resolve(l, pair.Key, out _)) l.objects.Remove(pair.Key);
    }
    private void Button(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsPlayerFree || IntegrationHost.Saving || Helper.Input.IsSuppressed(e.Button)) return;
        bool action = e.Button.IsActionButton(), tool = e.Button.IsUseToolButton();
        if (!action && !tool) return;
        // Preserve toolbar selection and scrolling while holding a terminal.
        if (e.Button is SButton.MouseLeft or SButton.MouseRight)
        {
            Game1.PushUIMode();
            try { if (Game1.onScreenMenus.Any(m => m.isWithinBounds(Game1.getMouseX(), Game1.getMouseY()))) return; }
            finally { Game1.PopUIMode(); }
        }
        Vector2 tile = Game1.options.gamepadControls ? e.Cursor.GrabTile : e.Cursor.Tile;
        var l = Game1.currentLocation;
        try
        {
            if (OrderTerminal.IsTerminal(Game1.player.ActiveObject))
            {
                string? rejected = TerminalEntity.PlacementRejection(l, tile, Game1.player);
                if (Monitor.IsVerbose) Monitor.Log($"Terminal placement map={l.NameOrUniqueName} target={tile} anchor={tile} footprint={TerminalEntity.Bounds(tile)} result={rejected ?? "accepted"}", LogLevel.Trace);
                // Invalid placement must fall through to native door/object interaction.
                // No preview object exists and native placement remains escrow-only.
                if (rejected != null)
                {
                    // Tell the player why nothing happened, unless the click has its own native use (door, object, furniture).
                    if (rejected is not ("map" or "range") && !l.objects.ContainsKey(tile) && l.GetFurnitureAt(tile) == null
                        && l.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Action", "Buildings") == null
                        && !l.warps.Any(w => w.X == (int)tile.X && w.Y == (int)tile.Y)) Notice("orders.terminal-blocked");
                    return;
                }
                Helper.Input.Suppress(e.Button); Network.Request(l, tile); return;
            }
            if (!TerminalEntity.Resolve(l, tile, out var anchor)) return;
            Helper.Input.Suppress(e.Button);
            if (!TerminalEntity.InReach(Game1.player, anchor.TileLocation, TerminalEntity.Pending(anchor))) { Notice("orders.return-terminal"); return; }
            if (tool && Game1.player.CurrentTool is Axe or Pickaxe) Network.Request(l, anchor.TileLocation, anchor);
            else if (action) anchor.checkForAction(Game1.player);
        }
        catch (Exception ex) { Error(ex); Notice("orders.terminal-pending"); }
    }
    private static bool CanPlacePatch(SObject __instance, GameLocation l, Vector2 tile, bool showError, ref bool __result)
    {
        if (!OrderTerminal.IsTerminal(__instance))
        {
            if (!l.objects.TryGetValue(tile, out var existing) || !OrderTerminal.IsTerminal(existing)) return true;
            __result = false; return false;
        }
        __result = TerminalEntity.CanPlace(l, tile, Game1.player);
        if (!__result && showError) Instance.Notice(TerminalEntity.Allowed(l, Game1.player) ? "orders.terminal-blocked" : "orders.terminal-map");
        return false;
    }
    private static bool PlacePatch(SObject __instance, GameLocation location, int x, int y, Farmer who, ref bool __result)
    {
        if (!OrderTerminal.IsTerminal(__instance)) return true;
        __result = false; // Only the input -> durable escrow -> host transaction may consume/place a terminal.
        return false;
    }
    private static bool ToolPatch(SObject __instance, ref bool __result)
    { if (!OrderTerminal.IsTerminal(__instance)) return true; __result = false; return false; }
    private static bool ExplosionPatch(SObject __instance, ref bool __result)
    { if (!OrderTerminal.IsTerminal(__instance)) return true; __result = false; return false; }
    private static bool BoundsPatch(SObject __instance, int x, int y, ref Rectangle __result)
    {
        if (!OrderTerminal.IsTerminal(__instance)) return true;
        __result = TerminalEntity.CollisionBounds(__instance, new(x, y)); return false;
    }
    private static bool PassablePatch(SObject __instance, ref bool __result)
    { if (!OrderTerminal.IsTerminal(__instance)) return true; __result = false; return false; }
    private static bool Preview(SObject __instance, SpriteBatch spriteBatch, GameLocation location)
    {
        if (!OrderTerminal.IsTerminal(__instance)) return true;
        var cursor = Instance.Helper.Input.GetCursorPosition();
        var tile = Game1.options.gamepadControls ? cursor.GrabTile : cursor.Tile;
        bool valid = TerminalEntity.CanPlace(location, tile, Game1.player);
        foreach (var cell in TerminalEntity.Tiles(tile))
        {
            Vector2 point = Game1.GlobalToLocal(Game1.viewport, cell * 64);
            spriteBatch.Draw(Game1.staminaRect, new Rectangle((int)point.X, (int)point.Y, 64, 64), (valid ? Color.LimeGreen : Color.Red) * 0.25f);
        }
        OrderTerminal.DrawPlaced(spriteBatch, tile, 0.65f, false);
        OrderTerminal.MarkPlacementPreview(); return false;
    }
}

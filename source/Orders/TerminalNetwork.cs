using System.Globalization;
using System.Text.Json;
using Microsoft.Xna.Framework;
using BetterStardewValley.Transactions;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace BetterStardewValley.Orders;

internal sealed record TerminalRequest(int Schema, string Id, long Sequence, long Actor, string Action, string Location, int X, int Y, string Entity, string Item);
internal sealed record TerminalReceipt(string Id, long Sequence, long Actor, string Action, bool Accepted, string Reason, string Item);

// The inventory owner escrows one item; only the host changes world tiles. Replicated modData
// carries durable intent/receipt, so message loss or disconnect never triggers a speculative refund.
internal sealed class TerminalNetwork
{
    internal const string ModId = "Eddie.BetterStardewValley";
    internal const string PendingKey = OrderRules.Prefix + "TerminalAction.v1", SequenceKey = OrderRules.Prefix + "TerminalSequence";
    private const string ReceiptPrefix = OrderRules.Prefix + "TerminalReceipt/";
    private readonly IModHelper Helper;
    private readonly TerminalWorld World;
    private readonly PerScreen<string> LastNotice = new(() => "");
    private readonly string Version;
    internal TerminalNetwork(IModHelper helper, TerminalWorld world)
    {
        Helper = helper; World = world;
        Version = helper.ModRegistry.Get(ModId)?.Manifest.Version.ToString() ?? typeof(OrderTerminal).Assembly.GetName().Version!.ToString(3);
    }
    internal bool HasPending => Game1.player.modData.ContainsKey(PendingKey);
    internal static string ReceiptKey(long actor) => ReceiptPrefix + actor.ToString(CultureInfo.InvariantCulture);
    internal static bool Valid(TerminalRequest r) => r.Schema == 1 && Guid.TryParseExact(r.Id, "N", out _) && r.Sequence > 0
        && r.Location is { Length: > 0 and <= 256 } && r.X is >= 0 and < 32768 && r.Y is >= 0 and < 32768
        && (r.Action == "place" && r.Item is { Length: > 0 and <= 262144 } && r.Entity == ""
            || r.Action == "pickup" && r.Item == "" && Guid.TryParseExact(r.Entity, "N", out _));
    internal static TerminalRequest? Read(Farmer who)
    {
        if (!who.modData.TryGetValue(PendingKey, out string raw)) return null;
        if (raw.Length > 600000) throw new InvalidOperationException("Oversized terminal request; retained.");
        var value = JsonSerializer.Deserialize<TerminalRequest>(raw);
        if (value == null || value.Actor != who.UniqueMultiplayerID || !Valid(value)) throw new InvalidOperationException("Invalid terminal request; retained.");
        return value;
    }
    internal static TerminalReceipt? Receipt(long actor)
    {
        if (!Game1.MasterPlayer.modData.TryGetValue(ReceiptKey(actor), out var raw)) return null;
        if (raw.Length > 600000) throw new InvalidOperationException("Oversized terminal receipt; retained.");
        var receipt = JsonSerializer.Deserialize<TerminalReceipt>(raw);
        if (receipt == null || receipt.Actor != actor || receipt.Sequence <= 0 || !Guid.TryParseExact(receipt.Id, "N", out _)
            || receipt.Action is not ("place" or "pickup")) throw new InvalidOperationException("Invalid terminal receipt; retained.");
        return receipt;
    }
    private bool PeerMatches(long peer) => peer == Game1.MasterPlayer.UniqueMultiplayerID && Context.IsMainPlayer
        || Context.IsSplitScreen || Helper.Multiplayer.GetConnectedPlayer(peer)?.GetMod(ModId)?.Version.ToString() == Version;
    internal void Request(GameLocation location, Microsoft.Xna.Framework.Vector2 tile, SObject? anchor = null)
    {
        if (!Context.IsWorldReady || IntegrationHost.Saving || !Context.IsPlayerFree) return;
        if (HasPending) { World.Notice("orders.terminal-pending"); return; }
        if (!PeerMatches(Game1.MasterPlayer.UniqueMultiplayerID)) { World.Notice("orders.host-version"); return; }
        var who = Game1.player;
        bool place = anchor == null;
        if (place && !TerminalEntity.CanPlace(location, tile, who))
        { World.Notice(TerminalEntity.Allowed(location, who) ? "orders.terminal-blocked" : "orders.terminal-map"); return; }
        if (!place && (!TerminalEntity.InReach(who, anchor!.TileLocation, TerminalEntity.Pending(anchor)) || who.CurrentTool is not (Axe or Pickaxe))) return;
        SObject? item = place ? who.ActiveObject : anchor;
        if (item == null || !OrderTerminal.IsTerminal(item) || item.Stack < 1 || TerminalEntity.IsPart(item)) return;
        string packed = TerminalItem.Pack(item);
        long.TryParse(who.modData.GetValueOrDefault(SequenceKey), out long previous);
        var request = new TerminalRequest(1, Guid.NewGuid().ToString("N"), checked(Math.Max(previous, Receipt(who.UniqueMultiplayerID)?.Sequence ?? 0) + 1),
            who.UniqueMultiplayerID, place ? "place" : "pickup", location.NameOrUniqueName, (int)tile.X, (int)tile.Y,
            place ? "" : TerminalEntity.Id(anchor!), place ? packed : "");
        if (!Valid(request)) return;
        string raw = JsonSerializer.Serialize(request);
        if (raw.Length > 600000) throw new InvalidOperationException("Terminal request exceeds size limit; inventory unchanged.");
        var before = who.Items.ToArray(); int stack = item.Stack;
        string? oldSequence = who.modData.GetValueOrDefault(SequenceKey);
        try
        {
            if (place) who.reduceActiveItemByOne();
            who.modData[PendingKey] = raw;
            who.modData[SequenceKey] = request.Sequence.ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            item.Stack = stack; Restore(who, before); who.modData.Remove(PendingKey);
            if (oldSequence == null) who.modData.Remove(SequenceKey); else who.modData[SequenceKey] = oldSequence;
            throw;
        }
        Tick();
    }
    internal void Tick()
    {
        if (!Context.IsWorldReady || IntegrationHost.Saving) return;
        if (Context.IsMainPlayer)
            foreach (Farmer who in Game1.getOnlineFarmers())
                try { Process(who); } catch (Exception ex) { World.Error(ex); }
        try { Receive(Game1.player); } catch (Exception ex) { World.Error(ex); }
    }
    internal void Process(Farmer who)
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady || IntegrationHost.Saving) return;
        var request = Read(who); if (request == null) return;
        var prior = Receipt(who.UniqueMultiplayerID);
        if (prior != null && request.Sequence <= prior.Sequence) return; // replay is consumed from the persisted receipt, never executed twice
        var location = who.currentLocation;
        string? error = !PeerMatches(who.UniqueMultiplayerID) ? "orders.host-version" : null;
        if (error == null && (location == null || location.NameOrUniqueName != request.Location || Game1.eventUp
            || (request.Action == "place" ? !TerminalEntity.PlacementReach(who, new(request.X, request.Y))
                : !TerminalEntity.InReach(who, new(request.X, request.Y))))) error = "orders.return-terminal";
        if (error != null) { Store(new(request.Id, request.Sequence, request.Actor, request.Action, false, error, "")); return; }
        var outcome = new TerminalReceipt(request.Id, request.Sequence, request.Actor, request.Action, true, "", "");
        if (request.Action == "place")
            error = World.Place(location!, new(request.X, request.Y), who, request.Id, TerminalItem.Read(request.Item), () => Store(outcome));
        else
            error = World.Pickup(location!, new(request.X, request.Y), who, request.Entity, raw => Store(outcome with { Item = raw }));
        if (error != null) Store(outcome with { Accepted = false, Reason = error });
    }
    private static void Store(TerminalReceipt receipt) => Game1.MasterPlayer.modData[ReceiptKey(receipt.Actor)] = JsonSerializer.Serialize(receipt);
    internal void Receive(Farmer who)
    {
        if (!ReferenceEquals(who, Game1.player)) return; // Never mutate another player's inventory on this client.
        var pending = Read(who); if (pending == null) return;
        var receipt = Receipt(who.UniqueMultiplayerID);
        if (receipt == null || receipt.Id != pending.Id || receipt.Sequence != pending.Sequence || receipt.Action != pending.Action) return;
        bool give = pending.Action == "place" && !receipt.Accepted || pending.Action == "pickup" && receipt.Accepted;
        var before = who.Items.ToArray(); var stacks = before.Select(i => i?.Stack ?? 0).ToArray();
        string raw = who.modData[PendingKey];
        try
        {
            if (give && pending.Action == "pickup")
            {
                // Like vanilla furniture and machines: the terminal pops out as a small icon on the floor
                // and is picked up by walking over it, even when the backpack is full.
                var dropped = TerminalItem.Read(receipt.Item);
                var location = Game1.getLocationFromName(pending.Location) ?? who.currentLocation;
                Game1.createItemDebris(dropped, new Vector2(pending.X * 64 + 32, pending.Y * 64 + 32), who.FacingDirection, location);
            }
            else if (give)
            {
                var item = TerminalItem.Read(pending.Item);
                if (!CanReceive(who, item))
                {
                    if (LastNotice.Value != pending.Id) { World.Notice("orders.terminal-space"); LastNotice.Value = pending.Id; }
                    return; // Durable escrow remains until a slot is available.
                }
                if (!who.addItemToInventoryBool(item)) throw new InvalidOperationException("Terminal refund did not fit; rolling back.");
            }
            who.modData.Remove(PendingKey);
        }
        catch
        {
            for (int i = 0; i < before.Length; i++) if (before[i] != null) before[i]!.Stack = stacks[i];
            Restore(who, before); who.modData[PendingKey] = raw; throw;
        }
        LastNotice.Value = "";
        if (!receipt.Accepted) World.Notice(receipt.Reason);
    }
    private static void Restore(Farmer who, Item[] before)
    {
        who.Items.Clear(); foreach (var item in before) who.Items.Add(item);
    }
    private static bool CanReceive(Farmer who, SObject item) => who.Items.Take(who.MaxItems)
        .Any(slot => slot == null || slot.canStackWith(item) && slot.Stack < slot.maximumStackSize());
}

using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using SObject = StardewValley.Object;

namespace BetterStardewValley.Orders;

/// <summary>
/// Every farmer who already got an order terminal and no longer has one anywhere in the world
/// (thrown in the trash, sold, left on the ground overnight...) finds a new one in the mailbox the
/// next morning, sent by EddieF. The host decides each morning; each player puts the letter in
/// their own mailbox. Terminals remember who first received them, so giving one away is not a loss.
/// </summary>
internal sealed class TerminalReplacement
{
    internal const string MailId = "Eddie.BetterStardewValley_OrderTerminalReplacement";
    internal const string OwnerKey = OrderRules.Prefix + "TerminalOwner.v1";
    private const string DuePrefix = OrderRules.Prefix + "TerminalReplacementDue/";
    private const string AckKey = OrderRules.Prefix + "TerminalReplacementAck.v1";
    private readonly IModHelper Helper;
    private readonly IMonitor Monitor;
    private bool Warned;

    internal TerminalReplacement(IModHelper helper, IMonitor monitor)
    {
        Helper = helper; Monitor = monitor;
        helper.Events.Content.AssetRequested += Assets;
        helper.Events.GameLoop.DayStarted += (_, _) => Safe(() => { TagOwn(); Check(); Deliver(); });
        helper.Events.GameLoop.OneSecondUpdateTicked += (_, _) => Safe(() => { TagOwn(); Deliver(); });
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => Warned = false;
    }

    private void Assets(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("Data/mail"))
            e.Edit(a => a.AsDictionary<string, string>().Data[MailId] = BsvI18n.Get(Helper, "orders.terminal-replace-mail")
                + "%item bigobject " + OrderRules.TerminalId + " %%[#]" + BsvI18n.Get(Helper, "orders.terminal-replace-title"));
    }

    private void Safe(Action action)
    {
        if (!Context.IsWorldReady || IntegrationHost.Saving) return;
        try { action(); }
        catch (Exception ex) { if (!Warned) { Warned = true; Monitor.Log("BSV order terminal replacement skipped: " + ex, LogLevel.Warn); } }
    }

    internal static long? Owner(Item item)
    {
        if (item.modData.TryGetValue(OwnerKey, out var raw) && long.TryParse(raw, out var id)) return id;
        if (item is SObject placed && placed.Location != null && placed.owner.Value != 0 && !TerminalEntity.IsPart(placed)) return placed.owner.Value;
        return null;
    }

    /// <summary>A terminal belongs to the first player who holds it.</summary>
    private static void TagOwn()
    {
        var me = Game1.player;
        foreach (var item in me.Items)
            if (OrderTerminal.IsTerminal(item) && !item.modData.ContainsKey(OwnerKey))
                item.modData[OwnerKey] = me.UniqueMultiplayerID.ToString();
    }

    private static bool Received(Farmer who) => who.mailReceived.Contains(OrderTerminal.MailId) || who.mailReceived.Contains(MailId)
        || who.modData.ContainsKey(OrderTerminal.LegacyDeliveryKey);

    private static bool Waiting(Farmer who) => who.mailbox.Contains(OrderTerminal.MailId) || who.mailbox.Contains(MailId)
        || who.mailForTomorrow.Contains(OrderTerminal.MailId) || who.mailForTomorrow.Contains(MailId)
        || who.modData.ContainsKey(TerminalNetwork.PendingKey); // a terminal on its way back from the host

    private void Check()
    {
        if (!Context.IsMainPlayer) return;
        var owners = new HashSet<long>(); bool unowned = false;
        void See(Item? item)
        {
            if (item == null || !OrderTerminal.IsTerminal(item) || item is SObject o && TerminalEntity.IsPart(o)) return;
            if (Owner(item) is long id) owners.Add(id); else unowned = true;
        }
        Utility.ForEachItem(item => { See(item); return true; });
        foreach (var farmer in Game1.getAllFarmers()) foreach (var item in farmer.Items) See(item);
        foreach (var item in Game1.player.team.returnedDonations) See(item);
        foreach (var pair in Game1.player.team.globalInventories.Pairs) foreach (var item in pair.Value) See(item);

        string token = Game1.Date.TotalDays.ToString();
        foreach (var who in Game1.getOnlineFarmers())
        {
            long id = who.UniqueMultiplayerID;
            if (!Received(who) || Waiting(who) || owners.Contains(id)) continue;
            // Terminals from before ownership was recorded count as the host's.
            if (unowned && who.IsMainPlayer) continue;
            var master = Game1.MasterPlayer.modData;
            if (master.TryGetValue(DuePrefix + id, out var due) && who.modData.GetValueOrDefault(AckKey) != due) continue;
            master[DuePrefix + id] = token;
            Monitor.Log($"BSV: {who.Name} no longer has an order terminal; a new one arrives in the mailbox.", LogLevel.Info);
        }
    }

    /// <summary>Each player puts the letter in their own mailbox once per decision.</summary>
    private static void Deliver()
    {
        var me = Game1.player;
        if (!Game1.MasterPlayer.modData.TryGetValue(DuePrefix + me.UniqueMultiplayerID, out var token)) return;
        if (me.modData.GetValueOrDefault(AckKey) == token) return;
        if (!me.mailbox.Contains(MailId)) me.mailbox.Add(MailId);
        me.modData[AckKey] = token;
    }
}

using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;

namespace BetterStardewValley.DeveloperAppreciation;

internal sealed class DeveloperLetterService
{
    internal static DeveloperLetterService? Instance;
    private readonly IModHelper Helper;
    private readonly IMonitor Monitor;
    private readonly PerScreen<bool> CheckPending = new(() => true);
    private readonly PerScreen<LetterViewerMenu?> Active = new(() => null);
    private readonly PerScreen<bool> Saving = new(() => false);
    private readonly HashSet<string> Reported = new();
    internal DeveloperLetterService(IModHelper helper, IMonitor monitor, string modId)
    {
        Helper = helper; Monitor = monitor;
        var harmony = new Harmony(modId + ".DeveloperAppreciation");
        try { DeveloperLetterAttachment.Install(harmony); }
        catch { harmony.UnpatchAll(harmony.Id); throw; }
        Instance = this;
        helper.Events.Content.AssetRequested += (_, e) =>
        {
            if (e.NameWithoutLocale.IsEquivalentTo("Data/mail"))
                e.Edit(a => a.AsDictionary<string, string>().Data[DeveloperLetterRules.Id] = DeveloperLetterRules.Letter(k => BsvI18n.Get(helper, k)), BetterStardewValley.BsvAssets.EditPriority);
        };
        helper.Events.GameLoop.SaveLoaded += (_, _) => CheckPending.Value = true;
        helper.Events.GameLoop.Saving += (_, _) => Saving.Value = true;
        helper.Events.GameLoop.Saved += (_, _) => Saving.Value = false;
        helper.Events.GameLoop.DayStarted += (_, _) => CheckPending.Value = true;
        helper.Events.Player.Warped += (_, e) => { if (e.IsLocalPlayer) CheckPending.Value = true; };
        helper.Events.GameLoop.OneSecondUpdateTicked += (_, _) => Coordinate();
        helper.Events.Content.AssetsInvalidated += (_, e) =>
        { if (e.NamesWithoutLocale.Any(n => n.IsEquivalentTo("Data/Objects"))) CheckPending.Value = true; };
        helper.Events.Display.MenuChanged += (_, e) =>
        {
            if (e.OldMenu == Active.Value && Active.Value != null)
            {
                Active.Value.itemsToGrab.Clear(); Active.Value = null;
                CheckPending.Value = true;
            }
        };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        { Active.ResetAllScreens(); CheckPending.ResetAllScreens(); Saving.ResetAllScreens(); Reported.Clear(); };
    }
    private void Coordinate()
    {
        if (!CheckPending.Value || !Context.IsWorldReady || Saving.Value || Game1.player?.IsLocalPlayer != true
            || Game1.eventUp || Game1.activeClickableMenu != null || Game1.currentMinigame != null) return;
        CheckPending.Value = false;
        try
        {
            var who = Game1.player;
            if (!DeveloperLetterRules.Eligible(Game1.year, Game1.season, Game1.dayOfMonth)) return;
            if (ItemRegistry.GetDataOrErrorItem(WineRules.BloodOfJesus).IsErrorItem)
            { Error("reward", "Gift item is unavailable; entitlement retained. Retry on next day/load or item-data invalidation."); return; }
            if (!DataLoader.Mail(Game1.content).ContainsKey(DeveloperLetterRules.Id))
            { Error("mail", "Mail data unavailable; entitlement retained."); return; }
            Queue(who, message => Error("state:" + who.UniqueMultiplayerID, message));
        }
        catch (Exception ex) { Error("delivery", ex.ToString()); }
    }
    internal static bool Queue(Farmer who, Action<string> error)
    {
        if (!who.IsLocalPlayer) return false;
        string? state = who.modData.TryGetValue(DeveloperLetterRules.StateKey, out var saved) ? saved : null;
        if (state == DeveloperLetterRules.Claimed) return false;
        if (state != null && state != DeveloperLetterRules.Pending)
        { error("Unknown or interrupted gift state preserved; no replacement reward created."); return false; }
        if (state == null && who.mailReceived.Contains(DeveloperLetterRules.Id))
        { error("Historical read record without gift receipt: cannot prove unclaimed; no automatic duplicate."); return false; }
        if (state == null) who.modData[DeveloperLetterRules.StateKey] = DeveloperLetterRules.Pending;
        if (who.mailbox.Contains(DeveloperLetterRules.Id) || who.mailForTomorrow.Contains(DeveloperLetterRules.Id)) return false;
        who.mailbox.Add(DeveloperLetterRules.Id);
        return true;
    }
    internal bool Begin(LetterViewerMenu menu)
    {
        var who = Game1.player;
        if (menu.isFromCollection || who?.IsLocalPlayer != true || !IsPending(who)) return false;
        // One live attachment lease per local screen. Old menu callbacks cannot claim.
        if (Active.Value != null && Active.Value != menu) Active.Value.itemsToGrab.Clear();
        Active.Value = menu;
        return true;
    }
    internal static bool IsPending(Farmer who) => who.modData.TryGetValue(DeveloperLetterRules.StateKey, out var state) && state == DeveloperLetterRules.Pending;
    internal void Transfer(LetterViewerMenu menu, Farmer who, Item item)
    {
        if (Active.Value != menu || who != Game1.player || !who.IsLocalPlayer || menu.isFromCollection || !IsPending(who)) return;
        if (item.QualifiedItemId != WineRules.BloodOfJesus || item.Stack != 1 || ItemRegistry.GetDataOrErrorItem(item.QualifiedItemId).IsErrorItem)
        { Error("attachment", "Unexpected gift attachment preserved as pending; not delivered."); return; }
        TransferOne(who, item, message => Error("transfer:" + who.UniqueMultiplayerID, message));
        if (IsPending(who))
            Game1.showRedMessage(BsvI18n.Get(Helper, "developer-letter.full"));
    }
    internal static bool TransferOne(Farmer who, Item item, Action<string> error)
    {
        if (!who.IsLocalPlayer || !IsPending(who) || item.QualifiedItemId != WineRules.BloodOfJesus || item.Stack != 1) return false;
        who.modData[DeveloperLetterRules.StateKey] = DeveloperLetterRules.Transferring;
        try
        {
            // Real native inventory insertion; null means fully transferred, including
            // merging into an existing stack. No overflow menu or ground debris owns it.
            Item? remainder = who.addItemToInventory(item);
            who.modData[DeveloperLetterRules.StateKey] = remainder == null ? DeveloperLetterRules.Claimed : DeveloperLetterRules.Pending;
            return remainder == null;
        }
        catch (Exception ex)
        {
            // An arbitrary third-party hook can throw after changing inventory. Do not
            // turn an uncertain transfer into a new gift; keep its receipt for diagnosis.
            error("Gift insertion interrupted; transfer record retained, automatic reissue blocked. " + ex);
            return false;
        }
    }
    private void Error(string key, string message)
    { if (Reported.Add(key)) Monitor.Log(BsvI18n.Get(Helper,"developer-letter.error")+"\nDeveloper appreciation letter: " + message, LogLevel.Error); }
}

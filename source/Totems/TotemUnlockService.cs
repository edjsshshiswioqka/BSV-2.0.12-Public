using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using SObject = StardewValley.Object;
namespace BetterStardewValley.Totems;

// A — local-player progression, native recipe mail, and read-only observation of committed shipping.
// Never patches inventory consumption, Orders, shared totem cooldowns, or server admission.
internal sealed class TotemUnlockService
{
    private static TotemUnlockService Instance = null!;
    private readonly IModHelper Helper;
    private readonly IMonitor Monitor;
    private readonly HashSet<long> Warned = new();
    private HashSet<string>? CropIds;
    internal TotemUnlockService(IModHelper helper, IMonitor monitor, IManifest manifest)
    {
        Instance = this; Helper = helper; Monitor = monitor;
        var h = new Harmony(manifest.UniqueID + ".TotemUnlocks");
        try
        {
            h.Patch(AccessTools.Method(typeof(ShippingMenu), nameof(ShippingMenu.parseItems)),
                prefix: new HarmonyMethod(typeof(TotemUnlockService), nameof(ShippingBefore)),
                postfix: new HarmonyMethod(typeof(TotemUnlockService), nameof(ShippingAfter)));
            h.Patch(AccessTools.Method(typeof(Farmer), nameof(Farmer.OnItemReceived)),
                postfix: new HarmonyMethod(typeof(TotemUnlockService), nameof(ItemReceived)));
        }
        catch { h.UnpatchAll(h.Id); throw; }
        helper.Events.Content.AssetRequested += Assets;
        helper.Events.Content.AssetsInvalidated += (_, _) => CropIds = null;
        helper.Events.GameLoop.SaveLoaded += (_, _) => Refresh();
        helper.Events.GameLoop.DayStarted += (_, _) => Refresh();
        helper.Events.GameLoop.OneSecondUpdateTicked += (_, _) => Refresh();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { Warned.Clear(); CropIds = null; };
        helper.ConsoleCommands.Add("bsv_totem_status", Tr("totem.unlock.command"), (_, _) => Status());
    }
    private string Tr(string key, object? tokens = null) => BsvI18n.Get(Helper, key, tokens);
    private void Assets(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes")) e.Edit(a =>
        {
            var data = a.AsDictionary<string, string>().Data;
            foreach (var r in TotemUnlockRules.Recipes) data[r.Id] = r.Data(Tr(r.Key + ".name"));
        });
        if (e.NameWithoutLocale.IsEquivalentTo("Data/mail")) e.Edit(a =>
        {
            var data = a.AsDictionary<string, string>().Data;
            foreach (var r in TotemUnlockRules.Recipes)
                data[r.Mail] = Tr(r.Key + ".mail") + "%item craftingrecipe " + r.Id + " %%[#]" + Tr(r.Key + ".mail-title");
            data[AllRecipesMail] = Tr("totem.all.mail") + "[#]" + Tr("totem.all.mail-title");
        });
    }
    private bool IsCrop(string id)
    {
        CropIds ??= DataLoader.Crops(Game1.content).Where(p => p.Key is not ("495" or "496" or "497" or "498"))
            .Select(p => Plain(p.Value.HarvestItemId)).ToHashSet(StringComparer.Ordinal);
        return CropIds.Contains(Plain(id));
    }
    private static string Plain(string id) => id.StartsWith("(O)", StringComparison.Ordinal) ? id[3..] : id;
    private TotemUnlockState Read(Farmer who)
    {
        if (who.modData.TryGetValue(TotemUnlockRules.StateKey, out string raw)) return TotemUnlockState.Load(raw);
        var s = new TotemUnlockState { LegacyHistoryIncomplete = Game1.Date.TotalDays > 1 };
        // Prior Orders observation is evidence of possession only, never of shipping.
        if (Game1.getFarm().modData.TryGetValue(Orders.OrderRules.Prefix + "Farmer/" + who.UniqueMultiplayerID, out var book))
        {
            try { s.Battery = Orders.OrderBook.Load(book).Known.Contains("(O)787"); }
            catch { /* Unrelated invalid/future order data is left untouched. */ }
        }
        return s;
    }
    private static void Write(Farmer who, TotemUnlockState s)
    {
        string next = s.Save();
        if (!who.modData.TryGetValue(TotemUnlockRules.StateKey, out var old) || old != next) who.modData[TotemUnlockRules.StateKey] = next;
    }
    private void ObserveHistory(Farmer who, TotemUnlockState s)
    {
        long crops = 0, artisan = 0;
        foreach (var id in who.basicShipped.Keys)
        {
            int quantity = Math.Max(0, who.basicShipped[id]);
            string plain = Plain(id);
            if (quantity > 0 && plain == "787") s.Battery = true;
            if (IsCrop(plain)) crops += quantity;
            if (Game1.objectData.TryGetValue(plain, out var data) && data.Category == SObject.artisanGoodsCategory) artisan += quantity;
        }
        s.Crops = Math.Max(s.Crops, (int)Math.Min(50, crops));
        s.Artisan = Math.Max(s.Artisan, (int)Math.Min(25, artisan));
        s.Battery |= who.Items.Any(i => i?.QualifiedItemId == "(O)787" && i.Stack > 0);
    }
    private void Reconcile(Farmer who, TotemUnlockState s, int day)
    {
        foreach (var r in TotemUnlockRules.Recipes)
        {
            var e = s.Entries[r.Kind];
            e.RecipeUnlocked |= who.craftingRecipes.ContainsKey(r.Id);
            bool present = who.mailbox.Contains(r.Mail) || who.mailReceived.Contains(r.Mail);
            if (present && e.Received < 0) TotemUnlockRules.Delivered(s, r, day);
            // A recipe known before this update stays known. Cancel our stale pending letter only.
            if (e.RecipeUnlocked)
            {
                who.mailForTomorrow.Remove(r.Mail);
                who.mailbox.Remove(r.Mail);
            }
        }
    }
    // Farming level 8: the Wizard writes the next morning and the letter teaches all four totems at once.
    internal const string AllRecipesMail = "Eddie.BetterStardewValley_TotemRecipesWizard";
    internal const int UnlockFarmingLevel = 8;
    private void Refresh()
    {
        if (!Context.IsWorldReady || IntegrationHost.Saving || Game1.eventUp || !Game1.player.IsLocalPlayer) return;
        var who = Game1.player;
        try
        {
            if (who.mailReceived.Contains(AllRecipesMail))
            {
                foreach (var r in TotemUnlockRules.Recipes)
                    if (!who.craftingRecipes.ContainsKey(r.Id)) who.craftingRecipes.Add(r.Id, 0);
                return;
            }
            bool knowsAll = TotemUnlockRules.Recipes.All(r => who.craftingRecipes.ContainsKey(r.Id));
            if (who.farmingLevel.Value < UnlockFarmingLevel || knowsAll) return;
            // The old one-totem letters are no longer sent; an unread one waiting in the mailbox is withdrawn.
            foreach (var r in TotemUnlockRules.Recipes)
                if (!who.mailReceived.Contains(r.Mail)) { who.mailbox.Remove(r.Mail); who.mailForTomorrow.Remove(r.Mail); }
            if (!who.mailbox.Contains(AllRecipesMail) && !who.mailForTomorrow.Contains(AllRecipesMail)) who.mailForTomorrow.Add(AllRecipesMail);
        }
        catch (Exception ex) { Error(who, ex); }
    }
    private static void ItemReceived(Farmer __instance, Item item, int countAdded)
    {
        if (!__instance.IsLocalPlayer || countAdded <= 0 || item?.QualifiedItemId != "(O)787") return;
        try { var s = Instance.Read(__instance); s.Battery = true; Write(__instance, s); }
        catch (Exception ex) { Instance.Error(__instance, ex); }
    }
    internal sealed record ShippingSnapshot(Farmer Who, int Day, int Crops, int Artisan, int Gold, bool Battery);
    private static void ShippingBefore(IList<Item> items, out ShippingSnapshot? __state)
    {
        __state = null;
        // This is the game's nightly settlement list, not a chest, shop sale, Orders payment or preview.
        var who = Game1.player;
        if (who == null || !who.IsLocalPlayer || !ReferenceEquals(items, who.displayedShippedItems)) return;
        try
        {
            int crops = 0, artisan = 0, gold = 0; bool battery = false;
            foreach (var item in items)
            {
                if (item is not SObject o || o.bigCraftable.Value || o.questItem.Value || o.Stack <= 0) continue;
                if (Instance.IsCrop(o.ItemId))
                {
                    crops = Math.Min(50, crops + Math.Min(50, o.Stack));
                    if (o.Quality >= SObject.highQuality) gold = Math.Min(100, gold + Math.Min(100, o.Stack));
                }
                if (Game1.objectData.TryGetValue(o.ItemId, out var data) && data.Category == SObject.artisanGoodsCategory)
                    artisan = Math.Min(25, artisan + Math.Min(25, o.Stack));
                battery |= o.ItemId == "787";
            }
            __state = new(who, Math.Max(0, Game1.Date.TotalDays - 1), crops, artisan, gold, battery);
        }
        catch (Exception ex) { Instance.Error(who, ex); }
    }
    private static void ShippingAfter(ShippingSnapshot? __state)
    {
        if (__state is not { } p) return;
        try
        {
            var s = Instance.Read(p.Who);
            TotemUnlockRules.Shipping(s, p.Day, p.Crops, p.Artisan, p.Gold, p.Battery);
            // Native count records now contain this night's sale. Merge via max, not addition.
            Instance.ObserveHistory(p.Who, s);
            TotemUnlockRules.ObserveEligibility(s, p.Who.farmingLevel.Value, p.Day);
            TotemUnlockRules.Schedule(s, Game1.Date.TotalDays);
            Write(p.Who, s);
        }
        catch (Exception ex) { Instance.Error(p.Who, ex); }
    }
    private void Error(Farmer who, Exception ex)
    { if (Warned.Add(who.UniqueMultiplayerID)) Monitor.Log(Tr("totem.unlock.error") + "\n" + ex, LogLevel.Error); }
    private void Status()
    {
        if (!Context.IsWorldReady) { Monitor.Log(Tr("totem.unlock.no-save"), LogLevel.Info); return; }
        try
        {
            var s = Read(Game1.player);
            Monitor.Log(Tr("totem.unlock.progress", new { farming = Game1.player.farmingLevel.Value, battery = s.Battery, crops = s.Crops, artisan = s.Artisan, gold = s.GoldCrops }), LogLevel.Info);
            foreach (var r in TotemUnlockRules.Recipes)
            {
                var e = s.Entries[r.Kind];
                Monitor.Log(Tr("totem.unlock.status", new { name = Tr(r.Key + ".name"), eligible = e.Eligible, scheduled = e.Scheduled, received = e.Received, known = Game1.player.craftingRecipes.ContainsKey(r.Id) }), LogLevel.Info);
            }
            if (s.LegacyHistoryIncomplete) Monitor.Log(Tr("totem.unlock.legacy"), LogLevel.Info);
        }
        catch (Exception ex) { Error(Game1.player, ex); }
    }
}

using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.GameData.Machines;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;
using SObject = StardewValley.Object;

namespace BetterStardewValley;

public sealed class GrapeWineFeature
{
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private static GrapeWineFeature instance = null!;
    private readonly HashSet<string> loggedErrors = new();
    private readonly HashSet<SObject> kegs = new();
    private const string GrapeTexture = "Mods/" + WineRules.Prefix + "/GrapeWine";
    private const string BloodTexture = "Mods/" + WineRules.Prefix + "/BloodOfJesus";

    internal GrapeWineFeature(IModHelper helper, IMonitor monitor)
    {
        instance = this;
        this.helper = helper;
        this.monitor = monitor;
        var harmony = new Harmony(WineRules.Prefix + ".Wine");
        try
        {
        harmony.Patch(AccessTools.Method(typeof(SObject), nameof(SObject.minutesElapsed), new[] { typeof(int) }),
            prefix: new HarmonyMethod(typeof(GrapeWineFeature), nameof(BeforeMinutes)) { priority = Priority.First });
        harmony.Patch(AccessTools.Method(typeof(SObject), nameof(SObject.checkForAction), new[] { typeof(Farmer), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(GrapeWineFeature), nameof(BeforeCollect)) { priority = Priority.First });
        }
        catch { harmony.UnpatchAll(WineRules.Prefix + ".Wine"); throw; }
        // Serialized bottles can retain their old NetInt price. Normalize this exact ID before
        // native pricing, preserving native quality, artisan and profit-margin calculations.
        foreach (var method in new[] { AccessTools.PropertyGetter(typeof(SObject), nameof(SObject.Price)),
            AccessTools.Method(typeof(SObject), nameof(SObject.sellToStorePrice)),
            AccessTools.Method(typeof(SObject), nameof(SObject.salePrice)) })
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(GrapeWineFeature), nameof(NormalizePrice)));
        helper.Events.GameLoop.SaveLoaded += (_, _) =>
        {
            if (Context.IsMainPlayer) Utility.ForEachItem(item => { if (item is SObject obj) NormalizePrice(obj); return true; });
        };
        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.Content.LocaleChanged += (_, _) => helper.GameContent.InvalidateCache("Data/Objects");
        helper.Events.GameLoop.SaveLoaded += (_, _) => ResolveWorld();
        helper.Events.GameLoop.DayStarted += (_, _) => ResolveWorld();
        helper.Events.GameLoop.Saving += (_, _) => ResolveWorld();
        helper.Events.GameLoop.OneSecondUpdateTicked += (_, _) => ResolveKegs();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { kegs.Clear(); loggedErrors.Clear(); };
        helper.Events.World.LocationListChanged += (_, _) => ResolveWorld();
        helper.Events.World.ObjectListChanged += (_, e) =>
        {
            if (!Context.IsWorldReady || !Game1.IsMasterGame) return;
            foreach (var pair in e.Removed) kegs.Remove(pair.Value);
            foreach (var pair in e.Added) if (WineRules.IsIndexedKeg(pair.Value.QualifiedItemId)) kegs.Add(pair.Value);
        };
        helper.ConsoleCommands.Add("bsv_wine_status", BsvI18n.Get(helper,"cmd.wine-status"), (_, _) => Status());
    }

    private static void NormalizePrice(SObject __instance)
    {
        if (__instance.QualifiedItemId == WineRules.GrapeWine && __instance.price.Value != WineRules.GrapeWinePrice)
            __instance.price.Value = WineRules.GrapeWinePrice;
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(GrapeTexture))
            e.LoadFromModFile<Texture2D>("assets/items/grape_wine.png", AssetLoadPriority.Exclusive);
        else if (e.NameWithoutLocale.IsEquivalentTo(BloodTexture))
            e.LoadFromModFile<Texture2D>("assets/items/blood_of_jesus.png", AssetLoadPriority.Exclusive);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Crops"))
            e.Edit(a => Guard("crops", () => WineData.EditCrops(a.AsDictionary<string, CropData>().Data)), AssetEditPriority.Late);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
            e.Edit(a => Guard("shops", () => WineData.EditShops(a.AsDictionary<string, ShopData>().Data)), AssetEditPriority.Late);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
            e.Edit(a => Guard("objects", () => WineData.EditObjects(a.AsDictionary<string, ObjectData>().Data,
                k => BetterStardewValley.BsvI18n.Get(helper, k).ToString(), GrapeTexture, BloodTexture)), AssetEditPriority.Late);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Machines"))
            e.Edit(a => Guard("machines", () => WineData.EditMachines(a.AsDictionary<string, MachineData>().Data)), AssetEditPriority.Late);
    }

    // This is the native MachineOutputDelegate. Probe calls never roll or modify a machine.
    public static Item? OutputGrapeWine(SObject machine, Item inputItem, bool probe,
        MachineItemOutput outputData, Farmer player, out int? overrideMinutesUntilReady)
    {
        overrideMinutesUntilReady = null;
        if (machine.QualifiedItemId != WineRules.BsvKeg || inputItem?.QualifiedItemId != "(O)" + WineRules.GrapeId) return null;
        var item = ItemRegistry.Create(WineRules.GrapeWine, 1, 0);
        if (!probe)
        {
            item.modData[WineRules.PendingKey] = "1";
            item.modData[WineRules.BatchKey] = Guid.NewGuid().ToString("N");
        }
        return item;
    }

    private static void BeforeMinutes(SObject __instance) => instance.Guard("resolve", () => Resolve(__instance));

    private static bool BeforeCollect(SObject __instance, bool justCheckingForActivity, ref bool __result)
    {
        if (justCheckingForActivity || !WineRules.IsIndexedKeg(__instance.QualifiedItemId)) return true;
        instance.Guard("resolve", () => Resolve(__instance));
        if (__instance.heldObject.Value?.modData.ContainsKey(WineRules.PendingKey) != true) return true;
        // A farmhand waits for the authoritative output rather than removing an unresolved bottle.
        __result = false;
        return false;
    }

    internal static void Resolve(SObject machine, Func<int, int>? draw = null)
    {
        var pending = machine.heldObject.Value;
        if (Game1.IsMasterGame && machine.QualifiedItemId == WineRules.Keg && pending?.QualifiedItemId == WineRules.GrapeWine
            && pending.modData.ContainsKey(WineRules.PendingKey))
        {
            // Pre-separation batch: preserve its existing bottle, stack, quality and timer.
            // Its old unrecorded random outcome cannot be reconstructed. Never roll a vanilla keg.
            pending.modData.Remove(WineRules.PendingKey);
            pending.modData.Remove(WineRules.BatchKey);
            return;
        }
        if (!WineRules.ShouldResolve(Game1.IsMasterGame, machine.QualifiedItemId,
            pending?.QualifiedItemId, pending?.modData.ContainsKey(WineRules.PendingKey) == true)) return;
        // Build both possible results before drawing: a failed item lookup cannot consume a roll.
        var normal = (SObject)pending!.getOne();
        var rare = ItemRegistry.Create<SObject>(WineRules.BloodOfJesus, 1, pending.Quality);
        foreach (var pair in pending.modData.Pairs) rare.modData[pair.Key] = pair.Value;
        normal.modData.Remove(WineRules.PendingKey);
        rare.modData.Remove(WineRules.PendingKey);
        normal.modData.Remove(WineRules.BatchKey);
        rare.modData.Remove(WineRules.BatchKey);
        normal.Stack = rare.Stack = 1;
        bool won = WineRules.IsRare(draw?.Invoke(WineRules.BloodOfJesusDenominator)
            ?? (pending.modData.TryGetValue(WineRules.BatchKey, out string token) ? WineRules.BatchDraw(token) : 1));
        // A single net-field assignment publishes an already-resolved native Object. No new type
        // or external save ledger: the item ID, quality, timer and output serialize natively.
        machine.heldObject.Value = won ? rare : normal;
    }

    private void ResolveWorld()
    {
        if (!Context.IsWorldReady || !Game1.IsMasterGame) return;
        kegs.Clear();
        Guard("index", () => Utility.ForEachLocation(location =>
        {
            foreach (var machine in location.objects.Values)
                if (WineRules.IsIndexedKeg(machine.QualifiedItemId)) kegs.Add(machine);
            return true;
        }, includeInteriors: true, includeGenerated: true));
        ResolveKegs();
    }

    private void ResolveKegs()
    {
        if (!Context.IsWorldReady || !Game1.IsMasterGame) return;
        foreach (var machine in kegs.ToArray())
        {
            var location=machine.Location;
            if(location==null || !location.objects.TryGetValue(machine.TileLocation,out var live) || !ReferenceEquals(machine,live))
            { kegs.Remove(machine);continue; }
            if(machine.heldObject.Value?.modData.ContainsKey(WineRules.PendingKey)==true)
                Guard("resolve",()=>Resolve(machine));
        }
    }

    private void Guard(string key, Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            if (loggedErrors.Add(key)) monitor.Log($"BSV wine feature ({key}) failed: {ex}", LogLevel.Error);
        }
    }

    private void Status()
    {
        if (!Context.IsWorldReady) { monitor.Log("Load a save first.", LogLevel.Info); return; }
        Guard("status", () =>
        {
            var crop = helper.GameContent.Load<Dictionary<string, CropData>>("Data/Crops")[WineRules.GrapeSeedId];
            var machines = helper.GameContent.Load<Dictionary<string, MachineData>>("Data/Machines");
            monitor.Log($"BSV grape: seasons={string.Join(',', crop.Seasons)}, growth={crop.DaysInPhase.Sum()}, regrow={crop.RegrowDays}; seed price={WineRules.GrapeSeedPrice}; host={Game1.IsMasterGame}.", LogLevel.Info);
            foreach (var id in new[] { WineRules.GrapeWine, WineRules.BloodOfJesus })
                monitor.Log($"{id}: valid={ItemRegistry.GetData(id) != null}; cask rule={machines[WineRules.Cask].OutputRules.Any(r => r.Triggers.Any(t => t.RequiredItemId == id))}", LogLevel.Info);
            monitor.Log($"BSV keg grape rule={machines[WineRules.BsvKeg].OutputRules.Any(r => r.Id == WineRules.KegRuleId)}; vanilla overridden={machines[WineRules.Keg].OutputRules.Any(r => r.Id == WineRules.KegRuleId)}; rare chance=1/{WineRules.BloodOfJesusDenominator} (host only, replacement).", LogLevel.Info);
        });
    }
}

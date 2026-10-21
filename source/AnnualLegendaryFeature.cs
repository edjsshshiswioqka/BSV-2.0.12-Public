using System;
using System.Collections.Generic;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Locations;

namespace BetterStardewValley;

internal sealed class AnnualLegendaryFeature
{
    private static AnnualLegendaryFeature Instance = null!;
    private readonly IMonitor monitor;
    private readonly IModHelper helper;
    private string T(string key,object? args=null)=>BsvI18n.Get(helper,key,args);
    private bool active;
    private readonly bool InitialOriginalsEnabled;
    private bool OriginalsEnabled=>Configuration.SharedPreferences.HasLocal?Configuration.SharedPreferences.Current.EnableAnnualLegendaryFish:InitialOriginalsEnabled;
    private bool Handles(string id) => OriginalsEnabled || id == "Eddie.BetterStardewValley_SpiritFish";
    internal bool Initialized => active;
    private bool loggedError;

    internal AnnualLegendaryFeature(IModHelper helper, IMonitor monitor, string modId, bool enabled)
    {
        Instance = this;
        this.monitor = monitor;
        this.helper = helper;
        helper.ConsoleCommands.Add("bsv_fish_status", T("cmd.fish-status"), ShowStatus);
        InitialOriginalsEnabled = enabled;

        string owner = modId + ".AnnualLegendaryFish";
        var harmony = new Harmony(owner);
        try
        {
            var requirements = AccessTools.Method(typeof(GameLocation), "CheckGenericFishRequirements", new[]
            {
                typeof(Item), typeof(Dictionary<string, string>), typeof(GameLocation), typeof(Farmer),
                typeof(SpawnFishData), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
            }) ?? throw new MissingMethodException("GameLocation.CheckGenericFishRequirements was not found.");
            var caught = AccessTools.Method(typeof(Farmer), nameof(Farmer.caughtFish), new[]
            { typeof(string), typeof(int), typeof(bool), typeof(int) })
                ?? throw new MissingMethodException("Farmer.caughtFish was not found.");

            harmony.Patch(requirements,
                postfix: new HarmonyMethod(typeof(AnnualLegendaryFeature), nameof(AfterRequirements)) { priority = Priority.Last });
            harmony.Patch(caught,
                prefix: new HarmonyMethod(typeof(AnnualLegendaryFeature), nameof(BeforeCaughtFish)) { priority = Priority.First },
                postfix: new HarmonyMethod(typeof(AnnualLegendaryFeature), nameof(AfterCaughtFish)) { priority = Priority.Last });
            active = true;
            // Never remove the original limit unless the replacement guard is installed.
            helper.Events.Content.AssetRequested += OnAssetRequested;
            helper.Events.GameLoop.SaveLoaded += (_, _) => InitializeLocalPlayer();
            helper.Events.GameLoop.DayStarted += (_, _) => InitializeLocalPlayer();
            monitor.Log($"BSV annual quota guard initialized; original five enabled={OriginalsEnabled}; Spirit Fish always uses yearly quotas.", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            active = false;
            harmony.UnpatchAll(owner);
            monitor.Log($"BSV could not enable yearly legendary fish. Original lifetime limits remain.\n{ex}", LogLevel.Error);
        }
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!active || !OriginalsEnabled || !e.NameWithoutLocale.IsEquivalentTo("Data/Locations")) return;
        e.Edit(asset =>
        {
            int changed = AnnualLegendaryData.RemoveLifetimeLimits(asset.AsDictionary<string, LocationData>().Data);
            monitor.Log($"BSV replaced {changed} legendary-fish lifetime limits with yearly quotas.", LogLevel.Trace);
        }, AssetEditPriority.Late);
    }

    private static int LifetimeCount(Farmer player, string id) =>
        player.fishCaught.TryGetValue("(O)" + id, out int[] count) && count.Length > 0 ? count[0] : 0;

    private static LegendaryCatchRecord ReadPlayerRecord(Farmer player, string id)
    {
        string key = AnnualLegendaryRules.KeyPrefix + id;
        player.modData.TryGetValue(key, out string? stored);
        var record = AnnualLegendaryRules.ReadRecord(stored, LifetimeCount(player, id), Game1.year);
        string serialized = record.Serialize();
        if (serialized != stored) player.modData[key] = serialized;
        return record;
    }

    internal static bool MachineAvailable(Farmer player,string qualifiedId,int year,bool committed=false,bool historical=false)
    {
        string? id=AnnualLegendaryRules.NormalizeId(qualifiedId);
        if(id==null)return false;
        var record=ReadPlayerRecord(player,id);
        return Instance?.active==true && (committed||Instance.Handles(id)
            ? historical&&year<Game1.year||record.Year<year : record.Year==0&&record.LifetimeCount==0);
    }
    internal static void MarkMachineCatch(Farmer player,string qualifiedId,int year)
    {
        string id=AnnualLegendaryRules.NormalizeId(qualifiedId) ?? throw new ArgumentException("Not a legendary fish.");
        var record=ReadPlayerRecord(player,id);
        player.modData[AnnualLegendaryRules.KeyPrefix+id]=new LegendaryCatchRecord(Math.Max(record.Year,year),LifetimeCount(player,id)).Serialize();
    }

    private void InitializeLocalPlayer()
    {
        if (!active || !Context.IsWorldReady) return;
        try
        {
            foreach (string id in AnnualLegendaryRules.FishIds.Append("Eddie.BetterStardewValley_SpiritFish").Where(Handles)) ReadPlayerRecord(Game1.player, id);
        }
        catch (Exception ex) { LogError(ex); }
    }

    // A postfix can only disallow an otherwise valid catch. Original location,
    // season, weather, skill, magic-bait and quest rules keep running normally.
    private static void AfterRequirements(Item fish, Farmer player, ref bool __result)
    {
        string? id = AnnualLegendaryRules.NormalizeId(fish?.QualifiedItemId);
        if (!Instance.active || !__result || id is null) return;
        try
        {
            if (player is null || !Context.IsWorldReady
                || !MachineAvailable(player,"(O)"+id,Game1.year)
                || Eddie.FishingMachine.MachineLegendaryBridge.Reserved(player.UniqueMultiplayerID,"(O)"+id,Game1.year)
                    && !(SpiritFish.SpiritRules.IsFish(id) && SpiritFish.SpiritQuota.CanCatch(player)))
                __result = false;
        }
        catch (Exception ex)
        {
            __result = false;
            Instance.LogError(ex);
        }
    }

    private sealed record CatchState(string Id, int Before, int Year);

    private static void BeforeCaughtFish(Farmer __instance, string itemId,
        bool from_fish_pond, int numberCaught, out CatchState? __state)
    {
        __state = null;
        string? id = AnnualLegendaryRules.NormalizeId(itemId);
        if (!Instance.active || !Context.IsWorldReady || id is null || from_fish_pond || numberCaught <= 0) return;
        try
        {
            ReadPlayerRecord(__instance, id); // Initialize before vanilla increments history.
            __state = new CatchState(id, LifetimeCount(__instance, id), Game1.year);
        }
        catch (Exception ex) { Instance.LogError(ex); }
    }

    private static void AfterCaughtFish(Farmer __instance, bool from_fish_pond,
        int numberCaught, CatchState? __state)
    {
        if (!Instance.active || __state is null) return;
        try
        {
            int after = LifetimeCount(__instance, __state.Id);
            // caughtFish's return value means NEW SIZE RECORD, not catch success.
            if (!AnnualLegendaryRules.IsSuccessfulCatch(from_fish_pond, numberCaught, __state.Before, after)) return;
            __instance.modData[AnnualLegendaryRules.KeyPrefix + __state.Id] =
                new LegendaryCatchRecord(__state.Year, after).Serialize();
        }
        catch (Exception ex) { Instance.LogError(ex); }
    }

    private void ShowStatus(string command, string[] args)
    {
        if (!Context.IsWorldReady) { monitor.Log(T("diag.load-save"), LogLevel.Info); return; }
        if (!active) { monitor.Log(T("legendary.unavailable"), LogLevel.Info); return; }
        if (!OriginalsEnabled) { monitor.Log(T("legendary.disabled"), LogLevel.Info); return; }
        foreach (string id in AnnualLegendaryRules.FishIds)
        {
            var record = ReadPlayerRecord(Game1.player, id);
            string name = ItemRegistry.GetDataOrErrorItem("(O)" + id).DisplayName;
            string status = T(!AnnualLegendaryRules.IsAvailable(record, Game1.year) ? "legendary.caught"
                : Eddie.FishingMachine.MachineLegendaryBridge.Reserved(Game1.player.UniqueMultiplayerID,"(O)"+id,Game1.year) ? "legendary.reserved" : "legendary.available");
            monitor.Log(T("legendary.status",new{name,status,last=record.Year,year=Game1.year}), LogLevel.Info);
        }
    }

    private void LogError(Exception ex)
    {
        if (loggedError) return;
        loggedError = true;
        monitor.Log($"BSV could not read/update a yearly legendary fish record. Check the log before saving.\n{ex}", LogLevel.Error);
    }
}

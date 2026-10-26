using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Locations;
using xTile;

namespace BetterStardewValley;

/// <summary>
/// Three greenhouse upgrades ordered from Robin once the greenhouse is repaired (Community Center or Joja):
/// 1 orchard to the east (36 tree beds), 2 mushroom cave to the west (35 mushroom boxes), 3 an upper floor
/// identical to the 700-tile greenhouse. Only the host orders; the upgrade is ready the next morning.
/// </summary>
internal static class GreenhouseUpgrade
{
    internal const string Prefix = "Eddie.BetterStardewValley";
    internal const string TierKey = Prefix + "/GreenhouseTier", DueKey = Prefix + "/GreenhouseUpgradeDue", MapTierKey = Prefix + "/GreenhouseTierMap";
    internal const string CaveName = Prefix + "_MushroomCave", UpperName = Prefix + "_Greenhouse2F";
    internal const string CaveMap = "Maps/" + CaveName, UpperMap = "Maps/" + UpperName;
    internal const string ResponseKey = "BSVGreenhouse";
    internal sealed record Cost(int Gold, (string Id, int Count)[] Items);
    internal static readonly Cost[] Costs =
    {
        new(150_000, new[] { ("(O)709", 300), ("(O)338", 100), ("(O)336", 25) }),
        new(300_000, new[] { ("(O)709", 200), ("(O)337", 10), ("(O)422", 10), ("(O)257", 10), ("(O)281", 10) }),
        new(600_000, new[] { ("(O)709", 500), ("(O)338", 300), ("(O)337", 50), ("(O)787", 20), ("(O)74", 1) }),
    };
    private static IModHelper Helper = null!;
    private static IMonitor Monitor = null!;
    private static Func<bool> Enabled = () => true;
    private static bool Failed;
    private static int LastRefresh = -1;

    internal static void Install(IModHelper helper, IMonitor monitor, Func<bool> enabled)
    {
        Helper = helper; Monitor = monitor; Enabled = enabled;
        var h = new Harmony(Prefix + ".GreenhouseUpgrade");
        h.Patch(AccessTools.Method(typeof(GameLocation), nameof(GameLocation.createQuestionDialogue), new[] { typeof(string), typeof(Response[]), typeof(string) })
                ?? throw new MissingMethodException(nameof(GameLocation), nameof(GameLocation.createQuestionDialogue)),
            prefix: new HarmonyMethod(typeof(GreenhouseUpgrade), nameof(BeforeQuestion)));
        h.Patch(AccessTools.Method(typeof(GameLocation), nameof(GameLocation.answerDialogueAction), new[] { typeof(string), typeof(string[]) })
                ?? throw new MissingMethodException(nameof(GameLocation), nameof(GameLocation.answerDialogueAction)),
            prefix: new HarmonyMethod(typeof(GreenhouseUpgrade), nameof(BeforeAnswer)));
        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.DayStarted += (_, _) => Safe(Complete);
        helper.Events.GameLoop.SaveLoaded += (_, _) => Safe(Sync);
        helper.Events.GameLoop.OneSecondUpdateTicked += (_, _) => { if (Context.IsWorldReady) { Safe(Sync); Safe(MigrateCave); } };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { Failed = false; LastRefresh = -1; helper.GameContent.InvalidateCache(a => IsOurMap(a.NameWithoutLocale)); };
    }
    private static bool IsOurMap(IAssetName name) => name.IsEquivalentTo("Maps/Greenhouse") || name.IsEquivalentTo(CaveMap) || name.IsEquivalentTo(UpperMap);
    private static string T(string key, object? tokens = null) => BsvI18n.Get(Helper, "greenhouse-upgrade." + key, tokens);
    private static void Safe(Action action)
    {
        if (Failed) return;
        try { action(); }
        catch (Exception ex) { Failed = true; Monitor.Log("BSV greenhouse upgrade stopped safely; the greenhouse keeps its current state.\n" + ex, LogLevel.Error); }
    }

    // ------------------------------------------------------------------ state
    /// <summary>Completed upgrades (0-3). Readable while a save is still loading, before the world is ready.</summary>
    internal static int Tier
    {
        get
        {
            string? raw = null;
            if (Context.IsWorldReady) Game1.getFarm()?.modData.TryGetValue(TierKey, out raw);
            else SaveGame.loaded?.locations?.FirstOrDefault(l => l.NameOrUniqueName == "Farm")?.modData.TryGetValue(TierKey, out raw);
            return int.TryParse(raw, out int t) ? Math.Clamp(t, 0, Costs.Length) : 0;
        }
    }
    private static int? Due => Game1.getFarm()?.modData.TryGetValue(DueKey, out var raw) == true && int.TryParse(raw, out int d) ? d : null;
    private static bool Repaired => Game1.getFarm()?.greenhouseUnlocked.Value == true;
    private static bool BsvGreenhouse => Enabled() && Game1.getLocationFromName("Greenhouse")?.Map?.Properties.ContainsKey(GreenhouseMap.LayoutKey) == true;
    private static bool RobinBusy => Due.HasValue || Game1.IsThereABuildingUnderConstruction("Robin") || Game1.player.daysUntilHouseUpgrade.Value > 0;

    internal static int Count(Farmer who, string id) => who.Items.Where(i => i?.QualifiedItemId == id).Sum(i => i!.Stack);
    internal static List<(string Id, int Missing)> Missing(Farmer who, Cost cost) =>
        cost.Items.Select(r => (r.Id, Missing: r.Count - Count(who, r.Id))).Where(r => r.Missing > 0).ToList();
    private static void Take(Farmer who, string id, int count)
    {
        for (int i = 0; i < who.Items.Count && count > 0; i++)
        {
            var item = who.Items[i];
            if (item?.QualifiedItemId != id) continue;
            int used = Math.Min(count, item.Stack);
            count -= used;
            if (item.Stack == used) who.Items[i] = null; else item.Stack -= used;
        }
        if (count > 0) throw new InvalidOperationException("Materials changed while paying.");
    }

    // ------------------------------------------------------------------ Robin
    private static void BeforeQuestion(string dialogKey, ref Response[] answerChoices)
    {
        try
        {
            if (dialogKey != "carpenter" || !Context.IsMainPlayer || !Repaired || !BsvGreenhouse || answerChoices.Any(r => r.responseKey == ResponseKey)) return;
            var list = answerChoices.ToList();
            int at = list.FindIndex(r => r.responseKey is "Construct" or "Leave");
            list.Insert(at < 0 ? list.Count : at, new Response(ResponseKey, T("option")));
            answerChoices = list.ToArray();
        }
        catch (Exception ex) { Monitor.Log("BSV could not add the greenhouse upgrade option: " + ex.Message, LogLevel.Warn); }
    }
    private static bool BeforeAnswer(string questionAndAnswer, ref bool __result)
    {
        if (questionAndAnswer != "carpenter_" + ResponseKey) return true;
        __result = true;
        Safe(Offer);
        return false;
    }
    private static string Name(string id) => ItemRegistry.GetDataOrErrorItem(id).DisplayName;
    private static void Offer()
    {
        int tier = Tier;
        Monitor.Log($"BSV greenhouse upgrade: Robin asked at tier {tier} (due={Due?.ToString() ?? "none"}, building={Game1.IsThereABuildingUnderConstruction("Robin")}, house={Game1.player.daysUntilHouseUpgrade.Value}).", LogLevel.Info);
        if (tier >= Costs.Length) { Game1.drawObjectDialogue(T("done-all")); return; }
        if (RobinBusy) { Game1.drawObjectDialogue(T("busy")); return; }
        var cost = Costs[tier];
        string text = T("offer" + (tier + 1), new
        {
            gold = BsvI18n.Number(Helper, cost.Gold, "N0"), hardwood = Name("(O)709"), quartz = Name("(O)338"), goldBar = Name("(O)336"),
            iridiumBar = Name("(O)337"), purple = Name("(O)422"), morel = Name("(O)257"), chanterelle = Name("(O)281"),
            battery = Name("(O)787"), prismatic = Name("(O)74")
        });
        // Robin's explanation can be long in some languages: it is shown as normal paged dialogue, then the question.
        Game1.drawObjectDialogue(text);
        Game1.afterDialogues = () => Game1.currentLocation.createQuestionDialogue(T("ask"),
            new[] { new Response("Yes", T("yes")), new Response("No", T("no")) },
            (who, answer) =>
            {
                Monitor.Log($"BSV greenhouse upgrade {tier + 1}: answer '{answer}'.", LogLevel.Info);
                if (answer == "Yes") Safe(() => Order(who, tier)); else Game1.drawObjectDialogue(T("later"));
            });
    }
    private static void Order(Farmer who, int tier)
    {
        if (!Context.IsMainPlayer || Tier != tier || RobinBusy)
        {
            Monitor.Log($"BSV greenhouse upgrade {tier + 1} not ordered: host={Context.IsMainPlayer}, tier now {Tier}, busy={RobinBusy}.", LogLevel.Info);
            Game1.drawObjectDialogue(T("busy")); return;
        }
        var cost = Costs[tier];
        var missing = Missing(who, cost);
        if (who.Money < cost.Gold || missing.Count > 0)
        {
            var parts = missing.Select(m => T("material", new { count = m.Missing, name = Name(m.Id) })).ToList();
            if (who.Money < cost.Gold) parts.Insert(0, T("gold-missing", new { gold = BsvI18n.Number(Helper, cost.Gold - who.Money, "N0") }));
            Monitor.Log($"BSV greenhouse upgrade {tier + 1} not ordered, missing: gold {Math.Max(0, cost.Gold - who.Money)}; "
                + string.Join(", ", missing.Select(m => $"{m.Id} x{m.Missing}")), LogLevel.Info);
            Game1.drawObjectDialogue(T("missing", new { list = string.Join(T("separator"), parts) }));
            return;
        }
        foreach (var (id, count) in cost.Items) Take(who, id, count);
        who.Money -= cost.Gold;
        Game1.getFarm().modData[DueKey] = (Game1.Date.TotalDays + 1).ToString();
        Game1.playSound("purchaseClick");
        Game1.drawObjectDialogue(T("ordered"));
        Monitor.Log($"BSV greenhouse upgrade {tier + 1} ordered; ready tomorrow.", LogLevel.Info);
    }

    // ------------------------------------------------------------------ completion and maps
    private static void Complete()
    {
        if (!Context.IsMainPlayer || Due is not int due || Game1.Date.TotalDays < due) return;
        var farm = Game1.getFarm();
        int tier = Math.Min(Tier + 1, Costs.Length);
        farm.modData[TierKey] = tier.ToString();
        farm.modData.Remove(DueKey);
        Sync();
        if (tier == 2) PlaceMushroomBoxes();
        Game1.addHUDMessage(new HUDMessage(T("finished" + tier)) { noIcon = true });
        Monitor.Log($"BSV greenhouse upgrade {tier} finished.", LogLevel.Info);
    }
    private static void PlaceMushroomBoxes()
    {
        var cave = Game1.getLocationFromName(CaveName) ?? throw new InvalidOperationException("The mushroom cave location is missing.");
        int placed = 0;
        foreach (var (x, y) in GreenhouseRooms.MushroomBoxes())
        {
            var tile = new Vector2(x, y);
            if (cave.objects.ContainsKey(tile)) continue;
            var box = new StardewValley.Object(tile, "128");
            cave.objects.Add(tile, box);
            placed++;
        }
        cave.modData[GreenhouseRooms.CaveLayoutKey] = GreenhouseRooms.CaveLayout;
        Monitor.Log($"BSV placed {placed} mushroom boxes in the mushroom cave.", LogLevel.Debug);
    }
    /// <summary>A cave built with the first (20 x 28) layout: its mushroom boxes move to the matching spots of the bigger cave.</summary>
    private static void MigrateCave()
    {
        if (!Context.IsMainPlayer || Tier < 2 || Game1.getLocationFromName(CaveName) is not { } cave) return;
        if (cave.modData.TryGetValue(GreenhouseRooms.CaveLayoutKey, out var layout) && layout == GreenhouseRooms.CaveLayout) return;
        int moved = 0;
        foreach (var (from, to) in GreenhouseRooms.OldMushroomBoxes().Zip(GreenhouseRooms.MushroomBoxes()))
        {
            var a = new Vector2(from.X, from.Y); var b = new Vector2(to.X, to.Y);
            if (a == b || !cave.objects.TryGetValue(a, out var obj) || obj.QualifiedItemId != "(BC)128" || cave.objects.ContainsKey(b)) continue;
            cave.objects.Remove(a); obj.TileLocation = b; cave.objects.Add(b, obj); moved++;
        }
        cave.modData[GreenhouseRooms.CaveLayoutKey] = GreenhouseRooms.CaveLayout;
        Monitor.Log($"BSV mushroom cave enlarged; {moved} mushroom boxes moved to the new layout.", LogLevel.Info);
    }
    /// <summary>Reloads the greenhouse map when the saved upgrade level differs from the loaded map.</summary>
    private static void Sync()
    {
        var greenhouse = Game1.getLocationFromName("Greenhouse");
        if (greenhouse?.Map == null) return;
        int tier = Tier;
        string current = greenhouse.Map.Properties.TryGetValue(MapTierKey, out var v) ? v.ToString() : "0";
        if (current == tier.ToString() || tier == 0 && !greenhouse.Map.Properties.ContainsKey(MapTierKey)) return;
        if (!greenhouse.Map.Properties.ContainsKey(GreenhouseMap.LayoutKey)) return;  // not a BSV greenhouse: nothing to add
        if (LastRefresh == tier) return;   // one attempt per level; a failed map edit is logged once, not retried every second
        LastRefresh = tier;
        Helper.GameContent.InvalidateCache("Maps/Greenhouse");
        greenhouse.loadMap(greenhouse.mapPath.Value, force_reload: true);
        greenhouse.updateWarps();
        Monitor.Log($"BSV greenhouse map refreshed for upgrade level {tier}.", LogLevel.Trace);
    }
    private static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("Maps/Greenhouse"))
        {
            e.Edit(asset =>
            {
                var map = asset.AsMap().Data;
                int tier = Tier;
                if (tier <= 0 || !map.Properties.ContainsKey(GreenhouseMap.LayoutKey)) return;   // runs after BSV's own expansion
                try
                {
                    GreenhouseRooms.AddOrchard(map);
                    if (tier >= 2) GreenhouseRooms.AddCaveDoor(map, CaveName);
                    if (tier >= 3) GreenhouseRooms.AddStairs(map, UpperName);
                    map.Properties[MapTierKey] = tier.ToString();
                }
                catch (Exception ex) { Monitor.Log("BSV could not add the greenhouse upgrades to this map; the 700-tile greenhouse stays as it is.\n" + ex, LogLevel.Error); }
            }, BsvAssets.EditPriority + 1);
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Locations"))
        {
            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, LocationData>().Data;
                data[CaveName] = new LocationData { DisplayName = T("cave-name"), CreateOnLoad = new CreateLocationData { MapPath = CaveMap }, ExcludeFromNpcPathfinding = true };
                data[UpperName] = new LocationData { DisplayName = T("upper-name"), CreateOnLoad = new CreateLocationData { MapPath = UpperMap }, ExcludeFromNpcPathfinding = true, CanPlantHere = true };
            }, BsvAssets.EditPriority);
        }
        else if (e.NameWithoutLocale.IsEquivalentTo(CaveMap))
            e.LoadFrom(() => Room("mushroom cave", () => GreenhouseRooms.BuildCave(Helper.GameContent.Load<Map>("Maps/FarmCave"), CaveName, "Greenhouse"), "Maps/FarmCave"), AssetLoadPriority.Exclusive);
        else if (e.NameWithoutLocale.IsEquivalentTo(UpperMap))
            e.LoadFrom(() => Room("upper floor", () => GreenhouseRooms.BuildUpper(Helper.GameContent.Load<Map>("Maps/Greenhouse"), UpperName, "Greenhouse"), "Maps/Greenhouse"), AssetLoadPriority.Exclusive);
    }
    /// <summary>These locations always exist (so their contents are saved); if a room cannot be built on another
    /// mod's maps, a plain copy of the source map is used instead. It is unreachable because no upgrade is offered.</summary>
    private static Map Room(string what, Func<Map> build, string fallback)
    {
        try { return build(); }
        catch (Exception ex)
        {
            Monitor.Log($"BSV could not build the greenhouse {what}; it stays closed. {ex.Message}", LogLevel.Warn);
            return GreenhouseRooms.CopyOf(Helper.GameContent.Load<Map>(fallback), "BSV fallback " + what);
        }
    }
}

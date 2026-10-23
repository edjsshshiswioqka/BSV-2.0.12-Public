using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.TerrainFeatures;

namespace BetterStardewValley;

public sealed partial class ModEntry : Mod
{
    public override object GetApi() => new BsvApi();
    private static ModEntry Instance = null!;
    private ModConfig Config = new();
    private readonly StartupLog Startup = new();
    private bool loggedCaptureError;
    private bool loggedRewardError;
    private GreenhouseFeature Greenhouse = null!;
    private AnnualLegendaryFeature AnnualFish = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        BsvI18n.Register(helper, message => Monitor.Log(message, LogLevel.Warn));
        HdSprites.Install(helper, Monitor, ModManifest.UniqueID);
        Startup.Check(HdSprites.Initialized);
        CoalPriceFeature.Register(helper);
        TranspilerGuard.Monitor = Monitor;
        // Each feature starts on its own, so one failure cannot leave another one's items unregistered (Error Items).
        try { _ = new GrapeWineFeature(helper, Monitor); }
        catch(Exception ex) { Startup.Check(false); Monitor.Log($"BSV grape wine initialization failed; remaining features will continue.\n{ex}",LogLevel.Error); }
        try { _ = new Brewing.BsvKegFeature(helper); }
        catch(Exception ex) { Startup.Check(false); Monitor.Log($"BSV keg initialization failed; remaining features will continue.\n{ex}",LogLevel.Error); }
        try { _ = new DeveloperAppreciation.DeveloperLetterService(helper, Monitor, ModManifest.UniqueID); }
        catch(Exception ex) { Monitor.Log(BsvI18n.Get(helper,"diag.operation-error")+"\n"+BsvI18n.Get(helper,"diag.developer-letter-disabled")+$"\n{ex}",LogLevel.Error); }
        _ = new CellarFeature(helper, Monitor, () => Configuration.SharedPreferences.Current.EnableCellarExpansion, () => Config.EnableCellarAppearance);
        string machineStatus = "diag.machine.waiting";
        string Tr(string key, object? tokens = null) => BsvI18n.Get(helper, key, tokens);
        helper.ConsoleCommands.Add("bsv_machine_status", BsvI18n.Get(helper, "cmd.machine"), (_, _) =>
        {
            Monitor.Log(Tr("diag.machine.header", new { status=Tr(machineStatus), standalone=helper.ModRegistry.IsLoaded("Eddie.FishingMachine") }), LogLevel.Info);
            if (!Context.IsWorldReady)
            {
                Monitor.Log(Tr("diag.load-save"), LogLevel.Info);
                return;
            }
            const string id = "Eddie.FishingMachine_Machine";
            var slots = Eddie.FishingMachine.CraftingSupport.Slots();
            foreach (var requirement in Eddie.FishingMachine.Core.PreparationRules.Requirements)
            {
                bool tool = requirement.Id == Eddie.FishingMachine.Core.PreparationRules.FiberglassRod;
                int count = tool ? slots.Count(x => x.Id == requirement.Id && x.SafeTool && x.Count > 0)
                    : slots.Where(x => x.Id == requirement.Id).Sum(x => Math.Max(0, x.Count));
                Monitor.Log(Tr("diag.machine.material", new {id=requirement.Id,count,needed=requirement.Count}), LogLevel.Info);
            }
            Monitor.Log(Tr("diag.machine.craftable", new {count=Eddie.FishingMachine.CraftingSupport.Count()}), LogLevel.Info);
            Monitor.Log(Tr("diag.machine.recipe",new {level=Game1.player.fishingLevel.Value,learned=Game1.player.craftingRecipes.ContainsKey(id),recipe=CraftingRecipe.craftingRecipes.ContainsKey(id),item=Game1.bigCraftableData.ContainsKey(id)}),LogLevel.Info);
        });
        helper.Events.GameLoop.GameLaunched += (_, _) =>
        {
            if(!Configuration.ConfigFiles.Writable)
            {
                machineStatus="diag.machine.failed";
                Monitor.Log("BSV machine initialization skipped: unresolved configuration recovery; existing machine files preserved.",LogLevel.Error);
                return;
            }
            if (helper.ModRegistry.IsLoaded("Eddie.FishingMachine"))
            {
                machineStatus = "diag.machine.standalone";
                Monitor.Log(Tr("diag.machine.standalone-conflict"), LogLevel.Warn);
                return;
            }
            try
            {
                var machine = new Eddie.FishingMachine.ModEntry(helper, Monitor, ModManifest);
                machine.Entry(helper);
                machineStatus = machine.Ready ? "diag.machine.loaded" : "diag.machine.disabled";
                if (machine.Ready) Monitor.Log("BSV integrated Fishing Machine 3.0 loaded.", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                machineStatus = "diag.machine.failed";
                new Harmony(ModManifest.UniqueID + ".FishingMachine").UnpatchAll(ModManifest.UniqueID + ".FishingMachine");
                Monitor.Log($"BSV fishing machine initialization failed: {ex}", LogLevel.Error);
            }
        };
        Config = Configuration.ConfigFiles.Load(helper,Monitor);
        Startup.Check(Configuration.ConfigFiles.Writable);
        Orders.TerminalEntity.PlacementDistance = Config.HomeTerminalPlacementDistance;
        Config.AfkFishing ??= new();
        string beforeNormalize=Newtonsoft.Json.JsonConvert.SerializeObject(Config);
        Configuration.ConfigPolicy.Normalize(Config);
        if(beforeNormalize!=Newtonsoft.Json.JsonConvert.SerializeObject(Config))
            Monitor.Log("BSV repaired invalid configuration values for this run; original files remain unchanged until a successful configuration save.",LogLevel.Warn);
        Configuration.SharedPreferences.Register(helper, Config);
        Startup.Check(new HorseFeature(helper, Monitor, ModManifest.UniqueID, Config).Initialized);
        Greenhouse = new GreenhouseFeature(helper, Monitor, () => Configuration.SharedPreferences.Current.EnableGreenhouseExpansion);
        AnnualFish = new AnnualLegendaryFeature(helper, Monitor, ModManifest.UniqueID, Config.EnableAnnualLegendaryFish);
        Startup.Check(AnnualFish.Initialized);
        double normalized = HarvestRules.NormalizeChance(Config.SeedChance);
        if (normalized != Config.SeedChance)
        {
            Config.SeedChance = normalized;
            Monitor.Log($"SeedChance was invalid; corrected to {normalized}.", LogLevel.Warn);
        }

        try
        {
            _ = new Farming.FertilizerService(helper,Monitor,ModManifest.UniqueID);
            var target = AccessTools.Method(typeof(Crop), nameof(Crop.harvest), new[]
            {
                typeof(int), typeof(int), typeof(HoeDirt), typeof(JunimoHarvester), typeof(bool)
            });
            if (target is null)
                throw new MissingMethodException("The Crop.harvest signature was not found.");

            var harmony = new Harmony(ModManifest.UniqueID);
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeHarvest)) { priority = Priority.First },
                postfix: new HarmonyMethod(typeof(ModEntry), nameof(AfterHarvest)) { priority = Priority.Last },
                transpiler: new HarmonyMethod(typeof(ModEntry), nameof(CaptureHarvestOutputs)),
                finalizer: new HarmonyMethod(typeof(ModEntry), nameof(FinishHarvestScope)));
            Monitor.Log($"BSV {ModManifest.Version} loaded. Greenhouse: 480 planting tiles. Harvest seed bonus: {(Config.EnableHarvestSeeds ? Config.SeedChance.ToString("P0") : "disabled")}; 1 seed per winning harvest.", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Startup.Check(false);
            Monitor.Log($"BSV could not enable harvest seeds. Original harvesting is unchanged.\n{ex}", LogLevel.Error);
        }
        // Initialize the new systems after the established BSV modules. A conflicting external
        // damage transpiler must not prevent horse, fishing, harvest or greenhouse registration.
        try { _ = new TeaFeature(helper, Monitor); }
        catch (Exception ex) { Startup.Check(false); Monitor.Log($"BSV tea initialization failed. Existing BSV modules remain registered.\n{ex}", LogLevel.Error); }
        try { _ = new GoldenAppleFeature(helper, Monitor); }
        catch (Exception ex) { Startup.Check(false); Monitor.Log($"BSV Golden Apple initialization failed. Existing BSV modules remain registered.\n{ex}", LogLevel.Error); }
        IntegrationHost.Register(helper, Monitor, Config);
        Monitor.Log(Tr("hotfix.horse", new { enabled=Config.EnableHorseSpeedBonus, percent=BsvI18n.Number(helper,Config.HorseSpeedBonus*100,"0.##") }), LogLevel.Debug);
        try { _ = new Totems.TotemService(helper, Monitor, ModManifest); _ = new Totems.TotemUnlockService(helper, Monitor, ModManifest); Monitor.Log(Tr("totem.loaded"), LogLevel.Debug); }
        catch (Exception ex) { Startup.Check(false); Monitor.Log(Tr("totem.error") + "\n" + ex, LogLevel.Error); }
        try { _ = new Orders.OrdersService(helper, Monitor, ModManifest, () => Config.OrderItemExclusions); Monitor.Log(Tr("orders.loaded"), LogLevel.Debug); }
        catch (Exception ex) { Startup.Check(false); Monitor.Log(Tr("orders.error") + "\n" + ex, LogLevel.Error); }
        try { _ = new MultiplayerRescue.RescueService(helper, Monitor, ModManifest); }
        catch (Exception ex) { Startup.Check(false); Monitor.Log("BSV Multiplayer Rescue initialization failed; native death remains active.\n" + ex, LogLevel.Error); }
        try { _ = new AfkFishing.AfkFishingService(helper, Monitor, ModManifest, Config); }
        catch (Exception ex) { Startup.Check(false); Monitor.Log(Tr("afk.error") + "\n" + ex, LogLevel.Error); }
        try { _ = new SpiritFish.SpiritService(helper, Monitor, ModManifest.UniqueID); }
        catch (Exception ex) { Startup.Check(false); Monitor.Log("Spirit Fish initialization failed.\n" + ex, LogLevel.Error); }
        try { _ = new GiantLobster.LobsterService(helper, Monitor, ModManifest.UniqueID); }
        catch (Exception ex) { Startup.Check(false); Monitor.Log("Giant Lobster initialization failed.\n" + ex, LogLevel.Error); }
        try { _ = new LobsterKing.KingService(helper, Monitor, ModManifest.UniqueID); }
        catch (Exception ex) { Startup.Check(false); Monitor.Log("Lobster King initialization failed.\n" + ex, LogLevel.Error); }
        try { _ = new KingCrab.KingCrabService(helper, Monitor, ModManifest.UniqueID); }
        catch (Exception ex) { Startup.Check(false); Monitor.Log("King Crab initialization failed: " + ex, LogLevel.Error); }
        try { _ = new MutantLobsterKing.MutantService(helper, Monitor, ModManifest.UniqueID); }
        catch (Exception ex) { Startup.Check(false); Monitor.Log("Mutant Lobster King initialization failed: " + ex, LogLevel.Error); }
        _ = new Professions.ProfessionService(helper, Monitor, ModManifest.UniqueID, Startup.Check);
        CompatibilityDiagnostics.Register(helper, Monitor, Config);
        Configuration.ConfigMenu.Register(helper, Monitor, ModManifest, Config);
        // Runs after the existing GameLaunched initializers. Optional adapters retain their
        // own warnings/errors; absence or adapter failure does not fail core BSV startup.
        helper.Events.GameLoop.GameLaunched += (_, _) =>
        {
            Startup.Check(machineStatus is "diag.machine.loaded" or "diag.machine.standalone");
            Startup.Complete(message => Monitor.Log(message, LogLevel.Info));
        };
    }

    private sealed class HarvestState
    {
        public string EventId = "";
        public HoeDirt Soil = null!;
        public string SeedId = "";
        public string HarvestId = "";
        public int Multiplier;
        public int MinimumQuality;
        public HarvestState? Parent;
        public List<Item> Crops = new();
        public int DaysBefore;
        public int RegrowDays;
        public bool FullyGrownBefore;
        public double Chance;
        public Farmer Farmer = null!;
        public GameLocation Location = null!;
        public JunimoHarvester? Junimo;
        public Vector2 Position;
        public bool DropOnGround;
    }

    // Observe state only. Never suppress or replace the game's harvest operation.
    private static void BeforeHarvest(Crop __instance, int xTile, int yTile,
        HoeDirt soil, JunimoHarvester? junimoHarvester, bool isForcedScytheHarvest,
        out HarvestState? __state)
    {
        __state = new HarvestState { Parent = ActiveHarvest };
        var parent = ActiveHarvest;
        ActiveHarvest = null;
        try
        {
            var config = Configuration.SharedPreferences.Effective(Instance.Config);
            if (!Context.IsWorldReady
                || (junimoHarvester is not null && !Context.IsMainPlayer))
                return;

            if (!HarvestRules.IsReady(__instance.dead.Value, __instance.forageCrop.Value,
                __instance.phaseDays.Count, __instance.currentPhase.Value,
                __instance.fullyGrown.Value, __instance.dayOfCurrentPhase.Value))
                return;

            // Seasonal wild seeds become forage objects before harvesting and are outside this hook.
            string seedId = __instance.netSeedIndex.Value;
            if (string.IsNullOrWhiteSpace(__instance.indexOfHarvest.Value))
                return;
            if(Context.IsMainPlayer)Farming.FertilizerService.EnsureCrop(__instance);
            var data = __instance.GetData();
            if (data is null)
                return;

            var location = soil?.Location ?? __instance.currentLocation ?? Game1.currentLocation;
            if (location is null || Game1.player is null)
                return;
            __state = new HarvestState
            {
                SeedId = seedId,
                Soil = soil!,
                EventId = (__instance.modData.GetValueOrDefault(Farming.FertilizerService.CropKey)??"")+"/"+(__instance.modData.GetValueOrDefault(Farming.FertilizerService.GenerationKey)??"0"),
                MinimumQuality = Totems.TotemStorage.Quality(__instance, soil, location),
                HarvestId = __instance.indexOfHarvest.Value.StartsWith("(") ? __instance.indexOfHarvest.Value : "(O)" + __instance.indexOfHarvest.Value,
                Multiplier = 1,
                Parent = parent,
                DaysBefore = __instance.dayOfCurrentPhase.Value,
                RegrowDays = data.RegrowDays,
                FullyGrownBefore = __instance.fullyGrown.Value,
                Chance = config.EnableHarvestSeeds ? config.SeedChance : 0,
                Farmer = Game1.player,
                Location = location,
                Junimo = junimoHarvester,
                Position = new Vector2(xTile * 64 + 32, yTile * 64 + 32),
                DropOnGround = isForcedScytheHarvest
                    || __instance.GetHarvestMethod() == StardewValley.GameData.Crops.HarvestMethod.Scythe
            };
            ActiveHarvest = __state;
        }
        catch (Exception ex)
        {
            ActiveHarvest = null;
            if (!Instance.loggedCaptureError)
            {
                Instance.loggedCaptureError = true;
                Instance.Monitor.Log($"BSV skipped a seed bonus because crop inspection failed.\n{ex}", LogLevel.Error);
            }
        }
    }

    private static void AfterHarvest(Crop __instance, bool __result,
        bool __runOriginal, HarvestState? __state)
    {
        try
        {
            if (__state is null || __state.Multiplier == 0 || !HarvestRules.WasHarvested(true, __runOriginal,
                __result, __state.RegrowDays, __state.FullyGrownBefore, __instance.fullyGrown.Value,
                __state.DaysBefore, __instance.dayOfCurrentPhase.Value))
                return;

            // Only a successful native reset is changed; current timers are never recomputed by config changes.
            if(__state.RegrowDays>0&&__instance.dayOfCurrentPhase.Value==__state.RegrowDays)
                __instance.dayOfCurrentPhase.Value=Farming.FertilizerRules.Regrow(__state.RegrowDays,
                    Farming.FertilizerRules.Speed(Farming.FertilizerService.Read(__state.Soil).Speed),
                    Configuration.SharedPreferences.Current.EnableBsvSpeedGro);
            Farming.FertilizerService.SubmitHarvest(__instance,__state.Soil,__state.Farmer,__state.Crops,__state.EventId,
                __state.SeedId,__state.Chance>0,__state.Chance,__state.RegrowDays,__state.Junimo,__state.Position,__state.DropOnGround);
        }
        catch (Exception ex)
        {
            if (!Instance.loggedRewardError)
            {
                Instance.loggedRewardError = true;
                Instance.Monitor.Log($"BSV could not deliver a seed bonus. The crop harvest was not cancelled.\n{ex}", LogLevel.Error);
            }
        }
    }
}

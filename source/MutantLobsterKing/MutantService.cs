using Eddie.FishingMachine.Core;
using Eddie.FishingMachine;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.GameData.Objects;
namespace BetterStardewValley.MutantLobsterKing;

internal sealed class MutantService
{
    internal static bool Ready {get;private set;}
    internal static bool NativeCaptureReady {get;private set;}
    private static string AutomateState="not checked until GameLaunched";
    private readonly IModHelper Helper;
    private readonly Dictionary<int,Texture2D> Textures=new();
    internal static bool IsSewer(GameLocation? location)=>location is Sewer&&location.Name=="Sewer";
    internal static bool Eligible(GameLocation location,Vector2 tile)=>Ready&&IsSewer(location)&&location.canFishHere()
        &&WorldService.Water(location,new((int)tile.X,(int)tile.Y));
    internal MutantService(IModHelper helper,IMonitor monitor,string modId)
    {
        Ready=false;Helper=helper;var h=new Harmony(modId+".MutantLobsterKing");
        try
        {
            var pot=DrawPatchTargets.Require(typeof(StardewValley.Objects.CrabPot),"DayUpdate",typeof(void));
            if(Harmony.GetPatchInfo(pot)?.Transpilers.Any(p=>p.PatchMethod.DeclaringType==typeof(GiantLobster.LobsterCrabPots))!=true)
                throw new InvalidOperationException("Shared crab pot selection patch unavailable; no partial Mutant capture enabled.");
            UsagePolicy.Install(h);PotCollection.Install(h);
        }catch{h.UnpatchAll(h.Id);throw;}
        helper.Events.Content.AssetRequested+=(_,e)=>
        {
            if(e.NameWithoutLocale.IsEquivalentTo(MutantData.Texture))e.LoadFrom(()=>Texture(16),StardewModdingAPI.Events.AssetLoadPriority.Exclusive);
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))e.Edit(a=>MutantData.Objects(a.AsDictionary<string,ObjectData>().Data,k=>BsvI18n.Get(helper,k)));
        };
        helper.Events.Content.AssetsInvalidated+=(_,e)=>
        {if(e.NamesWithoutLocale.Any(n=>n.IsEquivalentTo(MutantData.Texture))){foreach(var tex in Textures.Values)HdSprites.Unregister(tex);foreach(var tex in Textures.Values.Distinct())tex.Dispose();Textures.Clear();}};
        helper.Events.GameLoop.GameLaunched+=(_,_)=>
        {
            if(!helper.ModRegistry.IsLoaded("Pathoschild.Automate")){AutomateState="not loaded; native collection guard active";return;}
            // This optional guard protects the single collectible when external Automate
            // collects a native CrabPot. No BSV machine factory or transport is registered.
            NativeCaptureReady=false;var optional=new Harmony(modId+".MutantAutomateCollection");
            try
            {
                var type=AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name=="Automate")
                    .Select(a=>a.GetType("Pathoschild.Stardew.Automate.Framework.Machines.Objects.CrabPotMachine",false)).FirstOrDefault(t=>t!=null)
                    ??throw new TypeLoadException("Loaded Automate CrabPotMachine type unavailable.");
                PotCollection.InstallAutomate(optional,type);NativeCaptureReady=true;AutomateState="loaded; exact crab book guard installed";
            }
            catch(Exception ex)
            {optional.UnpatchAll(optional.Id);AutomateState="FAILED: native Mutant capture disabled to avoid doubled output";monitor.Log(AutomateState+"; BSV crab production remains independent. "+ex,LogLevel.Error);}
        };
        helper.ConsoleCommands.Add("bsv_mutant_status",BsvI18n.Get(helper,"cmd.mutant-status"),(_,_)=>
            monitor.Log($"Mutant Lobster King: ready={Ready}; nativeCapture={NativeCaptureReady}; Automate={AutomateState}; ID={MutantLobsterRules.Item}; Sewer water only; 0.05% per base opportunity; one per farm per game year, exactly one item per success; Basic/category 0, 50000g base, no food/crafting/processing/pond. Existing output and batches never rerolled. Use bsv_sewer_status at a candidate placement tile.",LogLevel.Info));
        helper.Events.GameLoop.SaveCreating+=(_,_)=>MutantWildQuota.Initialize(true,monitor);
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>{MutantWildQuota.Initialize(false,monitor);MutantWildQuota.Reconcile();};
        Ready=true;NativeCaptureReady=true;monitor.Log("Mutant Lobster King collection rules initialized.",LogLevel.Debug);
    }
    private Texture2D Texture(int size)
    {
        if(Textures.TryGetValue(size,out var tex))return tex;
        var source=Helper.ModContent.Load<Texture2D>("assets/mutant-lobster-king/collectible.png");
        if(size!=16)throw new ArgumentOutOfRangeException(nameof(size),"Native logical handle must be 16px.");
        tex=HdSprites.CreateSourceResolution(source,out var full);
        Textures[size]=tex;Textures[0]=full;return tex;
    }
}

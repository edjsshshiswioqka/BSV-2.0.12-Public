using StardewModdingAPI;
using StardewValley;
using Newtonsoft.Json;

namespace BetterStardewValley.Configuration;

internal static class SharedPreferences
{
    private const string Key="Eddie.BetterStardewValley/SharedBalance.v3";
    private static string? cachedRaw;
    private static ModConfig? cached;
    private static Snapshot? cachedPolicy;
    private static ModConfig? local;
    private static long revision;
    private static string? appliedLocationPolicy;
    private static (bool,bool)? appliedMaps; // null: the first host snapshot always reloads both maps on a farmhand
    private static bool nextProfessions=true,nextAnnual=true;
    private static readonly ModConfig Waiting=new(){EnableBsvSpeedGro=false,EnableHarvestSeeds=false,HarvestBonus=new(){Enabled=false},
        EnableHorseSpeedBonus=false,AutoRestartProduction=false,EnableAllProfessions=false,EnableAnnualLegendaryFish=false,EnableOrdersSystem=false,
        EnableGreenhouseExpansion=false,EnableCellarExpansion=false};
    internal sealed class Snapshot
    {
        [JsonProperty(Required=Required.Always)] public int Schema=3;
        [JsonProperty(Required=Required.Always)] public long Revision;
        [JsonProperty(Required=Required.Always)] public bool EnableHarvestSeeds;
        [JsonProperty(Required=Required.Always)] public double SeedChance;
        [JsonProperty(Required=Required.Always)] public HarvestBonusOptions HarvestBonus=new();
        [JsonProperty(Required=Required.Always)] public bool AutoRestartProduction,EnableAllProfessions,EnableAnnualLegendaryFish,EnableHorseSpeedBonus,EnableOrdersSystem;
        [JsonProperty(Required=Required.Always)] public double HorseSpeedBonus;
        [JsonProperty(Required=Required.Always)] public int FishCycleMinutes=2880,CrabCycleMinutes=2880;
        [JsonProperty(Required=Required.Always)] public int FishLevelTwoMinutes=4320,FishLevelThreeMinutes=5760,CrabLevelTwoMinutes=5760;
        [JsonProperty(Required=Required.Always)] public bool EnableBsvSpeedGro;
        [JsonProperty] public bool OnlyOwnersRide=true;
        // Map size is the host's decision: every player must load the same greenhouse and cellar map.
        [JsonProperty] public bool EnableGreenhouseExpansion=true,EnableCellarExpansion=true;
        [JsonProperty] public string[] AllProfessionsExcludedSkills=Array.Empty<string>();
    }
    internal static ModConfig Current=>local==null?Waiting:Effective(local);
    internal static bool HasLocal=>local!=null;
    internal static bool Ready=>!Context.IsWorldReady||Context.IsMainPlayer||Read()!=null;
    internal static int? CycleMinutes(bool crab,int level=1)
    {
        if(!Context.IsWorldReady||Context.IsMainPlayer)
            return crab?Eddie.FishingMachine.ModEntry.Instance?.CrabConfig.MinutesForLevel(level):Eddie.FishingMachine.ModEntry.Instance?.Config.MinutesForLevel(level);
        if(Read()==null)return null;
        return crab?(level==2?cachedPolicy!.CrabLevelTwoMinutes:cachedPolicy!.CrabCycleMinutes)
            :level==3?cachedPolicy!.FishLevelThreeMinutes:level==2?cachedPolicy!.FishLevelTwoMinutes:cachedPolicy!.FishCycleMinutes;
    }
    internal static void SetNextLoad(ModConfig saved)
    {nextProfessions=saved.EnableAllProfessions;nextAnnual=saved.EnableAnnualLegendaryFish;}
    internal static void Register(IModHelper helper,ModConfig config)
    {
        local=config;SetNextLoad(config);
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>
        {
            cachedRaw=null;cached=null;config.EnableAllProfessions=nextProfessions;config.EnableAnnualLegendaryFish=nextAnnual;
            appliedLocationPolicy=null;
            Publish(config);helper.GameContent.InvalidateCache("Data/Locations");
        };
        helper.Events.GameLoop.UpdateTicked+=(_,e)=>
        {
            if(!e.IsMultipleOf(60)||!Context.IsWorldReady||Context.IsMainPlayer||Read()==null)return;
            // Net fields can arrive after SaveLoaded; never retain a pre-policy fish asset.
            if(appliedLocationPolicy==cachedRaw)return;
            appliedLocationPolicy=cachedRaw;helper.GameContent.InvalidateCache("Data/Locations");
            // Reload the greenhouse and cellar maps when the host's map size decision arrives or changes.
            var maps=(cachedPolicy!.EnableGreenhouseExpansion,cachedPolicy.EnableCellarExpansion);
            if(appliedMaps!=maps)
            {
                if(appliedMaps?.Item1!=maps.Item1)helper.GameContent.InvalidateCache("Maps/Greenhouse");
                if(appliedMaps?.Item2!=maps.Item2)helper.GameContent.InvalidateCache("Maps/Cellar");
                appliedMaps=maps;
            }
        };
        helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>{cachedRaw=null;cached=null;appliedLocationPolicy=null;appliedMaps=null;};
        helper.Events.Multiplayer.PeerConnected+=(_,_)=>Publish(config);
    }
    internal static void Publish(ModConfig c)
    {
        if(!Context.IsWorldReady||!Context.IsMainPlayer)return;
        var snapshot=new Snapshot{Revision=++revision,EnableHarvestSeeds=c.EnableHarvestSeeds,SeedChance=c.SeedChance,
            HarvestBonus=ConfigPolicy.Copy(c.HarvestBonus),EnableBsvSpeedGro=c.EnableBsvSpeedGro,AutoRestartProduction=c.AutoRestartProduction,
            EnableAllProfessions=c.EnableAllProfessions,EnableAnnualLegendaryFish=c.EnableAnnualLegendaryFish,
            EnableHorseSpeedBonus=c.EnableHorseSpeedBonus,HorseSpeedBonus=c.HorseSpeedBonus,EnableOrdersSystem=c.EnableOrdersSystem,
            FishCycleMinutes=Eddie.FishingMachine.ModEntry.Instance?.Config.CycleGameMinutes??2880,
            CrabCycleMinutes=Eddie.FishingMachine.ModEntry.Instance?.CrabConfig.MinutesForLevel(1)??2880,
            FishLevelTwoMinutes=Eddie.FishingMachine.ModEntry.Instance?.Config.MinutesForLevel(2)??4320,
            FishLevelThreeMinutes=Eddie.FishingMachine.ModEntry.Instance?.Config.MinutesForLevel(3)??5760,
            CrabLevelTwoMinutes=Eddie.FishingMachine.ModEntry.Instance?.CrabConfig.MinutesForLevel(2)??5760,OnlyOwnersRide=c.OnlyOwnersRide,
            EnableGreenhouseExpansion=c.EnableGreenhouseExpansion,EnableCellarExpansion=c.EnableCellarExpansion,
            AllProfessionsExcludedSkills=c.AllProfessionsExcludedSkills??Array.Empty<string>()};
        Game1.MasterPlayer.modData[Key]=JsonConvert.SerializeObject(snapshot);
    }
    private static ModConfig? Read()
    {
        if(!Game1.MasterPlayer.modData.TryGetValue(Key,out var raw))return null;
        if(raw==cachedRaw)return cached;
        cachedRaw=raw;cached=null;cachedPolicy=null;
        try
        {
            var s=JsonConvert.DeserializeObject<Snapshot>(raw);
            if(s==null||s.Schema!=3||s.Revision<=0||s.HarvestBonus==null||!double.IsFinite(s.SeedChance)||s.SeedChance is <0 or >1
                ||!double.IsFinite(s.HorseSpeedBonus)||s.HorseSpeedBonus is <0 or >1
                ||new[]{s.FishCycleMinutes,s.CrabCycleMinutes,s.FishLevelTwoMinutes,s.FishLevelThreeMinutes,s.CrabLevelTwoMinutes}.Any(n=>n is <10 or >40320))return null;
            var h=s.HarvestBonus;
            if(new[]{h.Chances(0),h.Chances(1),h.Chances(2),h.Chances(3)}.Any(p=>!double.IsFinite(p.Double)||!double.IsFinite(p.Triple)||p.Double<0||p.Triple<0||p.Double+p.Triple>1))return null;
            cached=new ModConfig{EnableHarvestSeeds=s.EnableHarvestSeeds,SeedChance=s.SeedChance,HarvestBonus=h,EnableBsvSpeedGro=s.EnableBsvSpeedGro,
                AutoRestartProduction=s.AutoRestartProduction,EnableAllProfessions=s.EnableAllProfessions,
                EnableAnnualLegendaryFish=s.EnableAnnualLegendaryFish,EnableHorseSpeedBonus=s.EnableHorseSpeedBonus,
                HorseSpeedBonus=s.HorseSpeedBonus,EnableOrdersSystem=s.EnableOrdersSystem,OnlyOwnersRide=s.OnlyOwnersRide,
                EnableGreenhouseExpansion=s.EnableGreenhouseExpansion,EnableCellarExpansion=s.EnableCellarExpansion,
                AllProfessionsExcludedSkills=s.AllProfessionsExcludedSkills??Array.Empty<string>()};
            cachedPolicy=s;
        }
        catch(JsonException){return null;}
        return cached;
    }
    internal static ModConfig Effective(ModConfig localConfig)
        =>!Context.IsWorldReady||Context.IsMainPlayer?localConfig:Read()??Waiting;
}

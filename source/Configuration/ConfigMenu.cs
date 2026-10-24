using Eddie.FishingMachine.Core;
using StardewModdingAPI;
using MachineMod=Eddie.FishingMachine.ModEntry;
using CrabSettings=Eddie.FishingMachine.CrabSettings;

namespace BetterStardewValley.Configuration;

internal sealed class ConfigMenu
{
    private readonly IModHelper helper;private readonly IMonitor monitor;private readonly IManifest manifest;
    private readonly ModConfig live;
    private ModConfig draft,saved;
    private readonly BalanceConfig fish;
    private readonly CrabSettings crab;
    private static bool HostEditable=>!Context.IsWorldReady||Context.IsMainPlayer;
    internal static void Register(IModHelper h,IMonitor m,IManifest id,ModConfig c)
        =>h.Events.GameLoop.GameLaunched+=(_,_)=>new ConfigMenu(h,m,id,c).Install();
    private ConfigMenu(IModHelper h,IMonitor m,IManifest id,ModConfig c)
    {
        helper=h;monitor=m;manifest=id;live=c;draft=ConfigPolicy.Copy(c);saved=ConfigPolicy.Copy(c);
        fish=ConfigPolicy.Copy(MachineMod.Instance?.Config??new());
        crab=ConfigPolicy.Copy(MachineMod.Instance?.CrabConfig??new());
    }
    private string T(string key)=>BsvI18n.Get(helper,"config."+key);
    private string HostSummary()
    {
        if(!SharedPreferences.Ready)return T("waiting-host");
        var c=SharedPreferences.Current;
        string Flag(string name,bool value)=>T(name)+": "+T(value?"state-on":"state-off");
        return T("host-summary")+"\n"+string.Join("\n",new[]{
            Flag("seed-enabled",c.EnableHarvestSeeds)+" ("+BsvI18n.Number(helper,c.SeedChance,"P2")+")",
            Flag("speedgro-enabled",c.EnableBsvSpeedGro),Flag("yield-enabled",c.HarvestBonus.Enabled),Flag("fertilizer-enabled",c.HarvestBonus.EnableFertilizerYieldBonus),
            T("fish-days1")+": "+(SharedPreferences.CycleMinutes(false,1) is {} fish1?BsvI18n.Number(helper,fish1/1440d,"G17"):""),
            T("fish-days2")+": "+(SharedPreferences.CycleMinutes(false,2) is {} fish2?BsvI18n.Number(helper,fish2/1440d,"G17"):""),
            T("fish-days3")+": "+(SharedPreferences.CycleMinutes(false,3) is {} fish3?BsvI18n.Number(helper,fish3/1440d,"G17"):""),
            T("crab-days1")+": "+(SharedPreferences.CycleMinutes(true,1) is {} crab1?BsvI18n.Number(helper,crab1/1440d,"G17"):""),
            T("crab-days2")+": "+(SharedPreferences.CycleMinutes(true,2) is {} crab2?BsvI18n.Number(helper,crab2/1440d,"G17"):""),
            Flag("auto-restart",c.AutoRestartProduction),Flag("professions",c.EnableAllProfessions),
            Flag("annual-fish",c.EnableAnnualLegendaryFish),Flag("horse-enabled",c.EnableHorseSpeedBonus)+" ("+BsvI18n.Number(helper,c.HorseSpeedBonus,"P2")+")",
            Flag("orders-enabled",c.EnableOrdersSystem)});
    }
    private string YieldSummary()
    {
        var options=HostEditable?draft.HarvestBonus:SharedPreferences.Current.HarvestBonus;
        return T("exclusive")+"\n"+string.Join("\n",new[]{"none","basic","quality","deluxe"}.Select((tier,grade)=>
        {
            var p=options.Chances(options.EnableFertilizerYieldBonus?grade:0);
            if(!options.Enabled)p=(0,0);
            return T("yield-"+tier+"-double")+": "+BsvI18n.Number(helper,p.Double,"P2")+" · "+T("yield-"+tier+"-triple")+": "+BsvI18n.Number(helper,p.Triple,"P2");
        }));
    }
    private void Reset()
    {
        var defaults=new ModConfig();
        draft.AfkFishing.Enabled=defaults.AfkFishing.Enabled;
        draft.AfkFishing.ReturnStaminaThreshold=defaults.AfkFishing.ReturnStaminaThreshold;
        draft.AfkFishing.ReturnToFishingSpotNextDay=defaults.AfkFishing.ReturnToFishingSpotNextDay;
        draft.AfkFishing.EnableChestConnection=defaults.AfkFishing.EnableChestConnection;
        draft.CallHorseKey=defaults.CallHorseKey;draft.EnableLargeHorses=defaults.EnableLargeHorses;
        if(!HostEditable)return;
        draft.EnableHarvestSeeds=true;draft.SeedChance=.50;draft.HarvestBonus.Enabled=true;
        draft.EnableBsvSpeedGro=true;draft.HarvestBonus.EnableFertilizerYieldBonus=true;draft.AutoRestartProduction=true;
        draft.EnableAllProfessions=true;draft.EnableAnnualLegendaryFish=true;draft.EnableHorseSpeedBonus=true;
        draft.HorseSpeedBonus=.20;draft.EnableOrdersSystem=true;draft.EnableLargeHorses=true;draft.OnlyOwnersRide=true;
        draft.EnableGreenhouseExpansion=true;draft.EnableCellarExpansion=true;draft.EnableCellarAppearance=true;
        if(MachineMod.Instance?.Ready==true){fish.CycleGameMinutes=2880;fish.LevelTwoCycleGameMinutes=4320;fish.LevelThreeCycleGameMinutes=5760;fish.LevelDurationVersion=1;crab.CycleGameMinutes=2880;crab.LevelTwoCycleGameMinutes=5760;crab.LevelDurationVersion=1;}
    }
    private void Save()
    {
        try
        {
            if(IntegrationHost.Saving)throw new InvalidOperationException("Cannot change configuration during a game save.");
            var next=ConfigPolicy.Copy(saved);
            next.AfkFishing.Enabled=draft.AfkFishing.Enabled;
            next.AfkFishing.ReturnStaminaThreshold=draft.AfkFishing.ReturnStaminaThreshold;
            next.AfkFishing.ReturnToFishingSpotNextDay=draft.AfkFishing.ReturnToFishingSpotNextDay;
            next.AfkFishing.EnableChestConnection=draft.AfkFishing.EnableChestConnection;
            next.CallHorseKey=draft.CallHorseKey;
            next.EnableLargeHorses=draft.EnableLargeHorses; // visual only, drawn locally: a personal setting
            if(HostEditable)
            {
                next.EnableHarvestSeeds=draft.EnableHarvestSeeds;next.SeedChance=draft.SeedChance;
                next.EnableBsvSpeedGro=draft.EnableBsvSpeedGro;next.HarvestBonus.Enabled=draft.HarvestBonus.Enabled;
                next.HarvestBonus.EnableFertilizerYieldBonus=draft.HarvestBonus.EnableFertilizerYieldBonus;
                next.AutoRestartProduction=draft.AutoRestartProduction;next.EnableAllProfessions=draft.EnableAllProfessions;
                next.EnableAnnualLegendaryFish=draft.EnableAnnualLegendaryFish;
                next.EnableHorseSpeedBonus=draft.EnableHorseSpeedBonus;next.HorseSpeedBonus=draft.HorseSpeedBonus;
                next.EnableOrdersSystem=draft.EnableOrdersSystem;next.OnlyOwnersRide=draft.OnlyOwnersRide;
                next.EnableGreenhouseExpansion=draft.EnableGreenhouseExpansion;next.EnableCellarExpansion=draft.EnableCellarExpansion;
                next.EnableCellarAppearance=draft.EnableCellarAppearance;
            }
            if(!double.IsFinite(next.SeedChance)||next.SeedChance is <0 or >1
                ||!double.IsFinite(next.HorseSpeedBonus)||next.HorseSpeedBonus is <0 or >1
                ||!double.IsFinite(next.AfkFishing.ReturnStaminaThreshold)||next.AfkFishing.ReturnStaminaThreshold is <.01 or >.50)
                throw new InvalidDataException("Invalid configuration draft; nothing saved.");
            bool machines=HostEditable&&MachineMod.Instance?.Ready==true;
            if(machines){fish.Validate();crab.Validate();}
            var files=new Dictionary<string,object>{{"config.json",next}};
            if(machines){files.Add("fishing-machine.json",fish);files.Add("crab-catcher.json",crab);}
            ConfigFiles.Commit(helper.DirectoryPath,files);
            saved=ConfigPolicy.Copy(next);draft=ConfigPolicy.Copy(next);
            SharedPreferences.SetNextLoad(next);
            if(Context.IsWorldReady)
            {
                next.EnableAnnualLegendaryFish=live.EnableAnnualLegendaryFish;
                // Maps are built while a save loads: map switches apply from the next save load, never mid-session.
                PendingMaps=(next.EnableGreenhouseExpansion,next.EnableCellarExpansion,next.EnableCellarAppearance);
                next.EnableGreenhouseExpansion=live.EnableGreenhouseExpansion;next.EnableCellarExpansion=live.EnableCellarExpansion;
                next.EnableCellarAppearance=live.EnableCellarAppearance;
            }
            else PendingMaps=null;
            if(machines)
            {
                // Existing batches have captured DueAt/plan. Only the next Begin reads these.
                ConfigPolicy.Apply(fish,MachineMod.Instance!.Config);
                ConfigPolicy.Apply(crab,MachineMod.Instance.CrabConfig);
            }
            ConfigPolicy.Apply(next,live);SharedPreferences.Publish(live);helper.GameContent.InvalidateCache("Data/BigCraftables");
            monitor.Log(T("saved"),LogLevel.Debug);
        }
        catch(Exception ex){monitor.Log(T("save-error")+"\n"+ex,LogLevel.Error);}
    }
    private static (bool Greenhouse,bool Cellar,bool Appearance)? PendingMaps;
    private void Install()
    {
        helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>
        {
            if(PendingMaps is not {} maps)return;
            live.EnableGreenhouseExpansion=maps.Greenhouse;live.EnableCellarExpansion=maps.Cellar;live.EnableCellarAppearance=maps.Appearance;
            PendingMaps=null;
            helper.GameContent.InvalidateCache("Maps/Greenhouse");helper.GameContent.InvalidateCache("Maps/Cellar");
        };
        if(!helper.ModRegistry.IsLoaded("spacechase0.GenericModConfigMenu"))return;
        IGenericModConfigMenuApi? api=null;
        try
        {
            api=helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if(api==null)throw new InvalidOperationException("GMCM API unavailable.");
            api.Register(manifest,Reset,Save);
            void Flag(string id,Func<bool> get,Action<bool> set,bool personal=false)
                =>api.AddBoolOption(manifest,get,v=>{if(personal||HostEditable)set(v);},()=>T(id),()=>T(id+".help"),id);
            void Step(string id,Func<double> get,Action<double> set,int min,int max,int interval,bool personal=false,int units=100)
            {
                var initial=get()*units;
                bool standard=double.IsFinite(initial)&&initial>=min&&initial<=max&&Math.Abs(initial-Math.Round(initial))<1e-8&&Math.Round(initial)%interval==0;
                if(standard)
                    api.AddNumberOption(manifest,()=> (int)Math.Round(get()*units),v=>{if(personal||HostEditable)set(Math.Clamp((int)Math.Round(v/(double)interval)*interval,min,max)/(double)units);},
                        ()=>T(id),()=>T(id+".help"),min,max,interval,v=>units==100?v+"%":v+" "+T("days"),id);
                else
                {
                    string[] values=new[]{"keep"}.Concat(Enumerable.Range(0,(max-min)/interval+1).Select(i=>(min+i*interval).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
                    string Value(){double v=get()*units;string text=v.ToString(System.Globalization.CultureInfo.InvariantCulture);return values.Contains(text)?text:"keep";}
                    api.AddTextOption(manifest,Value,v=>{if(v!="keep"&&(personal||HostEditable)&&values.Contains(v))set(int.Parse(v,System.Globalization.CultureInfo.InvariantCulture)/(double)units);},
                        ()=>T(id),()=>T(id+".help")+" "+T("legacy"),values,
                        v=>v=="keep"?T("keep")+" ("+initial.ToString("G17",System.Globalization.CultureInfo.InvariantCulture)+(units==100?"%": " "+T("days"))+")":v+(units==100?"%":" "+T("days")),id);
                }
            }
            void Page(string id){api.AddPage(manifest,id,()=>T("page."+id));api.AddParagraph(manifest,()=>T("permissions"));}
            api.AddParagraph(manifest,()=>T("intro2"));
            api.AddParagraph(manifest,()=>!Context.IsWorldReady?T("title-preset"):Context.IsMainPlayer?T("host"):
                T("guest")+"\n"+HostSummary());
            foreach(string p in new[]{"farming","machines","afk","skills","maps","other"})api.AddPageLink(manifest,p,()=>T("page."+p));
            Page("farming");
            Flag("seed-enabled",()=>draft.EnableHarvestSeeds,v=>draft.EnableHarvestSeeds=v);
            Step("seed-chance",()=>draft.SeedChance,v=>draft.SeedChance=v,0,100,5);
            Flag("yield-enabled",()=>draft.HarvestBonus.Enabled,v=>draft.HarvestBonus.Enabled=v);
            Flag("fertilizer-enabled",()=>draft.HarvestBonus.EnableFertilizerYieldBonus,v=>draft.HarvestBonus.EnableFertilizerYieldBonus=v);
            Flag("speedgro-enabled",()=>draft.EnableBsvSpeedGro,v=>draft.EnableBsvSpeedGro=v);
            api.AddParagraph(manifest,YieldSummary);
            Page("machines");
            if(MachineMod.Instance?.Ready==true)
            {
                Step("fish-days1",()=>fish.MinutesForLevel(1)/1440d,v=>{fish.LevelDurationVersion=1;fish.CycleGameMinutes=checked((int)Math.Round(v*1440));},1,4,1,units:1);
                Step("fish-days2",()=>fish.MinutesForLevel(2)/1440d,v=>{fish.LevelDurationVersion=1;fish.LevelTwoCycleGameMinutes=checked((int)Math.Round(v*1440));},1,4,1,units:1);
                Step("fish-days3",()=>fish.MinutesForLevel(3)/1440d,v=>{fish.LevelDurationVersion=1;fish.LevelThreeCycleGameMinutes=checked((int)Math.Round(v*1440));},1,4,1,units:1);
                Step("crab-days1",()=>crab.MinutesForLevel(1)/1440d,v=>{crab.LevelDurationVersion=1;crab.CycleGameMinutes=checked((int)Math.Round(v*1440));},1,4,1,units:1);
                Step("crab-days2",()=>crab.MinutesForLevel(2)/1440d,v=>{crab.LevelDurationVersion=1;crab.LevelTwoCycleGameMinutes=checked((int)Math.Round(v*1440));},1,4,1,units:1);
            }
            else api.AddParagraph(manifest,()=>T("machines-unavailable"));
            Flag("auto-restart",()=>draft.AutoRestartProduction,v=>draft.AutoRestartProduction=v);
            Page("afk");
            Flag("afk-enabled",()=>draft.AfkFishing.Enabled,v=>draft.AfkFishing.Enabled=v,true);
            Step("afk-threshold",()=>draft.AfkFishing.ReturnStaminaThreshold,v=>draft.AfkFishing.ReturnStaminaThreshold=v,5,50,5,true);
            Flag("afk-return",()=>draft.AfkFishing.ReturnToFishingSpotNextDay,v=>draft.AfkFishing.ReturnToFishingSpotNextDay=v,true);
            Flag("afk-chests",()=>draft.AfkFishing.EnableChestConnection,v=>draft.AfkFishing.EnableChestConnection=v,true);
            Page("skills");
            Flag("professions",()=>draft.EnableAllProfessions,v=>draft.EnableAllProfessions=v);
            Flag("annual-fish",()=>draft.EnableAnnualLegendaryFish,v=>draft.EnableAnnualLegendaryFish=v);
            Page("maps");
            api.AddParagraph(manifest,()=>T("maps-note"));
            Flag("greenhouse-expansion",()=>draft.EnableGreenhouseExpansion,v=>draft.EnableGreenhouseExpansion=v);
            Flag("cellar-expansion",()=>draft.EnableCellarExpansion,v=>draft.EnableCellarExpansion=v);
            Flag("cellar-appearance",()=>draft.EnableCellarAppearance,v=>draft.EnableCellarAppearance=v);
            Page("other");
            api.AddKeybind(manifest,()=>draft.CallHorseKey,v=>draft.CallHorseKey=v,()=>T("horse-key"),()=>T("horse-key.help"),"horse-key");
            Flag("horse-enabled",()=>draft.EnableHorseSpeedBonus,v=>draft.EnableHorseSpeedBonus=v);
            Step("horse-speed",()=>draft.HorseSpeedBonus,v=>draft.HorseSpeedBonus=v,0,50,5);
            Flag("horse-large",()=>draft.EnableLargeHorses,v=>draft.EnableLargeHorses=v,true);
            Flag("horse-owner-only",()=>draft.OnlyOwnersRide,v=>draft.OnlyOwnersRide=v);
            Flag("orders-enabled",()=>draft.EnableOrdersSystem,v=>draft.EnableOrdersSystem=v);
        }
        catch(Exception ex)
        {
            try{api?.Unregister(manifest);}catch{}
            monitor.Log(T("registration-error")+"\n"+ex,LogLevel.Warn);
        }
    }
}

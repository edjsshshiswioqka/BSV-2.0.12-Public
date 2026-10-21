using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using BetterStardewValley;
using BetterStardewValley.SpiritFish;
using Eddie.FishingMachine.Core;
using Eddie.FishingMachine;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Objects;
using StardewValley.GameData.FishPonds;
using StardewValley.GameData.Locations;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Objects;
using SObject=StardewValley.Object;
class Program
{
    static readonly List<string> Checks=new();
    static void Check(bool x,string message){if(!x)throw new Exception(message);Checks.Add(message);}
    static readonly BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Farmer Host=null!;
    static bool Skip()=>false;
    static bool MailText(string path,ref string __result)
    {if(path!="Strings\\UI:LearnedRecipe_cooking")return true;__result="Cooking recipe learned (headless text fixture)";return false;}
    static bool Yes(ref bool __result){__result=true;return false;}
    static bool No(ref bool __result){__result=false;return false;}
    static bool Master(ref Farmer __result){__result=Host;return false;}
    static bool GetPlayer(long id,ref Farmer __result){__result=Host;return false;}
    static bool Farmers(ref IEnumerable<Farmer> __result){__result=new[]{Host};return false;}
    static bool Date(ref WorldDate __result){__result=new WorldDate(Game1.year,Game1.season.ToString().ToLowerInvariant(),Math.Max(1,Game1.dayOfMonth));return false;}
    sealed class Services:IServiceProvider{public object? GetService(Type t)=>null;}
    static Dictionary<string,string> FishRows=new(){["163"]="Legend/110/mixed/49/51/600 2000/spring/rainy/0/0/.1/.1/10"};
    static List<FishPondData> Ponds=new(){new(){Id="fixture-legendary",RequiredTags=new(){"fish_legendary"},MaxPopulation=1,SpawnTime=5,ProducedItems=new(){new(){ItemId="(O)812",Chance=1,RequiredPopulation=1}},BaseMinProduceChance=.15f,BaseMaxProduceChance=.95f}};
    sealed class Content:LocalizedContentManager
    {
        internal Content():base(new Services(),"."){}
        public override T Load<T>(string name)=>(T)(object)(name.Replace('\\','/') switch {
            "Data/CookingRecipes"=>CraftingRecipe.cookingRecipes,"Strings/UI"=>new Dictionary<string,string>(),
            "Data/Buffs"=>new Dictionary<string,StardewValley.GameData.Buffs.BuffData>(),"Data/Objects"=>Game1.objectData,"Data/Machines"=>new Dictionary<string,StardewValley.GameData.Machines.MachineData>(),"Data/ObjectContextTags" or "Strings/StringsFromCSFiles" or "Data/Furniture"=>new Dictionary<string,string>(),
            "Data/Fish"=>FishRows,"Data/FishPondData"=>Ponds,"Data/Locations"=>new Dictionary<string,LocationData>(),
            _=>throw new Exception("Missing content fixture "+name)});
    }
    static void Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--export-art")
        {
            foreach(string path in Directory.GetFiles(args[1],"*.rgba"))
            {
                using var reader=new BinaryReader(File.OpenRead(path));int width=reader.ReadInt32(),height=reader.ReadInt32();
                var pixels=new Color[width*height];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(reader.ReadByte(),reader.ReadByte(),reader.ReadByte(),reader.ReadByte());
                foreach(int size in new[]{16,64})
                {
                    var result=SpiritArt.Import(pixels,width,height,size);
                    Check(result.Any(c=>c.A>=200)&&result.Any(c=>c.A==0),"nonempty transparent production sprite: "+Path.GetFileName(path)+"/"+size);
                    Check(Enumerable.Range(0,size).All(n=>result[n].A==0&&result[(size-1)*size+n].A==0&&result[n*size].A==0&&result[n*size+size-1].A==0),"production sprite edge padding prevents cropping: "+Path.GetFileName(path)+"/"+size);
                    using var writer=new BinaryWriter(File.Create(path+"."+size));
                    foreach(var c in result){writer.Write(c.R);writer.Write(c.G);writer.Write(c.B);writer.Write(c.A);}
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=Checks,renderer="actual SpiritArt.Import; no GPU",live_game=false}));return;
        }
        Console.Error.WriteLine("STAGE service");
        var helper=(IModHelper)Recorder.Make(typeof(IModHelper));var monitor=(IMonitor)Recorder.Make(typeof(IMonitor));
        _=new SpiritService(helper,monitor,"BSV.Spirit.Tests");
        _=new BetterStardewValley.GiantLobster.LobsterService(helper,monitor,"BSV.Lobster.Tests");
        // Match ModEntry: register all food patches before any consumption method is executed/JIT-inlined.
        _=new BetterStardewValley.KingCrab.KingCrabService(helper,monitor,"BSV.KingCrab.Tests");
        _=new BetterStardewValley.LobsterKing.KingService(helper,monitor,"BSV.King.Tests");
        _=new BetterStardewValley.MutantLobsterKing.MutantService(helper,monitor,"BSV.Mutant.Tests");
        // Earlier species-specific fixtures intentionally exclude the subsequently introduced crab selector.
        KingCrabChecks.SetCaptureEnabledForFixture(false);
        var patched=Harmony.GetAllPatchedMethods().Where(m=>Harmony.GetPatchInfo(m)!.Owners.Contains("BSV.Spirit.Tests.SpiritFish")).ToArray();
        Check(SpiritLuckTargets.Sites.All(s=>patched.Any(m=>m.Module==typeof(Farmer).Module&&m.MetadataToken==s.Token)),"production Spirit constructor installs exact native targets including audited luck call sites");
        Check(Recorder.Events.Any(e=>e.Name=="add_AssetRequested")&&Recorder.Events.Any(e=>e.Name=="add_SaveLoaded"),"real constructor registers asset and saved-player lifecycle events");
        Game1.game1=(Game1)FormatterServices.GetUninitializedObject(typeof(Game1));Game1.game1._locations=new List<GameLocation>();Game1.otherFarmers=new();Game1.random=new Random(35);Game1.year=1;Game1.dayOfMonth=1;Game1.season=Season.Spring;Game1._locationLookup=new();Game1.locationData=new Dictionary<string,LocationData>{["Default"]=new(),["Mountain"]=new(),["LobsterFixtureOcean"]=new()};
        var h=new Harmony("BSV.Spirit.Headless");
        void Patch(Type t,string name,string callback,params Type[] types)=>h.Patch(t.GetMethod(name,Any,null,types,null)!,prefix:new(typeof(Program),callback));
        Patch(typeof(LocalizedContentManager),"LoadString",nameof(MailText),typeof(string));
        Patch(typeof(Building),"ReloadBuildingData",nameof(Skip),typeof(bool),typeof(bool));
        Patch(typeof(Farmer),"farmerInit",nameof(Skip));Patch(typeof(AnimatedSprite),"LoadTexture",nameof(Skip),typeof(string),typeof(bool));
        Patch(typeof(Context),"get_IsWorldReady",nameof(Yes));Patch(typeof(Context),"get_IsMainPlayer",nameof(Yes));Patch(typeof(Game1),"get_IsMasterGame",nameof(Yes));
        Patch(typeof(Game1),"get_MasterPlayer",nameof(Master));Patch(typeof(Game1),"GetPlayer",nameof(GetPlayer),typeof(long),typeof(bool));Patch(typeof(Game1),"getAllFarmers",nameof(Farmers));Patch(typeof(Game1),"get_Date",nameof(Date));
        Patch(typeof(StardewValley.BellsAndWhistles.PlayerStatusList),"AddSpriteDefinition",nameof(Skip),typeof(string),typeof(string),typeof(int),typeof(int),typeof(int),typeof(int));
        Game1.content=new Content();Game1.options=(Options)FormatterServices.GetUninitializedObject(typeof(Options));
        Game1.netWorldState=new Netcode.NetRoot<StardewValley.Network.NetWorldState>(new StardewValley.Network.NetWorldState());
        Host=new Farmer();Host.UniqueMultiplayerID=35;typeof(Game1).GetField("_player",Any)!.SetValue(null,Host);Host.buffs.SetOwner(Host);Host.buffs.Dirty=false;Host.fishingLevel.Value=10;
        Game1.objectData=new Dictionary<string,ObjectData>{["163"]=new(){Name="Legend",DisplayName="Legend",Type="Fish",Category=-4,ContextTags=new(){"fish_legendary"}},["812"]=new(){Name="Roe",DisplayName="Roe",Price=30,Type="Basic",Category=-23},["447"]=new(){Name="Aged Roe",DisplayName="Aged Roe",Type="Basic",Category=-26},["SmokedFish"]=new(){Name="Smoked Fish",DisplayName="Smoked Fish",Type="Basic",Category=-26}};
        SpiritData.Objects(Game1.objectData,k=>k);SpiritData.Fish(FishRows);
        ItemRegistry.AddTypeDefinition(new ObjectDataDefinition());SpiritData.Ponds(Ponds);
        if(args.Contains("--letter-only")){var result=DeveloperLetterChecks.Run(Host,helper,monitor,Check);Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=Checks,letter=result,live_game=false}));return;}
        if(args.Contains("--r15-native")){((StardewModdingAPI.Utilities.PerScreen<bool>)AccessTools.Field(typeof(Context),"IsWorldReadyForScreen").GetValue(null)).Value=true;_=new AnnualLegendaryFeature(helper,monitor,"BSV.R15.Annual",true);var result=FishingRareNativeR15.Run(Host,FishRows,monitor,Check);Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=Checks,result,live_game=false}));return;}
        if(args.Contains("--mutant-only")){var result=MutantChecks.Run(Host,FishRows,Check);Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=Checks,mutant=result,live_game=false}));return;}
        _=new AnnualLegendaryFeature(helper,monitor,"BSV.Spirit.Annual",false);
        if(args.Contains("--catchup-native")){CatchupNativeChecks.Run(Host,FishRows,helper,monitor,Check);Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=Checks,real_game_assembly=true,content="explicit ecology/map fixtures; host mutex ownership injected",live_game=false}));return;}
        if(args.Contains("--timeline-only")){TimelineNativeChecks.Run(Host,FishRows,Check);Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=Checks,real_game_assembly=true,content="explicit map/item/weather fixtures",live_game=false,multiplayer_session=false}));return;}
        Check(AnnualLegendaryFeature.MachineAvailable(Host,SpiritRules.Fish,1),"Spirit annual restriction remains active when original-five option is off");
        Check(AnnualLegendaryFeature.MachineAvailable(Host,"(O)163",1),"annual OFF permits the original fish's first lifetime catch, not yearly replenishment");
        AnnualLegendaryFeature.MarkMachineCatch(Host,"(O)163",1);
        Check(!AnnualLegendaryFeature.MachineAvailable(Host,"(O)163",2)
            &&!AnnualLegendaryFeature.MachineAvailable(Host,"(O)163",1,historical:true),"annual OFF blocks repeats including dated machine opportunities");
        Host.modData.Remove(AnnualLegendaryRules.KeyPrefix+"163");
        Check(FishRows[SpiritRules.Id].Split('/')[1..3].SequenceEqual(FishRows["163"].Split('/')[1..3]),"difficulty and movement cloned from supplied loaded Legend row (fixture, not claimed game data)");
        var rows=new Dictionary<string,LocationData>{["Mountain"]=new(){Fish=new(){new(){Id="foreign",ItemId="(O)128"}}}};SpiritData.Locations(rows);SpiritData.Locations(rows);
        Check(rows["Mountain"].Fish.Count==1&&rows["Mountain"].Fish.Any(x=>x.Id=="foreign"),"location edits idempotent and retain foreign entries");
        Check(!rows["Mountain"].Fish.Any(x=>x.Id==SpiritRules.Id)&&SpiritRules.Chance==.01,"no independently rolled Spirit location row; shared 1% rule");
        foreach(int level in new[]{0,9,10,15})foreach(int time in new[]{600,1750,1800,2500,2600})foreach(string map in new[]{"Mountain","Beach"})Check(SpiritRules.Eligible(level,time,map)==(level>=10&&time>=1800&&time<2600&&map=="Mountain"),$"eligibility boundary {level}/{time}/{map}");
        var def=new ObjectDataDefinition();
        foreach(int q in new[]{0,1,2,4})
        {
            var fish=ItemRegistry.Create<SObject>(SpiritRules.Fish,quality:q);
            int raw=q switch{0=>6000,1=>7500,2=>9000,_=>12000};
            Host.professions.Clear();Check(fish.sellToStorePrice(35)==raw,"native fish quality price "+q);
            var smoke=def.CreateFlavoredSmokedFish(fish);Check(smoke.Quality==q&&smoke.Price==12000&&smoke.preservedParentSheetIndex.Value==SpiritRules.Id,"native smoke identity/quality "+q);
            Check(smoke.sellToStorePrice(35)==2*raw,"native smoke quality price "+q);
            var roe=def.CreateFlavoredRoe(fish);Check(roe.Price==3030&&roe.Quality==0,"native roe unstarred 3030 from quality "+q);
            var aged=def.CreateFlavoredAgedRoe(roe);Check(aged.Price==6060&&aged.Quality==0,"native aged roe unstarred 6060 from quality "+q);
            foreach(int profession in new[]{6,8})
            {Host.professions.Clear();Host.professions.Add(6);if(profession==8)Host.professions.Add(8);double multiplier=profession==6?1.25:1.5;
                Check(fish.sellToStorePrice(35)==(int)(raw*multiplier),"native fish profession "+profession+" quality "+q);
                Check(smoke.sellToStorePrice(35)==(int)(2*raw*multiplier),"native smoke fishing profession "+profession+" quality "+q);
                Check(roe.sellToStorePrice(35)==3030,"roe not boosted by Fisher/Angler "+profession+"/"+q);
                Host.professions.Add(4);Check(Math.Abs(smoke.sellToStorePrice(35)-2*raw*multiplier*1.4)<2,"native smoke Artisan stacking "+profession+"/"+q);
                Check(Math.Abs(aged.sellToStorePrice(35)-6060*1.4)<2,"native aged roe Artisan "+profession+"/"+q);
            }
        }
        var soup=ItemRegistry.Create<SObject>(SpiritRules.Soup);Check(soup.healthRecoveredOnConsumption()==225&&soup.staminaRecoveredOnConsumption()==225,"real native consumption methods return exactly 225 HP and energy");
        var cooking=new Dictionary<string,string>();SpiritData.Cooking(cooking,k=>k);Check(cooking[SpiritRules.SoupId].StartsWith(SpiritRules.Id+" 1 430 1 257 1/"),"soup exact spirit/truffle/morel recipe");
        LuckChecks();
        var pond=new FishPond();pond.fishType.Value=SpiritRules.Id;pond.currentOccupants.Value=1;pond.maxOccupants.Value=10;
        pond.SpawnFish();Check(pond.FishCount==1,"real pond SpawnFish cannot breed Spirit even if another mod raises capacity");
        var a=pond.GetWaterColor(new Vector2(1,1));var b=pond.GetWaterColor(new Vector2(2,2));Check(a.HasValue&&b.HasValue&&a!=b,"native pond water gets spatial rainbow colors");pond.currentOccupants.Value=0;Check(pond.GetWaterColor(new Vector2(1,1))==null,"empty pond immediately restores native color");
        Check(SpiritArt.Kind(def.CreateFlavoredAgedRoe(ItemRegistry.Create<SObject>(SpiritRules.Fish)))=="aged_roe","native flavored aged roe routes to dedicated art");
        Check(CraftingRecipe.TryParseLevelRequirement(SpiritRules.SoupId,cooking[SpiritRules.SoupId],true,out var skill,out var recipeLevel,true)&&skill==1&&recipeLevel==10,"native level-up recipe parser recognizes Fishing 10");
        var foods=new BetterStardewValley.AfkFishing.AfkFoodRules(new Dictionary<string,BetterStardewValley.AfkFishing.AfkIngredient>{{SpiritRules.Id,new(SpiritRules.Id,-4,false)},{SpiritRules.SoupId,new(SpiritRules.SoupId,-7,false)}},new[]{SpiritRules.Id},cooking.Values);
        Check(!foods.Allows(SpiritRules.Id)&&!foods.Allows(SpiritRules.SoupId),"AFK never automatically eats Spirit Fish or its seafood soup");
        Check((bool)DrawPatchTargets.Require(typeof(FishPond),"isLegalFishForPonds",typeof(bool),typeof(string)).Invoke(new FishPond(),new object[]{SpiritRules.Id})!,"native pond data recognizes Spirit as a legal pond fish");
        Console.Error.WriteLine("STAGE pond");
        var pondRewards=PondRewardChecks.Run(Ponds,Check);
        Console.Error.WriteLine("STAGE lobster");
        var lobster=LobsterChecks.Run(Host,FishRows,Check);
        Console.Error.WriteLine("STAGE king");
        var king=KingChecks.Run(Host,FishRows,Check,helper,monitor,Ponds);
        Console.Error.WriteLine("STAGE quotas");
        Quotas();CrossYear();MachineRolls();
        Console.Error.WriteLine("STAGE king-crab");
        var kingCrab=KingCrabChecks.Run(Host,FishRows,Check,helper,monitor);
        Console.WriteLine(JsonSerializer.Serialize(new {passed=true,checks=Checks,pondRewards,lobster,king,kingCrab,real_game_assembly=true,content="explicit fixtures; not shipped Data XNB",live_game=false,multiplayer_session=false}));
    }
    sealed class FixedRandom:Random
    {
        readonly double sample;int serial;internal int Draws;
        internal FixedRandom(double sample){this.sample=sample;}
        public override double NextDouble(){Draws++;return sample;}
        public override int Next(int max){return serial++%max;}
    }
    static void LuckChecks()
    {
        Host.health=100;Host.passedOut=false;
        var beforeBase=Host.luckLevel.Value;
        foreach(double original in new[]{-.1,0,.1,.2})foreach(int level in new[]{0,2,13,26})foreach(bool charm in new[]{false,true})
        {
            Host.team.sharedDailyLuck.Value=original;Host.luckLevel.Value=level;Host.hasSpecialCharm=charm;
            Host.modData[SpiritRules.LuckKey]=Game1.Date.TotalDays.ToString();
            Check(SpiritLuck.EffectiveDaily(Host)==.125&&SpiritLuck.EffectiveLevel(Host)==13,"fixed effective luck "+original+"/"+level+"/"+charm+" actual="+Host.DailyLuck+"/"+Host.LuckLevel+" active="+SpiritLuck.Active(Host)+" world="+Context.IsWorldReady);
            Check(Host.luckLevel.Value==level&&Host.team.sharedDailyLuck.Value==original,"base fields unchanged "+original+"/"+level+"/"+charm);
            Check(SpiritLuck.SlotLevel(Host)==14&&SpiritLuck.EffectiveLevel(Host)==13,"slot helper never leaks level 14");
            Check(SpiritLuck.NormalLevel(Host)==Math.Max(0,level),"Calico native level bypass");
            var d=SpiritLuck.NormalDaily(Host);Check(Math.Abs(d-Math.Clamp(original+(charm?.02500000037252903:0),-.20000000298023224,.20000000298023224))<1e-10,"Calico native daily bypass");
            Check(SpiritLuck.EffectiveDaily(Host)==.125&&SpiritLuck.EffectiveLevel(Host)==13,"Calico bypass scope restored");
        }
        var visitor=new Farmer();visitor.health=100;visitor.buffs.SetOwner(visitor);visitor.buffs.Dirty=false;visitor.luckLevel.Value=2;
        Check(visitor.LuckLevel==2&&!SpiritLuck.Active(visitor),"unblessed farmer unaffected");
        Check(SpiritLuck.LadderDaily(Host,visitor)==SpiritLuck.EffectiveDaily(visitor),"unblessed miner does not inherit host soup in ladder branch");
        Check(SpiritLuck.LadderDaily(visitor,Host)==.125,"blessed miner owns ladder daily luck");
        visitor.UniqueMultiplayerID=36;Game1.otherFarmers.Add(36,visitor);
        var online=Game1.getOnlineFarmers().ToArray();
        Check(online.Contains(Host)&&online.Contains(visitor),"native online farmer set includes both headless fixtures");
        double expectedD=online.Average(f=>SpiritLuck.EffectiveDaily(f));double expectedL=online.Average(f=>(double)SpiritLuck.EffectiveLevel(f));
        Check(Math.Abs(Host.team.AverageDailyLuck()-expectedD)<1e-9&&Math.Abs(Host.team.AverageLuckLevel()-expectedL)<1e-9,"native team averages preserve participants and only replace blessed contributions");
        Game1.otherFarmers.Remove(36);
        Check(Host.GetSkillLevel(5)==13,"real native GetSkillLevel call site receives fixed luck despite getter inlining");
        Host.stats.Set(StardewValley.Constants.StatKeys.Mastery(1),0);
        Check(Math.Abs(SpiritLuck.TreasureThreshold(.01,Host)-.6275)<1e-8,"normal treasure virtual Magnet/Pirate/one tackle threshold");
        Host.stats.Set(StardewValley.Constants.StatKeys.Mastery(1),1);
        Check(Math.Abs(SpiritLuck.TreasureThreshold(.99,Host)-.6775)<1e-8,"mastery two-tackle threshold replaces, not adds to native equipment bonus");
        Check(SpiritLuck.TreasureThreshold(.23,visitor)==.23,"unblessed treasure threshold unchanged");
        Host.stats.Set(StardewValley.Constants.StatKeys.Mastery(1),0);
        Host.luckLevel.Value=4;Host.team.sharedDailyLuck.Value=-.05;Host.hasSpecialCharm=false;
        var slots=(StardewValley.Minigames.Slots)FormatterServices.GetUninitializedObject(typeof(StardewValley.Minigames.Slots));
        var target=DrawPatchTargets.Require(typeof(StardewValley.Minigames.Slots),"setSlotResults",typeof(void),typeof(List<float>));
        foreach(double boundary in new[]{.001,.0016,.0025,.005,.007,.01,.02,.12,.2,.4})
        {
            float[] pay={2500,1000,500,200,120,80,30,3,5,2,0};double[] cut={.001,.0016,.0025,.005,.007,.01,.02,.12,.2,.4};int k=Array.IndexOf(cut,boundary);
            foreach(int sign in new[]{-1,1})
            {
                var rng=new FixedRandom(boundary*2.37+sign*1e-10);Game1.random=rng;
                target.Invoke(slots,new object[]{new List<float>{0,0,0}});
                float payout=(float)typeof(StardewValley.Minigames.Slots).GetField("payoutModifier",Any)!.GetValue(slots)!;
                Check(payout==pay[k+(sign>0?1:0)]&&rng.Draws==1,"real slots cumulative boundary/one initial RNG draw "+boundary+"/"+sign);
            }
        }
        // Actual patched native slot generator, fixed seed. Predetermined six-sigma tolerance.
        Game1.random=new Random(136);var histogram=new Dictionary<float,int>();const int trials=20000;
        for(int trial=0;trial<trials;trial++)
        {
            target.Invoke(slots,new object[]{new List<float>{0,0,0}});
            float payout=(float)typeof(StardewValley.Minigames.Slots).GetField("payoutModifier",Any)!.GetValue(slots)!;
            histogram[payout]=histogram.GetValueOrDefault(payout)+1;
        }
        double[] cuts={0,.001,.0016,.0025,.005,.007,.01,.02,.12,.2,.4};float[] payouts={2500,1000,500,200,120,80,30,3,5,2,0};
        for(int bin=0;bin<payouts.Length;bin++)
        {
            double probability=bin==10?1-.4*2.37:(cuts[bin+1]-cuts[bin])*2.37;
            double expected=trials*probability,tolerance=6*Math.Sqrt(trials*probability*(1-probability))+1;
            Check(Math.Abs(histogram.GetValueOrDefault(payouts[bin])-expected)<=tolerance,"actual slots 20k seeded distribution, six-sigma predeclared tolerance: "+payouts[bin]);
        }
        Check(histogram.GetValueOrDefault(0)>0,"native slot losing outcomes remain possible");
        SpiritLuck.End(Host);Check(Host.LuckLevel==4&&Math.Abs(Host.DailyLuck+.05)<1e-9,"expiry restores CURRENT values without snapshot or equipment mutation");
        Host.modData[SpiritRules.LuckKey]=(Game1.Date.TotalDays-1).ToString();Check(!SpiritLuck.Active(Host),"stale saved day cannot grant next-day luck");
        Host.modData[SpiritRules.LuckKey]=Game1.Date.TotalDays.ToString();Host.health=0;Check(!SpiritLuck.Active(Host),"dead player inactive");Host.health=100;
        Check(Game1.objectData[SpiritRules.SoupId].Price==6000,"soup provisional base price 6000");
        bool mismatch=false;try{SpiritLuck.SlotsIL(Array.Empty<CodeInstruction>()).ToArray();}catch(InvalidOperationException){mismatch=true;}
        Check(mismatch,"slot IL mismatch fails closed");
        foreach(bool aged in new[]{false,true})
        {
            var colors=new HashSet<Color>();
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)
            {
                var c=new Color(220,220,220,(x+y)%4==0?0:255);var colored=SpiritRoeArt.Rainbow(c,x,y,16,16,aged);
                Check(c.A==colored.A,"roe mask alpha invariant "+aged+"/"+x+"/"+y);
                if(c.A==0)Check(c==colored,"fully transparent source pixel unchanged");else colors.Add(colored);
            }
            Check(colors.Count>8,"simultaneous spatial rainbow, not single-color animation");
        }
        Check(!SpiritRoeArt.Applies(ItemRegistry.Create<SObject>(SpiritRules.Fish)),"base Spirit Fish is never recolored");
        SpiritLuck.End(Host);Host.luckLevel.Value=beforeBase;Game1.random=new Random(35);
    }
    static void Quotas()
    {
        string token="manual:"+Guid.NewGuid().ToString("N");
        Check(MachineLegendaryBridge.ReserveManual(Host,1,token),"host grants first manual lease");
        Check(MachineLegendaryBridge.ReserveManual(Host,1,token),"manual request retry is idempotent");
        Check(!MachineLegendaryBridge.Available(Host,SpiritRules.Fish,1),"manual lease excludes machine start");
        Check(!MachineLegendaryBridge.ReserveManual(Host,1,"manual:other"),"second simultaneous lease rejected");
        MachineLegendaryBridge.ReleaseManual(Host,1,"manual:other",true);
        Check(MachineLegendaryBridge.Reserved(35,SpiritRules.Fish,1),"stale/wrong release cannot unlock quota");
        MachineLegendaryBridge.ReleaseManual(Host,1,token,false);
        Check(MachineLegendaryBridge.Available(Host,SpiritRules.Fish,1),"unsuccessful manual attempt returns allowance");
        Check(MachineLegendaryBridge.ReserveManual(Host,1,token),"allowance reacquired after failed catch");
        MachineLegendaryBridge.ReleaseManual(Host,1,token,true);
        string once=Host.modData[AnnualLegendaryRules.KeyPrefix+SpiritRules.Id];
        MachineLegendaryBridge.ReleaseManual(Host,1,token,true);
        Check(Host.modData[AnnualLegendaryRules.KeyPrefix+SpiritRules.Id]==once,"duplicate successful release does not settle twice");
        Check(!AnnualLegendaryFeature.MachineAvailable(Host,SpiritRules.Fish,1)&&AnnualLegendaryFeature.MachineAvailable(Host,SpiritRules.Fish,2),"same record blocks current year and opens next year");
        string saved=Host.modData[AnnualLegendaryRules.KeyPrefix+SpiritRules.Id];
        Check(AnnualLegendaryRules.ReadRecord(saved,0,1).Year==1,"serialized annual record round trip");
    }
    static void CrossYear()
    {
        string recordKey=AnnualLegendaryRules.KeyPrefix+SpiritRules.Id;
        var batch=new ProductionState{Phase="working",Level=1,DueAt=112*1440+360,
            Pending=new(){new(SpiritRules.Fish,3,2)},LegendaryPlans=new(){new(SpiritRules.Fish,3,2,"(O)128")}};
        MachineLegendaryBridge.Start(Host,batch,()=>{});
        Check(batch.LegendaryYear==2&&MachineLegendaryBridge.Reserved(35,SpiritRules.Fish,2),"machine persists completion-year reservation");
        Game1.year=3;
        string before=Host.modData[recordKey];
        bool injected=false;
        try{MachineLegendaryBridge.Finish(batch.Snapshot(),()=>throw new InvalidOperationException("injected inventory commit failure"));}
        catch(InvalidOperationException){injected=true;}
        Check(injected&&Host.modData[recordKey]==before&&MachineLegendaryBridge.Reserved(35,SpiritRules.Fish,2),"failed inventory commit restores annual record and reservation");
        MachineLegendaryBridge.Finish(batch,()=>{});
        Check(AnnualLegendaryRules.ReadRecord(Host.modData[recordKey],0,3).Year==3&&batch.Pending.Single().Id==SpiritRules.Fish,"delayed batch consumes actual successful-production year, not old reserved year");
        Check(!MachineLegendaryBridge.Reserved(35,SpiritRules.Fish,2),"completed delayed batch releases old reservation");
        var conflict=new ProductionState{Phase="working",Level=1,DueAt=3*112*1440+360,
            Pending=new(){new(SpiritRules.Fish,2,4)},LegendaryPlans=new(){new(SpiritRules.Fish,2,4,"(O)128")}};
        MachineLegendaryBridge.Start(Host,conflict,()=>{});Game1.year=5;
        string token="manual:"+Guid.NewGuid().ToString("N");
        Check(MachineLegendaryBridge.ReserveManual(Host,5,token),"new-year manual catch can reserve independently of a stale old-year batch");
        MachineLegendaryBridge.Finish(conflict,()=>{});
        Check(conflict.Pending.Single()==new StoredCatch("(O)128",2,4)&&MachineLegendaryBridge.Reserved(35,SpiritRules.Fish,5),"conflicting stale batch uses persisted ordinary fallback and preserves active manual lease");
        MachineLegendaryBridge.ReleaseManual(Host,5,token,false);Game1.year=1;
    }
    static void MachineRolls()
    {
        var config=new BalanceConfig();int hits=0;
        for(int level=1;level<=3;level++)for(int seed=0;seed<1000;seed++)
        {
            var old=new ProductionState{Level=level};int count=MachineUpgrade.RequiredBait(old,config);
            var state=Production.Begin(config,new[]{new BaitSpend(0,count,BaitKind.GiantLobster,null)},new[]{new FishChoice("(O)128",1),new FishChoice(Production.SpiritId,1,true)},new[]{"(O)168"},1000,seed,old);
            foreach(var plan in state.LegendaryPlans){hits++;Check(plan.Count<=3,"Spirit special bait/critical capped three seed "+seed);}
            Check(ProductionState.Load(state.Save()).Pending.SequenceEqual(state.Pending),"persisted machine batch roundtrip seed "+seed);
        }
        Check(hits>0,"seed sweep exercises actual Spirit machine roll branch");
    }
}

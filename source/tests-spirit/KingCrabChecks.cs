using System.Reflection;
using System.Runtime.Serialization;
using BetterStardewValley;
using BetterStardewValley.KingCrab;
using BetterStardewValley.GiantLobster;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Menus;
using SObject=StardewValley.Object;
internal static class KingCrabChecks
{
    internal static void SetCaptureEnabledForFixture(bool enabled)=>typeof(KingCrabService)
        .GetField("<Ready>k__BackingField",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,enabled);
    private sealed class Sequence:Random
    {private readonly Queue<double> Values;internal int Calls;internal Sequence(params double[] values){Values=new(values);}public override double NextDouble(){Calls++;return Values.Count>0?Values.Dequeue():.5;}}
    internal static object Run(Farmer player,Dictionary<string,string> rows,Action<bool,string> check,IModHelper helper,IMonitor monitor)
    {
        Console.Error.WriteLine("KING CRAB install");
        SetCaptureEnabledForFixture(true);
        Console.Error.WriteLine("KING CRAB data");
        KingCrabData.Objects(Game1.objectData,k=>k,KingCrabData.ApprovedBowl);KingCrabData.Fish(rows);KingCrabData.Fish(rows);
        var item=ItemRegistry.Create<SObject>(KingCrabRules.Fish);player.professions.Clear();
        check(item.Price==750&&item.sellToStorePrice(player.UniqueMultiplayerID)==750&&item.Category==-4,"native unmodified normal King Crab is fish and 750g");
        check(item.Edibility==-300&&item.healthRecoveredOnConsumption()==0&&Game1.objectData.ContainsKey(KingCrabRules.BowlId),"raw crab stays inedible and approved bowl is registered in release");
        var loaded=new Dictionary<string,string>(rows);loaded[KingCrabRules.Id]="King Crab/trap/1/1/ocean/2/20";
        check(!GiantLobsterRules.OrdinaryTrapData(loaded).ContainsKey(KingCrabRules.Id)&&!CrabRules.NativePool(loaded,new HashSet<string>{"ocean"}).Any(x=>KingCrabRules.IsFish(x.Id)),"both ordinary/Mariner pools reject King Crab even if another asset edit makes marker weight one");
        var location=new GameLocation();location.name.Value="LobsterFixtureOcean";
        foreach(double u in new[]{0d,Math.BitDecrement(.033),.033,Math.BitIncrement(.033),Math.BitDecrement(.048),.048,Math.BitIncrement(.048),Math.BitDecrement(1d)})
        {
            var random=new Sequence(u,.5,.5);var pot=new CrabPot{Location=location};
            LobsterCrabPots.SelectOrJunk(random,0,pot,2,1);
            check((pot.heldObject.Value?.QualifiedItemId==KingCrabRules.Fish)==(u>=.033&&u<.048),"actual native adapter boundary "+u.ToString("R"));
            if(u>=.033&&u<.048)check(random.Calls==1&&pot.heldObject.Value!.Stack==2&&pot.heldObject.Value.Quality==1,"mutual hit returns once with native quantity/quality");
        }
        var fallbackPot=new CrabPot{Location=location};LobsterCrabPots.SelectOrJunk(new Sequence(.003),0,fallbackPot,2,0);
        check(fallbackPot.heldObject.Value?.ItemId==GiantLobsterRules.Id,"shared draw selects Giant without a second rare roll");
        foreach(double invalid in new[]{double.NaN,-.01,1d}){bool rejected=false;try{KingCrabRules.Select(true,invalid);}catch(ArgumentOutOfRangeException){rejected=true;}check(rejected,"invalid random rejected "+invalid);}
        const int trials=100000,seed=142015;int selected=0;var rng=new Random(seed);
        for(int i=0;i<trials;i++)if(KingCrabRules.Select(true,rng.NextDouble()))selected++;
        CheckRate(selected,trials,check,"shared production rule");
        Console.Error.WriteLine("KING CRAB food");
        var food=Food(player,check);Console.Error.WriteLine("KING CRAB TV");Television(player,location,check);
        Console.Error.WriteLine("KING CRAB native-pots");
        var native=NativePot(player,location,check);Console.Error.WriteLine("KING CRAB machines");var machine=Machines(check);
        return new{trials,seed,selected,rate=(double)selected/trials,native,machine,food,live_game=false,production_bowl="ENABLED: 850g, base HP 150/energy 150",reruns="every year winter 18"};
    }
    private static void CheckRate(int count,int n,Action<bool,string> check,string label)=>check(Math.Abs(count-n*.015)<6*Math.Sqrt(n*.015*.985),label+": count="+count+", E="+n+", expected .015, prespecified 6 sigma");
    private static object NativePot(Farmer player,GameLocation location,Action<bool,string> check)
    {
        var h=new Harmony("BSV.KingCrab.SeedFixture");
        h.Patch(DrawPatchTargets.Require(typeof(CrabPot),"DayUpdate",typeof(void)),transpiler:new HarmonyMethod(typeof(LobsterChecks).GetMethod("InjectDayRandom",BindingFlags.Static|BindingFlags.NonPublic)!));
        var result=new List<object>();
        try
        {
            foreach(string profession in new[]{"none","mariner","luremaster","all"})foreach(string bait in new[]{"685","774","DeluxeBait"})
            {
                player.professions.Clear();if(profession=="mariner")player.professions.Add(10);if(profession=="luremaster")player.professions.Add(11);if(profession=="all")foreach(int id in Enumerable.Range(0,30))player.professions.Add(id);
                int hits=0,quantity=0;const int n=100000;
                for(int i=0;i<n;i++)
                {
                    var pot=new CrabPot{Location=location,TileLocation=new Vector2(i,21)};pot.owner.Value=player.UniqueMultiplayerID;
                    pot.bait.Value=ItemRegistry.Create<SObject>("(O)"+bait);pot.DayUpdate();
                    if(pot.heldObject.Value?.QualifiedItemId!=KingCrabRules.Fish)continue;
                    hits++;quantity+=pot.heldObject.Value.Stack;
                    if(pot.heldObject.Value.Quality!=(bait=="DeluxeBait"?1:0))throw new Exception("Native King Crab quality mismatch");
                }
                CheckRate(hits,n,check,"actual CrabPot.DayUpdate "+profession+"/"+bait);
                check(bait=="774"?quantity>hits:quantity==hits,"native quantity preserved "+profession+"/"+bait);
                result.Add(new{profession,bait,opportunities=n,selections=hits,itemQuantity=quantity,rate=(double)hits/n});
            }
            player.professions.Clear();var empty=new CrabPot{Location=location};empty.owner.Value=player.UniqueMultiplayerID;empty.DayUpdate();check(empty.heldObject.Value==null,"no bait/no exemption: no production opportunity");
            empty.bait.Value=ItemRegistry.Create<SObject>("(O)685");var stored=empty.heldObject.Value=ItemRegistry.Create<SObject>(KingCrabRules.Fish,2,2);empty.DayUpdate();check(ReferenceEquals(stored,empty.heldObject.Value)&&stored.Stack==2,"pending native output preserved through next DayUpdate");
            Game1.locationData["LobsterFixtureOcean"].FishAreas["Default"].CrabPotFishTypes=new(){"freshwater"};
            for(int i=0;i<1000;i++){var pot=new CrabPot{Location=location,TileLocation=new Vector2(i,21)};pot.owner.Value=player.UniqueMultiplayerID;pot.bait.Value=ItemRegistry.Create<SObject>("(O)685");pot.DayUpdate();if(pot.heldObject.Value?.QualifiedItemId==KingCrabRules.Fish)throw new Exception("Freshwater King Crab");}
            check(true,"1000 native freshwater calls exclude King Crab");
        }
        finally{h.UnpatchAll(h.Id);Game1.locationData["LobsterFixtureOcean"].FishAreas["Default"].CrabPotFishTypes=new(){"ocean"};player.professions.Clear();}
        return new{seedFactory="explicit existing fixture seed 139715+tileX+17*tileY+saveInput; missing native xxHash dependency",loadedContent="trap fixtures",results=result};
    }
    private static object Machines(Action<bool,string> check)
    {
        var result=new List<object>();var pool=new[]{new CrabAquatic("(O)715",1)};
        foreach(int level in new[]{1,2})foreach(bool lure in new[]{false,true})foreach(bool deluxe in new[]{false,true})
        {
            var config=new CrabBalanceConfig{LevelOneCriticalChance=0,LevelTwoCriticalChance=0};int cost=config.Required(level,lure),n=0,hits=0;
            for(int i=0;n<100000;i++)
            {
                var state=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},new[]{new CrabBait(0,deluxe?"(O)DeluxeBait":"(O)685",cost,deluxe)},pool,35,lure,false,1000,142015+i,config,true);
                n+=state.Crab!.Rolls;hits+=state.Pending.Where(p=>KingCrabRules.IsFish(p.Id)).Sum(p=>p.Count);
                if(!CrabRules.Valid(ProductionState.Load(state.Save())))throw new Exception("Invalid persisted King Crab batch");
            }
            CheckRate(hits,n,check,$"actual CrabRules.Begin L{level}/lure={lure}/deluxe={deluxe}");
            var defaults=new CrabBalanceConfig();var bait=new[]{new CrabBait(0,"(O)685",cost,deluxe)};
            var b=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},bait,pool,35,lure,true,1000,142015,defaults,true);
            var plain=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},bait,pool,35,lure,false,1000,142015,config,true);
            check(b.FishCount==plain.FishCount&&b.OtherCount==plain.OtherCount&&b.Crab!.MachineCrits>0&&b.Crab.BookDoubles>0&&CrabRules.Valid(b),"normal bonuses do not reroll selections "+level+"/"+lure+"/"+deluxe);
            check(b.Pending.Where(p=>KingCrabRules.IsFish(p.Id)).Sum(p=>p.Count)>=plain.Pending.Where(p=>KingCrabRules.IsFish(p.Id)).Sum(p=>p.Count),"King Crab participates in quantity bonuses");
            var criticalConfig=new CrabBalanceConfig{LevelOneCriticalChance=1,LevelTwoCriticalChance=1};
            var critical=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},bait,pool,35,lure,false,1000,142015,criticalConfig,true);
            check(critical.Pending.Where(p=>KingCrabRules.IsFish(p.Id)).Sum(p=>p.Count)==2*plain.Pending.Where(p=>KingCrabRules.IsFish(p.Id)).Sum(p=>p.Count),"controlled 100% machine critical test doubles selected King Crab exactly, without another species roll");
            bool bookWorked=false;
            for(int bookSeed=142015;bookSeed<142031&&!bookWorked;bookSeed++)
            {
                var noBook=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},bait,pool,35,lure,false,1000,bookSeed,config,true);
                var withBook=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},bait,pool,35,lure,true,1000,bookSeed,config,true);
                bookWorked=withBook.Pending.Where(p=>KingCrabRules.IsFish(p.Id)).Sum(p=>p.Count)>noBook.Pending.Where(p=>KingCrabRules.IsFish(p.Id)).Sum(p=>p.Count);
            }
            check(bookWorked,"bounded seed sweep exercises King Crab book doubling with production rules");
            var fresh=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},bait,pool,35,lure,false,1000,142015,config,false);
            check(fresh.Pending.All(p=>!KingCrabRules.IsFish(p.Id)),"freshwater machine excludes King Crab");
            result.Add(new{level,lure,deluxe,opportunities=n,selections=hits,rate=(double)hits/n,firstSeed=142015,statistics="crit=0/book=false to count selections; default bonuses tested separately"});
        }
        return result;
    }
    private static object Food(Farmer player,Action<bool,string> check)
    {
        Console.Error.WriteLine("FOOD entry");
        // Use exactly the production-approved values; no substituted test economics.
        var fixture=KingCrabData.ApprovedBowl;KingCrabData.Objects(Game1.objectData,k=>k,fixture);
        var recipes=new Dictionary<string,string>();KingCrabData.Cooking(recipes,k=>k,fixture);
        Console.Error.WriteLine("FOOD recipe");
        string row=recipes[KingCrabRules.BowlId];check(row.Split('/')[0]==KingCrabRules.Id+" 1 423 1 136 1"&&row.Split('/')[2]==KingCrabRules.BowlId+" 1","recipe exact King Crab/rice/largemouth bass, output one");
        check(!CraftingRecipe.TryParseLevelRequirement(KingCrabRules.BowlId,row,true,out _,out _,true),"native parser has no level unlock");
        var results=new List<object>();
        Game1.buffsDisplay=(BuffsDisplay)FormatterServices.GetUninitializedObject(typeof(BuffsDisplay));typeof(BuffsDisplay).GetField("updatedIDs")!.SetValue(Game1.buffsDisplay,new HashSet<string>());Game1.timeOfDay=800;Game1.gameTimeInterval=0;
        foreach(int q in new[]{0,2})
        {
            Console.Error.WriteLine("FOOD create/buffs quality="+q);
            var food=ItemRegistry.Create<SObject>(KingCrabRules.Bowl,quality:q);var buffs=food.GetFoodOrDrinkBuffs().ToArray();
            Console.Error.WriteLine("FOOD energy="+food.staminaRecoveredOnConsumption());
            Console.Error.WriteLine("FOOD health call");
            Console.Error.WriteLine("FOOD health="+food.healthRecoveredOnConsumption());
            check(food.Price==850&&food.sellToStorePrice(player.UniqueMultiplayerID)==(q==0?850:1275)&&food.staminaRecoveredOnConsumption()==(q==0?150:270)&&food.healthRecoveredOnConsumption()==(q==0?150:270),"actual patched native consumption: base 150/150; gold native quality 270/270, price 850");
            check(buffs.Length==1&&buffs[0].id=="food","native food slot, no separate stacking effect");var b=buffs[0];
            check(b.effects.Defense.Value==(q==0?8:9)&&b.effects.Immunity.Value==4,"native seasoning raises Defense only, not Immunity, q="+q);
            check(b.millisecondsDuration==(q==0?420000:630000),"native Duration 600 conversion and seasoning 1.5x, q="+q);
            Console.Error.WriteLine("FOOD apply");player.buffs.Apply(b);check(ReferenceEquals(player.buffs.AppliedBuffs["food"],b),"native BuffManager owns food");
            check(player.buffs.Defense==(q==0?8:9)&&player.buffs.Immunity==4,"native combined effects actually expose both defense and immunity");
            int day=Game1.Date.TotalDays;KingCrabFoodClock.Start(player,b,day,800);
            int before=b.millisecondsDuration;b.update(new GameTime(TimeSpan.Zero,TimeSpan.FromMilliseconds(100)));check(b.millisecondsDuration==before,"real-time buff tick cannot double-debit frozen world clock");
            check(!KingCrabFoodClock.Advance(b,day,1750),"still active just before 600 minutes");
            check(KingCrabFoodClock.Advance(b,day,1800)==(q==0),"600-minute boundary respects native seasoned 900-minute duration");
            if(q!=0)check(KingCrabFoodClock.Advance(b,day,2300),"seasoned expiration at 900 world minutes");
            player.buffs.Remove("food");check(!KingCrabFoodClock.Advance(b,day,2400),"removed food cannot be resurrected by clock");
            results.Add(new{quality=q,health=food.healthRecoveredOnConsumption(),energy=food.staminaRecoveredOnConsumption(),price=food.Price,defense=b.effects.Defense.Value,immunity=b.effects.Immunity.Value,worldMinutes=q==0?600:900});
        }
        var clock=new KingCrabClock(1,800,600);check(!clock.Advance(1,800)&&clock.Remaining==600,"pause does not debit clock");clock.Advance(1,900);clock.Advance(1,830);check(clock.Remaining==540,"rollback does not add duration");clock.Advance(1,900);check(clock.Remaining==510,"forward time after rollback still counts actual new elapsed time");
        var late=new KingCrabClock(1,2350,600);late.Advance(1,2510);check(late.Remaining==520,"HHMM midnight/25xx conversion is 80 minutes");check(late.Advance(2,600),"new day expires, no cross-night reapply");
        var partial=new KingCrabClock(1,800,600,true);partial.Advance(1,810);check(partial.Remaining==600&&!partial.Advance(1,1800)&&partial.Advance(1,1810),"partial first tick rounds up, explicitly bounded under ten minutes");
        var jump=new KingCrabClock(1,800,600,true);jump.Advance(1,1800);check(jump.Remaining==10,"legal large jump counted apart from first partial tick");
        check(ItemRegistry.Create<SObject>("(O)"+GiantLobsterRules.DinnerId).healthRecoveredOnConsumption()==72,"King Crab health patch leaves Lobster Dinner at 72 HP");
        check(ItemRegistry.Create<SObject>(BetterStardewValley.SpiritFish.SpiritRules.Soup).healthRecoveredOnConsumption()==225,"King Crab health patch leaves Spirit Soup at 225 HP");
        return results;
    }
    private static void Television(Farmer player,GameLocation location,Action<bool,string> check)
    {
        var responses=new[]{new Response("Weather","Weather"),new Response("Livin'","Livin'"),new Response("(Leave)","Leave")};
        foreach(int year in new[]{0,1,2,5})foreach(int season in Enumerable.Range(0,4))foreach(int day in new[]{17,18,19})
        {
            Game1.year=year;Game1.season=(Season)season;Game1.dayOfMonth=day;
            var entries=KingCrabTelevision.AddChannel(responses);
            check(entries.Count(x=>x.responseKey==KingCrabTelevision.Channel)==(year>=1&&season==3&&day==18?1:0),"actual channel builder date "+year+"/"+season+"/"+day);
            check(entries.Any(x=>x.responseKey=="Livin'")&&entries.Any(x=>x.responseKey=="Weather"),"native Thursday channels retained");
        }
        Game1.year=1;Game1.season=Season.Winter;Game1.dayOfMonth=18;
        check(KingCrabTelevision.AddChannel(responses).Any(r=>r.responseKey==KingCrabTelevision.Channel),"release channel enabled with author-approved values");
        var entries2=KingCrabTelevision.AppendChannel(KingCrabTelevision.AppendChannel(responses,true),true);check(entries2.Count(r=>r.responseKey==KingCrabTelevision.Channel)==1,"no duplicate channel");
        player.cookingRecipes.Remove(KingCrabRules.BowlId);player.currentLocation=location;var tv=new TV();
        KingCrabTelevision.BeginViewing(tv,player);check(!player.cookingRecipes.ContainsKey(KingCrabRules.BowlId),"opening/show intro doesn't learn");
        var method=DrawPatchTargets.Require(typeof(TV),"getWeeklyRecipe",typeof(string[]));var lesson=(string[])method.Invoke(tv,null)!;
        check(lesson.Length==2&&player.cookingRecipes.ContainsKey(KingCrabRules.BowlId),"actual native recipe teaching entry delegates to current viewer only");
        player.cookingRecipes[KingCrabRules.BowlId]=7;method.Invoke(tv,null);check(player.cookingRecipes[KingCrabRules.BowlId]==7,"repeat viewing preserves cooking count");
        var other=new Farmer();other.UniqueMultiplayerID=142;check(!other.cookingRecipes.ContainsKey(KingCrabRules.BowlId),"another farmer not implicitly taught");
        typeof(KingCrabTelevision).GetMethod("Off",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{tv});
        player.cookingRecipes.Remove(KingCrabRules.BowlId);Game1.year=2;
        KingCrabTelevision.BeginViewing(tv,player);check(!player.cookingRecipes.ContainsKey(KingCrabRules.BowlId),"rerun intro alone does not teach");
        method.Invoke(tv,null);check(player.cookingRecipes.ContainsKey(KingCrabRules.BowlId),"year two rerun actually teaches at the native lesson node");
        typeof(KingCrabTelevision).GetMethod("Off",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{tv});
        player.cookingRecipes.Remove(KingCrabRules.BowlId);Game1.year=1;Game1.season=Season.Spring;Game1.dayOfMonth=1;
    }
}

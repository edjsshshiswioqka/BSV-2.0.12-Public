using BetterStardewValley;
using BetterStardewValley.LobsterKing;
using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using StardewValley;
using StardewValley.Objects;
using StardewValley.GameData.Locations;
using System.Reflection;
using ModEntry=Eddie.FishingMachine.ModEntry;

internal static class TimelineNativeChecks
{
    internal static void Run(Farmer player,Dictionary<string,string> fish,Action<bool,string> check)
    {
        int oldYear=Game1.year,oldDay=Game1.dayOfMonth,oldTime=Game1.timeOfDay;var oldSeason=Game1.season;
        try
        {
            if(Game1.getLocationFromName("Farm") is not Farm){var farmFixture=new Farm();farmFixture.name.Value="Farm";Game1.game1._locations.Add(farmFixture);Game1._locationLookup.Clear();}
            Game1.year=1;Game1.season=Season.Winter;Game1.dayOfMonth=28;Game1.timeOfDay=600;player.fishingLevel.Value=10;
            Game1.locationContextData=new Dictionary<string,StardewValley.GameData.LocationContexts.LocationContextData>();
            var location=new GameLocation();location.name.Value="TimelineFixture";location.map=MutantChecks.Fixture().map;
            Game1.locationData[location.Name]=new(){Fish=new(){new(){Id="day",ItemId="(O)128",Chance=1},new(){Id="night",ItemId="(O)129",Chance=1}}};
            Game1.objectData["129"]=new(){Name="NightFixture",DisplayName="NightFixture",Type="Fish",Category=-4};
            Game1.objectData["128"]=new(){Name="DayFixture",DisplayName="DayFixture",Type="Fish",Category=-4};
            fish["128"]="Day/50/mixed/1/20/600 1200/winter/sunny/0/0/1/1/0";
            fish["129"]="Night/50/mixed/1/20/1800 2600/winter/rainy/0/0/1/1/0";
            var c=new Chest{Location=location,TileLocation=new(4,4)};c.modData[ModEntry.Tag+"Facing"]="Right";
            c.modData[ModEntry.Tag+"MachineId"]=Guid.NewGuid().ToString("N");
            var state=MachineTimeline.StartFish(new(),new(){BaitPerCycle=1},new[]{new BaitSpend(0,1,BaitKind.Normal,null)},player.UniqueMultiplayerID,ProductionService.Now,149);
            location.GetWeather().IsRaining=false;var dry=TimelineEnvironment.Capture(c,state);location.GetWeather().IsRaining=true;var wet=TimelineEnvironment.Capture(c,state);
            check(dry.Error==null&&wet.Error==null,"native capture uses real map/registry adapter with explicit fixture data");
            check(dry.Fish.Any(p=>p.Fish.Any(f=>f.Id=="(O)128"))&&dry.Fish.All(p=>p.Fish.All(f=>f.Id!="(O)129")),"actual FishPool capture honors sunny day-only native fish data");
            check(wet.Fish.Any(p=>p.From==1800&&p.Fish.Any(f=>f.Id=="(O)129"))&&wet.Fish.Where(p=>p.From<1800).All(p=>p.Fish.All(f=>f.Id!="(O)129")),"actual Capture sees 18:00 rain-night fish even when the real clock is 06:00");
            check(Game1.timeOfDay==600&&Game1.dayOfMonth==28,"temporal evaluation never changes global clock/date");
            foreach(int time in new[]{600,1759,1800,2400,2559,2600})foreach(string condition in new[]{"TIME 1800","TIME 1800 2600","TIME 600 -1","!TIME 1800 2600"})
            {
                Game1.timeOfDay=time;bool native=GameStateQuery.CheckConditions(condition,location,player);
                Game1.timeOfDay=600;check(MachineTimeConditions.Check(condition,time,location,player)==native,"explicit-time predicate agrees with actual 1.6.15 resolver "+condition+"/"+time);
            }
            // Persisted journal is farm-scoped, not an in-memory/static reference to yesterday's map.
            TimelineEnvironment.Reset();location.GetWeather().IsRaining=true;TimelineEnvironment.Start(c,state);
            var farm=Game1.getFarm();string journalKey=ModEntry.Tag+"EnvironmentTimeline/v1";
            var journal=System.Text.Json.JsonSerializer.Deserialize<MachineEnvironmentJournal>(farm.modData[journalKey])!;
            journal.Record(state.CycleToken,ProductionService.Now,wet,true);farm.modData[journalKey]=System.Text.Json.JsonSerializer.Serialize(journal);TimelineEnvironment.Reset();
            check(TimelineEnvironment.At(state.CycleToken,111L*1440+1500).Rain,"real farm modData journal restores sealed sleeping-night weather after cache reset");
            Game1.year=2;Game1.season=Season.Spring;Game1.dayOfMonth=1;
            player.modData.Clear();Game1.MasterPlayer.modData.Clear();
            ProductionState New()=>MachineTimeline.StartFish(new(){Level=3},new(){LevelThreeBaitPerCycle=1,Normal=new(){Fish=1},LevelThreeCriticalChance=0,LevelThreeTripleChance=0},new[]{new BaitSpend(0,1,BaitKind.GiantLobster,null)},player.UniqueMultiplayerID,111L*1440+1080,149);
            ProductionState Unit()
            {
                for(int seed=0;seed<10000;seed++)
                {var unit=Production.Begin(new(){LevelThreeBaitPerCycle=1,Normal=new(){Fish=1}},new[]{new BaitSpend(0,1,BaitKind.GiantLobster,null)},new[]{new FishChoice("(O)128",1),new FishChoice(Production.SpiritId,1,true)},new[]{"(O)168"},111L*1440+1080,seed,new(){Level=3},singleOpportunity:true);if(unit.LegendaryPlans.Count==1)return unit;}
                throw new Exception("No Spirit fixture seed found within bound.");
            }
            var selected=Unit();var next=New();MachineTimeline.Merge(next,selected);string? saved=null;
            check(MachineLegendaryBridge.AvailableTimeline(player,Production.SpiritId,1,true),"sealed eligible old-year opportunity remains usable after waking into year two");
            MachineLegendaryBridge.CommitTimeline(player,111,selected,next,()=>saved=next.Save());
            check(saved!=null&&!MachineLegendaryBridge.AvailableTimeline(player,Production.SpiritId,1,true)&&MachineLegendaryBridge.AvailableTimeline(player,Production.SpiritId,2,true),"actual dated ledger consumes old year only; new year stays available");
            check(ProductionState.Load(saved!).Timeline!.Claims.Single().Day==111,"dated opportunity attribution persists with generated catch");
            bool duplicate=false;try{MachineLegendaryBridge.CommitTimeline(player,111,selected,New(),()=>throw new Exception("duplicate commit"));}catch(InvalidOperationException){duplicate=true;}
            check(duplicate,"second machine cannot settle the same species/owner/old year");
            var second=New();MachineTimeline.Merge(second,selected);string before=player.modData[AnnualLegendaryRules.KeyPrefix+"Eddie.BetterStardewValley_SpiritFish"];
            bool rolled=false;try{MachineLegendaryBridge.CommitTimeline(player,112,selected,second,()=>throw new InvalidOperationException("injected state failure"));}catch(InvalidOperationException){rolled=true;}
            check(rolled&&MachineLegendaryBridge.AvailableTimeline(player,Production.SpiritId,2,true)&&player.modData[AnnualLegendaryRules.KeyPrefix+"Eddie.BetterStardewValley_SpiritFish"]==before,"actual ledger and annual record rollback together on failed output commit");
            second=New();MachineTimeline.Merge(second,selected);MachineLegendaryBridge.CommitTimeline(player,112,selected,second,()=>{});
            check(!MachineLegendaryBridge.AvailableTimeline(player,Production.SpiritId,2,true),"new-year opportunity consumes its own independent annual quota once");
            // Actual farm-scoped seasonal transaction, including a catch settled after the season changes.
            farm.modData.Remove(KingWildQuota.Key);Game1.season=Season.Summer;
            int spring=112+27,summer=112+28;string? claim=null;
            check(KingWildQuota.AvailableAt(spring)&&KingWildQuota.CommitAt(spring,"machine:timeline-fixture",t=>claim=t,()=>claim=null),"actual past-spring opportunity commits after summer begins");
            check(claim!=null&&!KingWildQuota.AvailableAt(spring)&&KingWildQuota.AvailableAt(summer),"seasonal claim does not consume the completion season");
            check(!KingWildQuota.AvailableAt(111)&&!KingWildQuota.AvailableAt(summer+1),"winter opportunity and unobserved future opportunity cannot claim King");
        }
        finally
        {TimelineEnvironment.Reset();Game1.year=oldYear;Game1.season=oldSeason;Game1.dayOfMonth=oldDay;Game1.timeOfDay=oldTime;}
    }
}

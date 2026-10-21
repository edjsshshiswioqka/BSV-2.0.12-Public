using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Objects;

internal static class TimelineAdapterChecks
{
    internal static void Run(Action<bool,string> check)
    {
        int oldDay=Game1.Date.TotalDays,oldTime=Game1.timeOfDay,oldYear=Game1.year;
        try
        {
            Game1.Date.TotalDays=111;Game1.year=1;Game1.timeOfDay=600;
            var mod=new ModEntry{Config=new(){BaitPerCycle=200,Normal=new(){Fish=1},FishCriticalChance=0}};
            var service=new ProductionService(mod);var c=new Chest();c.Location.objects[c.TileLocation]=c;mod.Machines.All[c]=c.Location;
            c.modData[ModEntry.Tag+"MachineId"]=Guid.NewGuid().ToString("N");c.Items.Add(ItemRegistry.Create("(O)685",200));service.Tick();
            var original=service.Read(c).Snapshot();check(original.Timeline?.Plan.Count==200&&c.Items.Count==0,"runtime atomically pays once and schedules every bait");
            var journal=new MachineEnvironmentJournal();
            MachineEnvironmentProfile Profile(int day,bool rain)=>new(){Location=c.Location.NameOrUniqueName,X=(int)c.TileLocation.X,Y=(int)c.TileLocation.Y,Day=day,Rain=rain,
                Fish=new(){new(600,1800,new(){new(rain?"(O)129":"(O)128",1)}),new(1800,2600,new(){new(rain?"(O)131":"(O)130",1)})},Other=new(){"(O)168"}};
            journal.Record(original.CycleToken,111L*1440+360,Profile(111,false),false);
            journal.Record(original.CycleToken,111L*1440+720,Profile(111,true),false);
            journal.Record(original.CycleToken,111L*1440+800,Profile(111,true),true);
            TimelineEnvironment.ReadOverride=journal.At;
            Game1.Date.TotalDays=112;Game1.year=2;Game1.timeOfDay=600;
            journal.Record(original.CycleToken,112L*1440+360,Profile(112,false),false);
            service.Tick();check(service.Read(c).Timeline!.Cursor>0&&service.Read(c).Timeline!.Cursor<=128&&service.Read(c).CompletedCycles==0&&c.Items.Count==0,"wake-up bounded scheduler advances a positive number of opportunities without early delivery");
            var partial=service.Read(c).Save();service.Reset();check(service.Read(c).Save()==partial,"partial state restored without reroll or fuel spend");
            SimulationPump.Drain(service);
            check(service.Read(c).Timeline!.Cursor==original.Timeline!.Plan.Count(p=>p.At<ProductionService.Now),"wake-up resolves exactly past-day legal opportunities, not tomorrow's pool");
            journal.Record(original.CycleToken,112L*1440+800,Profile(112,false),true);
            Game1.Date.TotalDays=113;
            SimulationPump.Drain(service);
            var done=service.Read(c);check(done.CompletedCycles==1&&done.Phase=="idle"&&c.modData.Writes.Where(p=>p.Key==ProductionService.StateKey).Select(p=>ProductionState.Load(p.Value)).Last(w=>w.Phase=="ready").Timeline!.Cursor==200&&c.Items.Where(p=>p.QualifiedItemId is "(O)128" or "(O)129" or "(O)130" or "(O)131").Sum(p=>p.Stack)==200,"sleep plus cross-year completion delivers every earned fish once");
            foreach(string id in new[]{"(O)128","(O)129","(O)130","(O)131"})
            {
                int expected=original.Timeline.Plan.Count(p=>{
                    var env=journal.At(original.CycleToken,p.At);int clock=MachineTimeline.Time(p.At);return env.Fish.Single(f=>clock>=f.From&&clock<f.Until).Fish.Single().Id==id;
                });
                check(c.Items.Where(i=>i.QualifiedItemId==id).Sum(i=>i.Stack)==expected,"real service output matches observed time/weather for "+id);
            }
            string final=done.Save();service.Reset();service.Tick();check(service.Read(c).Save()==final&&c.Items.Where(i=>i.QualifiedItemId is "(O)128" or "(O)129" or "(O)130" or "(O)131").Sum(i=>i.Stack)==200,"completion reload does not duplicate output");
            // A gap must retain the exact paid opportunity; repair of the observation boundary resumes it once.
            c.Items.Clear();service.Tick();c.Items.Add(ItemRegistry.Create("(O)685",200));TimelineEnvironment.ReadOverride=null;service.Tick();
            var paid=service.Read(c).Snapshot();Game1.Date.TotalDays+=2;
            TimelineEnvironment.ReadOverride=(_,_)=>throw new InvalidOperationException("injected absent weather history");service.Tick();
            var paused=service.Read(c);check(paused.Timeline!.Cursor==0&&paused.Timeline.PausedReason!=null&&paused.Pending.Count==0&&c.Items.Count==0,"missing historical data pauses without discarding paid fuel or inventing fish");
            TimelineEnvironment.ReadOverride=(_,at)=>Profile(MachineTimeline.Day(at),false);
            c.modData.FailNextWrite=true;service.Tick();check(service.Read(c).Timeline!.Cursor<200,"injected write failure never skips the opportunity cursor");
            SimulationPump.Drain(service);check(service.Read(c).CompletedCycles==2&&c.Items.Where(i=>i.QualifiedItemId is "(O)128" or "(O)129" or "(O)130" or "(O)131").Sum(i=>i.Stack)==200,"retry resolves paid budget exactly once after missing-history/write failure");
            check(paid.Timeline!.Plan.SequenceEqual(c.modData.Writes.Where(p=>p.Key==ProductionService.StateKey).Select(p=>ProductionState.Load(p.Value)).Last(w=>w.Phase=="ready").Timeline!.Plan),"retry retains original opportunity schedule");
        }
        finally{TimelineEnvironment.ReadOverride=null;Game1.Date.TotalDays=oldDay;Game1.timeOfDay=oldTime;Game1.year=oldYear;}
    }
}

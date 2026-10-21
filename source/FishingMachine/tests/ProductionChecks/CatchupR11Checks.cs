using System.Diagnostics;
using System.Text.Json;
using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Objects;
internal static class CatchupR11Checks
{
 static long Bait(Chest c)=>c.Items.Where(i=>i?.QualifiedItemId=="(O)685").Sum(i=>(long)i.Stack);
 static long Output(Chest c)=>c.Items.Where(i=>i!=null&&i.QualifiedItemId!="(O)685").Sum(i=>(long)i.Stack);
 static void Add(Chest c,int n){while(n>0){int k=Math.Min(999,n);c.Items.Add(ItemRegistry.Create("(O)685",k));n-=k;}}
 static (ModEntry M,ProductionService S,Chest C) Make(bool crab,int level,int cycles=12,bool small=true)
 {
  Game1.Date.TotalDays=0;Game1.timeOfDay=600;Game1.year=1;
  var m=new ModEntry();if(small){m.Config.BaitPerCycle=m.Config.LevelTwoBaitPerCycle=m.Config.LevelThreeBaitPerCycle=4;m.CrabConfig.LevelOneBait=m.CrabConfig.LevelTwoBait=4;}
  var s=new ProductionService(m);var c=new Chest{QualifiedItemId="(BC)"+(crab?MachineKinds.CrabId:MachineKinds.FishingId)};
  c.modData[ModEntry.Tag+"MachineId"]=Guid.NewGuid().ToString("N");c.modData[ProductionService.StateKey]=new ProductionState{Kind=crab?MachineKinds.Crab:MachineKinds.Fishing,Level=level}.Save();
  c.Location.objects[c.TileLocation]=c;m.Machines.All[c]=c.Location;Add(c,s.RequiredBait(c)*cycles);return(m,s,c);
 }
 static int Drain(ProductionService s,Chest c,int max=3000)
 {
  for(int i=0;i<max;i++){string before=s.Read(c).Save();s.Tick();if(s.Read(c).Save()==before)return i+1;}
  throw new Exception("catch-up did not quiesce within bounded ticks: "+JsonSerializer.Serialize(new{s.Read(c).Phase,s.Read(c).CompletedCycles,cursor=s.Read(c).Timeline?.Cursor}));
 }
 internal static void Run(Action<bool,string> check)
 {
  var pref=BetterStardewValley.Configuration.SharedPreferences.Current;pref.AutoRestartProduction=true;TimelineEnvironment.ReadOverride=null;
  foreach(bool crab in new[]{false,true})foreach(int level in Enumerable.Range(1,crab?2:3))foreach(int days in new[]{1,2,3,20,21})
  {
   Console.WriteLine($"CASE crab={crab} level={level} days={days}");
   var (m,s,c)=Make(crab,level);s.Tick();var first=s.Read(c).Snapshot();int required=first.BaitUsed;
   Game1.Date.TotalDays=days;Drain(s,c);var a=s.Read(c);int duration=crab?(level==1?2:4):level+1;int completed=days/duration;
   check(a.CompletedCycles==completed&&a.LevelCycles==completed&&a.Phase=="working","T01–03 chronological completions "+crab+"/"+level+"/"+days);
   check(a.StartedAt==360+completed*duration*1440&&a.DueAt==360+(completed+1)*duration*1440&&Bait(c)==required*(11-completed),"T01–03 deadline and payment conservation "+crab+"/"+level+"/"+days);
   string raw=a.Save();long output=Output(c);s.Reset();Drain(s,c);
   check(s.Read(c).Save()==raw&&Output(c)==output,"T05/T12 frozen clock and reload do not repeat "+crab+level+days);
   Game1.Date.TotalDays=0;Drain(s,c);Game1.Date.TotalDays=days;Drain(s,c);
   check(s.Read(c).CompletedCycles==completed&&Bait(c)==required*(11-completed)&&Output(c)==output,"T13 rollback cannot replay elapsed purchases "+crab+level+days);
  }
  foreach(bool crab in new[]{false,true})
  {
   var (m,s,c)=Make(crab,1,3);s.Tick();Game1.Date.TotalDays=20;Drain(s,c);
   check(s.Read(c).CompletedCycles==3&&s.Read(c).Phase=="idle"&&Bait(c)==0,"T06 only three historically funded cycles "+crab);
   Add(c,4);s.Tick();check(s.Read(c).StartedAt==ProductionService.Now&&s.Read(c).CompletedCycles==3,"T06 later fuel starts now, never fills seven old cycles "+crab);
   (m,s,c)=Make(crab,1);s.Tick();pref.AutoRestartProduction=false;s.Tick();Game1.Date.TotalDays=20;Drain(s,c);
   check(s.Read(c).CompletedCycles==1&&Bait(c)==44,"T07 OFF completes only the paid task "+crab);
   pref.AutoRestartProduction=true;s.Tick();check(s.Read(c).StartedAt==ProductionService.Now&&Bait(c)==40,"T07 reenable starts now "+crab);
   (m,s,c)=Make(crab,1);s.Tick();Game1.Date.TotalDays=20;
   // Edit current settings before backlog executes: old evidence must still govern past starts.
   m.Config.CycleGameMinutes=m.CrabConfig.CycleGameMinutes=1440;Drain(s,c);
   check(s.Read(c).CompletedCycles==10&&s.Read(c).StartedAt==ProductionService.Now&&s.Read(c).DueAt-ProductionService.Now==1440,"T08 config change at jump destination does not rewrite history "+crab);
   for(int duration=1;duration<=4;duration++)
   {
    (m,s,c)=Make(crab,1,30);m.Config.CycleGameMinutes=m.CrabConfig.CycleGameMinutes=duration*1440;s.Tick();Game1.Date.TotalDays=20;Drain(s,c);
    check(s.Read(c).CompletedCycles==20/duration&&s.Read(c).DueAt-s.Read(c).StartedAt==duration*1440,"T08 custom duration "+crab+"/"+duration);
   }
   (m,s,c)=Make(crab,1);s.Tick();var paid=s.Read(c).Snapshot();while(c.Items.Count<70)c.Items.Add(ItemRegistry.Create("(O)full"+c.Items.Count,999));
   Game1.Date.TotalDays=20;Drain(s,c);check(s.Read(c).Phase=="ready"&&s.Read(c).CompletedCycles==1,"T18 full capacity preserves output and stops catch-up "+crab);
   c.Items.RemoveAll(i=>i.QualifiedItemId.StartsWith("(O)full"));Drain(s,c);
   check(s.Read(c).CompletedCycles==1&&s.Read(c).StartedAt==ProductionService.Now,"T18 capacity released now cannot backfill paused time "+crab);
  }
  foreach(bool crab in new[]{false,true})foreach(int level in Enumerable.Range(1,crab?2:3))
  {
   var (m,s,c)=Make(crab,level,12,false);s.Tick();Game1.Date.TotalDays=20;var watch=Stopwatch.StartNew();int ticks=Drain(s,c);watch.Stop();
   check(s.Read(c).CompletedCycles==20/(crab?(level==1?2:4):level+1),"T01 full default fuel budget "+crab+level);
   Console.WriteLine(JsonSerializer.Serialize(new{benchmark="20-day production adapter",crab,level,ticks,milliseconds=watch.Elapsed.TotalMilliseconds,completed=s.Read(c).CompletedCycles,output=Output(c),fake_environment=true,live_game=false}));
  }
  // The prepared outcome survives a later missing-item failure and ecology changes.
  {
   var (m,s,c)=Make(false,1);m.Config.Normal=new(){Fish=1};m.Config.FishCriticalChance=0;s.Tick();Game1.Date.TotalDays=2;
   ItemRegistry.Missing="(O)128";s.Tick();var paused=s.Read(c).Snapshot();
   check(paused.Timeline?.Prepared?.Pending.Any(p=>p.Id=="(O)128")==true&&paused.Timeline.Cursor==0,"M10 failed resolution retains the generated result before settlement");
   ItemRegistry.Missing=null;TimelineEnvironment.ReadOverride=(_,at)=>new(){Location=c.Location.NameOrUniqueName,X=(int)c.TileLocation.X,Y=(int)c.TileLocation.Y,Day=MachineTimeline.Day(at),Fish=new(){new(600,2600,new(){new("(O)129",1)})},Other=new(){"(O)168"}};
   s.Reset();Drain(s,c);check(c.Items.Any(i=>i.QualifiedItemId=="(O)128")&&s.Read(c).CompletedCycles==1,"M10 retry uses the saved result, not the changed fish pool");TimelineEnvironment.ReadOverride=null;
  }
  {
   var (m,s,c)=Make(true,1);s.Tick();Game1.Date.TotalDays=2;ItemRegistry.Missing="*";s.Tick();
   var prepared=ProductionState.Load(s.Read(c).Save());
   check(prepared.Timeline?.Prepared!=null&&prepared.Timeline.Prepared.Continuity==null,"M10 crab prepared result reloads without nested fuel history");
   string result=JsonSerializer.Serialize(prepared.Timeline!.Prepared!.Pending);
   ItemRegistry.Missing=null;s.Reset();Drain(s,c);
   check(s.Read(c).CompletedCycles==1&&prepared.Timeline.Prepared.Pending.All(p=>c.Items.Any(i=>i.QualifiedItemId==p.Id)),"M10 crab failed resolution keeps output through reload");
  }
  // Stable event ordering uses logical opportunity time, then machine identity.
  {
   var (m,s,a)=Make(false,1);var b=new Chest();b.Location.objects[b.TileLocation]=b;m.Machines.All[b]=b.Location;
   a.modData[ModEntry.Tag+"MachineId"]="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";b.modData[ModEntry.Tag+"MachineId"]="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
   Add(b,48);s.Tick();Game1.Date.TotalDays=20;var events=new List<(long At,string Id)>();
   TimelineEnvironment.ReadOverride=(token,at)=>
   {
    var c=s.Read(a).CycleToken==token?a:b;events.Add((at,c.modData[ModEntry.Tag+"MachineId"]));
    return new(){Location=c.Location.NameOrUniqueName,X=(int)c.TileLocation.X,Y=(int)c.TileLocation.Y,Day=MachineTimeline.Day(at),Fish=new(){new(600,2600,new(){new("(O)128",1)})},Other=new(){"(O)168"}};
   };
   SimulationPump.Drain(s);TimelineEnvironment.ReadOverride=null;
   check(events.SequenceEqual(events.OrderBy(p=>p.At).ThenBy(p=>p.Id,StringComparer.Ordinal))&&s.Read(a).CompletedCycles==10&&s.Read(b).CompletedCycles==10,"T14 actual multi-machine dispatcher preserves global logical ordering");
  }
  check(MachineCycleRules.RequiredCycles(MachineKinds.Fishing,1)==15&&MachineCycleRules.RequiredCycles(MachineKinds.Fishing,2)==20&&MachineCycleRules.RequiredCycles(MachineKinds.Crab,1)==20,"T17 thresholds 15/20/20");
  foreach(long count in new long[]{19,20,22})
  {
   var state=new ProductionState{Level=2,CompletedCycles=count+15,LevelCycles=count};
   var materials=MachineUpgrade.Requirements(2).SelectMany((r,i)=>r.Id.StartsWith("(T)")?Enumerable.Range(0,r.Count).Select(n=>new UpgradeSlot(i*10+n,r.Id,1,true)):new[]{new UpgradeSlot(i*10,r.Id,r.Count)});
   check((MachineUpgrade.Plan(state,materials)!=null)==(count>=20),"T17 Lv2 stage boundary "+count);
  }
 }
}

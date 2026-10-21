using System.Text.Json;
using Eddie.FishingMachine.Core;
int assertions=0;
void Check(bool value,string label){assertions++;if(!value)throw new Exception(label);}
void Throws(Action action,string label){try{action();}catch{Check(true,label);return;}throw new Exception(label);}
void Rate(int hits,int n,double p,string label){Check(Math.Abs(hits-n*p)<=6*Math.Sqrt(n*p*(1-p))+1,label+": predeclared 6-sigma binomial band");}
var report=new List<object>();
var cfg=new BalanceConfig();
for(int level=1;level<=3;level++)
{
 int budget=MachineUpgrade.RequiredBait(new(){Level=level},cfg);
 Check(budget==new[]{200,300,400}[level-1]&&cfg.MinutesForLevel(level)==(level+1)*1440,"T01 budget and time L"+level);
 Check(MachineUpgrade.Capacity(level)==(level==1?36:70),"T19 capacity L"+level);
 var paid=MachineTimeline.StartFish(new(){Level=level},cfg,new[]{new BaitSpend(0,budget,BaitKind.Normal,null)},17,360,421);
 Check(paid.Timeline!.Plan.Count==budget&&paid.BaitUsed==budget&&paid.Pending.Count==0,"T01 one plan per paid bait");
 Check(ProductionState.Load(paid.Save()).Save()==paid.Save(),"T18 new paid state round trip");
 foreach(int seed in new[]{117,38101,960004})
 {
  const int n=100000;var r=new Random(seed+level*913);var mult=new int[3];var quality=new int[4];var treasure=new int[3];
  for(int i=0;i<n;i++)
  {
   mult[MachineLevelRules.ExtraMultiplier(cfg,level,r.NextDouble())]++;
   quality[Array.IndexOf(new[]{0,1,2,4},MachineLevelRules.Quality(level,r.NextDouble()))]++;
   treasure[MachineLevelRules.Treasure(level,r.NextDouble())]++;
  }
  var mp=level==1?new[]{.55,.45,0}:level==2?new[]{.35,.55,.10}:new[]{.10,.66,.24};
  var qp=level==1?new[]{.75,.25,0,0}:level==2?new[]{0,.5,.35,.15}:new[]{0,0,.25,.75};
  var tp=level==1?new[]{.965,.03,.005}:level==2?new[]{.91,.06,.03}:new[]{.84,.10,.06};
  for(int i=0;i<3;i++){Rate(mult[i],n,mp[i],"T03 multiplier");Rate(treasure[i],n,tp[i],"T03 treasure");}
  for(int i=0;i<4;i++)Rate(quality[i],n,qp[i],"T03 quality");
  report.Add(new{kind="fish-events",level,seed,n,mult,quality,treasure,mean=(double)(mult[0]+2*mult[1]+3*mult[2])/n});
 }
 var allFish=new BalanceConfig{Normal=new(){Fish=1},Deluxe=new(){Fish=1}};
 var batch=Production.Begin(allFish,new[]{new BaitSpend(0,budget,level>=2?BaitKind.Challenge:BaitKind.Normal,null)},new[]{new FishChoice("(O)128",1)},new[]{"(O)168"},360,55,new(){Level=level});
 Check(batch.FishCount==budget&&batch.EmptyCount==0&&batch.OtherCount==0,"T01 full production path budget");
 Check(batch.Pending.Sum(p=>p.Count)==batch.ProducedFishCount+batch.TreasureItemCount&&batch.NormalTreasures+batch.AdvancedTreasures<=budget,"T02 real output conservation/one treasure per event");
 Check(ProductionState.Load(batch.Save()).Pending.SequenceEqual(batch.Pending),"T18 tripled challenge output validates");
 foreach(double u in new[]{0,.029999999,.03,.034999999,.035,.059999999,.06,.089999999,.09,.099999999,.1,.159999999,.16,.249999999,.25,.499999999,.5,.549999999,.55,.649999999,.65,.659999999,.66,.749999999,.75,.849999999,.85,.899999999,.90,.999999999})
 {
  int m=MachineLevelRules.ExtraMultiplier(cfg,level,u),q=MachineLevelRules.Quality(level,u),t=MachineLevelRules.Treasure(level,u);
  Check(m==(level==1?(u<.45?1:0):level==2?(u<.55?1:u<.65?2:0):(u<.66?1:u<.90?2:0)),"T02 multiplier endpoint");
  Check(q==(level==1?(u<.25?1:0):level==2?(u<.5?1:u<.85?2:4):(u<.75?4:2)),"T02 quality endpoint");
  Check(t==(level==1?(u<.03?1:u<.035?2:0):level==2?(u<.06?1:u<.09?2:0):(u<.1?1:u<.16?2:0)),"T02 treasure endpoint");
 }
}
Check(15*2+20*3==90&&MachineCycleRules.RequiredCycles(MachineKinds.Fishing,1)==15&&MachineCycleRules.RequiredCycles(MachineKinds.Fishing,2)==20,"T01 progression 90 minimum days");
foreach(int level in new[]{1,2,3})foreach(int seed in new[]{21,17003,80291})
{
 const int n=100000;int hits=0;var pool=new[]{new CycleLegendaryOpportunity(111,3,"(O)128",level==2?9:1,level==2?1:2,new(){new("(O)163",1,true)})};
 for(int i=0;i<n;i++){var d=CycleLegendaryRules.Decide(level,MachineTimeline.SeedFor(seed,i,721),pool);if(d.Hit)hits++;}
 Rate(hits,n,CycleLegendaryRules.Chance(level),"T04 eligible cycle denominator");
 report.Add(new{kind="independent-eligible-cycles",level,seed,total=n,eligible=level>=2?n:0,hits,fallback=0,planned=hits,submitted=0,pureDecisionOnly=true,quotaResetEachTrial=true});
}
Check(!CycleLegendaryRules.Decide(3,42,Array.Empty<CycleLegendaryOpportunity>()).Hit,"T09 no candidate no legendary");
foreach(var (n,p,label) in new[]{(1000000,.0005,"mutant"),(1000000,.003,"king"),(1000000,.01,"spirit")})
{
 int seed=81209,hits=0;var r=new Random(seed);
 for(int i=0;i<n;i++){double u=r.NextDouble();if(label=="mutant"?MutantLobsterRules.Select(true,u):label=="king"?LobsterKingRules.Select(true,true,u)==RareCrab.LobsterKing:GiantLobsterRules.SelectSpirit(true,true,BaitKind.GiantLobster,u))hits++;}
 Rate(hits,n,p,"T05 "+label);report.Add(new{kind=label,seed,n,hits,quotaResetEachTrial=true});
}
var ledger=new MutantAnnualLedger();string saved="";int outputs=0;
void Save(MutantAnnualLedger x)=>saved=JsonSerializer.Serialize(x);
Check(ledger.Commit(0,"pot",Save,_=>outputs++,()=>outputs--),"T15 pot commits first farm claim");
Check(!ledger.Commit(111,"machine",Save,_=>outputs++,()=>outputs--),"T15 machine shares same year");
ledger=JsonSerializer.Deserialize<MutantAnnualLedger>(saved)!;
Check(!ledger.Available(1)&&ledger.Commit(112,"machine",Save,_=>outputs++,()=>outputs--),"T15 reload retains old year, next opens");
Check(outputs==2&&ledger.Claims.Count==2,"T15 uncollected old output coexists with next year");
Throws(()=>ledger.Commit(224,"failure",Save,_=>{outputs++;throw new Exception();},()=>outputs--),"T15 failing output commit throws");
Check(ledger.Available(3)&&outputs==2,"T15 own failed transaction releases own year only");
var migrated=new MutantAnnualLedger{MigrationLockedThroughYear=5,MigrationReason="missing-history"};
Check(!migrated.Available(1)&&!migrated.Available(5)&&migrated.Available(6)&&migrated.Claims.Count==0,"T17 migration lock is not a fabricated catch and is not permanent");
var seasonal=new KingLedger();Check(KingTransactions.Commit(seasonal,1,0,0,"pot",_=>{},_=>{},()=>{}),"T16 ordinary king first season");
Check(!seasonal.Available(1,0)&&seasonal.Available(1,1)&&!seasonal.Available(1,3),"T16 seasonal ledger independent, winter prohibited");
foreach(int oldBudget in new[]{150,200,300,350,450,650})
{
 var old=new ProductionState{Level=3,Phase="working",BaitUsed=oldBudget,FishCount=oldBudget,StartedAt=360,DueAt=1800,Pending=new(){new("(O)128",oldBudget,4)}};
 var read=ProductionState.Load(old.Save());Check(read.BaitUsed==oldBudget&&read.Pending.Single().Count==oldBudget&&read.DueAt==1800,"T18 immutable legacy budget "+oldBudget);
}
var nine=new ProductionState{FishingBalanceVersion=1,Level=2,Phase="working",BaitUsed=1,FishCount=1,ChallengeExtraFish=2,CriticalFishCount=6,StartedAt=360,DueAt=500,Pending=new(){new("(O)163",9,1)},LegendaryPlans=new(){new("(O)163",9,1,"(O)128")}};
Check(ProductionState.Load(nine.Save()).Pending.Single().Count==9,"T10/T18 silver nine-legend event valid");
nine.CriticalFishCount=7;Throws(()=>ProductionState.Load(nine.Save()),"T18 corrupt over-limit crit rejected");
foreach(int level in new[]{1,2,3})
{
 var c=new BalanceConfig{BaitPerCycle=10000,LevelTwoBaitPerCycle=10000,LevelThreeBaitPerCycle=10000};
 var b=Production.Begin(c,new[]{new BaitSpend(0,10000,BaitKind.Normal,null)},new[]{new FishChoice("(O)128",1)},new[]{"(O)167","(O)168","(O)169","(O)170","(O)171","(O)172","(O)153","(O)ThirdPartyAquatic"},360,309,new(){Level=level,AutoTrash=true},"(O)RiverJelly");
 Check(b.EmptyCount==0&&(level==1?b.Pending.Any(p=>MachineUpgrade.IsTrash(p.Id)):b.Pending.All(p=>!MachineUpgrade.IsTrash(p.Id))),"T06 trash policy");
 Check(b.Pending.Any(p=>p.Id=="(O)153")&&b.Pending.Any(p=>p.Id=="(O)ThirdPartyAquatic"),"T06 algae and custom nonfish retained");
 if(level>1)Check(b.Pending.Any(p=>p.Id=="(O)RiverJelly"),"T06 jelly retained");
}
var tiny=new BalanceConfig{LevelTwoBaitPerCycle=1};var one=new[]{new BaitSpend(0,1,BaitKind.Normal,null)};
Throws(()=>Production.Begin(tiny,one,Array.Empty<FishChoice>(),new[]{"(O)168"},360,1,new(){Level=2}),"T07 empty legal environment refuses paid generation");
Check(Production.Begin(tiny,one,Array.Empty<FishChoice>(),new[]{"(O)168"},360,1,new(){Level=2},"(O)CaveJelly").Pending.Single().Id=="(O)CaveJelly","T07 nonfish-only legal fallback");
foreach(string jelly in new[]{"(O)RiverJelly","(O)SeaJelly","(O)CaveJelly"})
 Check(Production.Begin(tiny,one,Array.Empty<FishChoice>(),new[]{"(O)168"},360,1,new(){Level=2},jelly).Pending.Single().Id==jelly,"T06 every legal jelly survives empty fish branch "+jelly);
Check(GiantLobsterRules.ValueWeight(299)==1&&GiantLobsterRules.ValueWeight(300)==1.25&&GiantLobsterRules.ValueWeight(301)==1.25,"T13 inclusive runtime base-price threshold");
foreach(int b in new[]{96,176,200,224,500,568})Check(GiantLobsterRules.BarHeight(b)==Math.Min(568,(int)Math.Floor((b+12)*1.20)),"T13 reference +12 then 1.20 floor/physical clamp");
var mixcfg=new BalanceConfig{LevelThreeBaitPerCycle=400,Normal=new(){Fish=1}};
for(int seed=0;seed<1000;seed++)
{
 var b=Production.Begin(mixcfg,new[]{new BaitSpend(0,399,BaitKind.Normal,null),new BaitSpend(1,1,BaitKind.GiantLobster,null)},new[]{new FishChoice("(O)128",1),new FishChoice(Production.SpiritId,1,true)},new[]{"(O)168"},360,seed,new(){Level=3});
 Check(b.Pending.Where(p=>p.Id==Production.SpiritId).Sum(p=>p.Count)<=3,"T11 mixed 399+1 maximum one eligible Spirit opportunity");
}
var corruptDecision=MachineTimeline.StartFish(new(){Level=2},new BalanceConfig(),new[]{new BaitSpend(0,300,BaitKind.Normal,null)},17,360,42);
corruptDecision.Timeline!.LegendaryDecision.StatsRecorded=true;
Throws(()=>ProductionState.Load(corruptDecision.Save()),"T18 statistical completion without a durable decision is rejected");
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,assertions,kind="pure production/transaction/statistical checks",live_game=false,report},new JsonSerializerOptions{WriteIndented=true}));

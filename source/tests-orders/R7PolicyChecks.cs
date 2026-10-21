using BetterStardewValley.Orders;
using System.Text.Json;

// Uses production policy, not a duplicate new-policy generator. OldPolicy below is
// explicitly the retired R6 formula, retained here only for numerical comparison.
internal static class R7PolicyChecks
{
 internal static double[] OldPolicy(int price,int quantity)
 {
  double sum=OrderMarket.DwarfRewards.Sum(r=>1.0/r.ReplacementValue),mean=9/sum;
  double mix=Math.Clamp((price*quantity*.8-15)/(mean-15),0,1);
  return OrderMarket.DwarfRewards.Select(r=>mix/(r.ReplacementValue*sum)+(r.Id=="(O)378"?1-mix:0)).ToArray();
 }
 internal static object Run(Action<bool,string> check)
 {
  double m=OrderMarket.MorrisChance,end=m+OrderMarket.DwarfChance;
  foreach(var (r,npc) in new[]{(0.0,"Morris"),(Math.BitDecrement(m),"Morris"),(m,"Dwarf"),(Math.BitIncrement(m),"Dwarf"),(Math.BitDecrement(end),"Dwarf"),(end,""),(Math.BitIncrement(end),""),(Math.BitDecrement(1.0),"")})
   check(OrderMarket.RareNpc(r)==npc,"mutually exclusive NPC interval boundary "+r.ToString("R"));
  int start=0;foreach(var option in OrderMarket.DwarfRewards)
  {
   check(OrderMarket.DwarfRewardAt(start)==new MarketReward(option.Id,1)&&OrderMarket.DwarfRewardAt(start+option.Weight-1)==new MarketReward(option.Id,1),"reward interval edges "+option.Id);
   start+=option.Weight;
  }
  check(start==100,"reward integer weights total exactly 100");
  foreach(int ticket in new[]{-1,100}){bool rejected=false;try{OrderMarket.DwarfRewardAt(ticket);}catch(ArgumentOutOfRangeException){rejected=true;}check(rejected,"invalid reward ticket rejected");}
  var allTickets=Enumerable.Range(0,100).Select(OrderMarket.DwarfRewardAt).ToArray();
  foreach(var option in OrderMarket.DwarfRewards)check(allTickets.Count(x=>x.Id==option.Id)==option.Weight,"exhaustive ticket measure "+option.Id);
  var p=OrderMarket.DwarfProbabilities();check(p.All(x=>x>0)&&Math.Abs(p.Sum()-1)<1e-12,"positive probabilities sum to one");
  check(Math.Abs(p.Take(3).Sum()-.5)<1e-12&&Math.Abs(p.Select((v,i)=>v*OrderMarket.DwarfRewards[i].ReplacementValue).Sum()*3-524.25)<1e-10,"50 percent bombs and 524.25 internal value");
  // Synthetic fixtures. Prices are explicitly NOT a scan of target Data/Objects.
  (string Id,string Name,int Price)[] foods={("216","Bread",60),("194","Fried Egg",35),("195","Omelet",125),("211","Pancakes",80),("210","Hashbrowns",120),("196","Salad",110)};
  var comparison=new List<object>();var rewardCounts=OrderMarket.DwarfRewards.ToDictionary(r=>r.Id,_=>0);
  var cells=new List<object>();ulong seed=12345;int total=0,maximum=0,totalItems=0;long totalValue=0;
  var joint=OrderMarket.DwarfRewards.ToDictionary(r=>r.Id,_=>Enumerable.Range(1,5).ToDictionary(q=>q,_=>0));
  var numberCounts=Enumerable.Range(1,5).ToDictionary(q=>q,_=>0);
  // Exact RNG-state comparison proves two successive draws, not one value mapped twice.
  ulong actual=987654,expectedRng=actual;
  for(int n=0;n<1000;n++)
  {var type=OrderMarket.DwarfRewardAt(OrderMarket.Pick(ref expectedRng,100));var count=1+OrderMarket.Pick(ref expectedRng,5);var result=OrderMarket.DwarfReward(ref actual);check(actual==expectedRng&&result==(type with{Count=count}),"reward uses successive independent type/count draws");}
  foreach(var food in foods)foreach(int quantity in Enumerable.Range(1,4))
  {
   var counts=OrderMarket.DwarfRewards.ToDictionary(r=>r.Id,_=>0);
   for(int i=0;i<5000;i++)
   {
    var reward=OrderMarket.DwarfReward(ref seed);check(reward.Count is >=1 and <=5&&counts.ContainsKey(reward.Id),"direct production reward: one supported type, count 1–5, never empty");
    counts[reward.Id]++;rewardCounts[reward.Id]++;joint[reward.Id][reward.Count]++;numberCounts[reward.Count]++;totalItems+=reward.Count;totalValue+=(long)reward.Count*OrderMarket.DwarfRewards.Single(r=>r.Id==reward.Id).ReplacementValue;total++;maximum=Math.Max(maximum,reward.Count);
   }
   cells.Add(new{food.Id,quantity,draws=5000,counts});var old=OldPolicy(food.Price,quantity);
   comparison.Add(new{food.Id,food.Name,fixturePrice=food.Price,quantity,oldGenerationActuallyAllowed=quantity<=2,oldProbabilities=old,oldBombs=old.Take(3).Sum(),oldExpected=old.Select((v,i)=>v*OrderMarket.DwarfRewards[i].ReplacementValue).Sum(),newProbabilities=p,newBombs=.5,newExpected=524.25});
  }
  // Prespecified six-standard-error tolerances plus one observation; fixed seeds.
  foreach(var option in OrderMarket.DwarfRewards)
  {double expected=option.Weight/100.0;check(Math.Abs(rewardCounts[option.Id]-total*expected)<=6*Math.Sqrt(total*expected*(1-expected))+1,"direct reward distribution six-sigma tolerance "+option.Id);}
  foreach(var option in OrderMarket.DwarfRewards)foreach(int count in Enumerable.Range(1,5))
  {
   double probability=option.Weight/500.0;int observed=joint[option.Id][count],kindTotal=rewardCounts[option.Id];
   check(observed>0&&Math.Abs(observed-total*probability)<=6*Math.Sqrt(total*probability*(1-probability))+1,"45 type-count joint probabilities "+option.Id+" x "+count);
   check(Math.Abs(observed-kindTotal*.2)<=6*Math.Sqrt(kindTotal*.2*.8)+1,"quantity conditional on each type is 20 percent");
  }
  foreach(int count in Enumerable.Range(1,5))check(Math.Abs(numberCounts[count]-total*.2)<=6*Math.Sqrt(total*.2*.8)+1,"overall quantity uniform 20 percent");
  check(maximum==5&&Math.Abs(totalItems/(double)total-3)<=6*Math.Sqrt(2.0/total),"maximum five and mean three within prespecified six-sigma");
  object OldSimulation(int price,int quantity)
  {
   ulong oldSeed=12345;var probabilities=OldPolicy(price,quantity);var counts=OrderMarket.DwarfRewards.ToDictionary(r=>r.Id,_=>0);
   for(int n=0;n<120000;n++)
   {double value=OrderMarket.Unit(ref oldSeed);int i=0;for(;i<probabilities.Length-1;i++){value-=probabilities[i];if(value<0)break;}counts[OrderMarket.DwarfRewards[i].Id]++;}
   return new{price,quantity,draws=120000,seed=12345,counts,bombs=counts.Take(3).Sum(x=>x.Value)/120000.0,expectedSample=counts.Sum(x=>x.Value*OrderMarket.DwarfRewards.Single(r=>r.Id==x.Key).ReplacementValue)/120000.0,scope="simulation of retired formula; not current production reward"};
  }
  var synthetic=OrderMarket.Npcs.Select((n,i)=>new MarketCandidate("(O)"+(100+i),n,50,i%2,n=="Morris")).ToArray();
  var full=synthetic.Where(c=>c.Npc!="Dwarf").Concat(foods.Select(f=>new MarketCandidate("(O)"+f.Id,"Dwarf",f.Price))).ToArray();
  // Unequal stable present counts test the unchanged inverse-count weighting directly.
  var present=OrderRules.Npcs.SelectMany((npc,i)=>Enumerable.Range(0,i).Select(n=>new MarketOrder(npc+n,npc,"(O)24",1,35,125,44,1,0,"standard","small",new()))).ToArray();
  var generationCounts=OrderMarket.Npcs.ToDictionary(n=>n,_=>0);int specials=0;var quantities=Enumerable.Range(1,4).ToDictionary(n=>n,_=>0);var foodCounts=foods.ToDictionary(f=>f.Id,_=>0);var failures=new Dictionary<string,int>();var generatedPairs=new HashSet<string>();var generatedRewards=new HashSet<string>();
  for(int sequence=1;sequence<=100000;sequence++)
  {
   var order=OrderMarket.Generate(928734,12,sequence,2,present,full,out string failure);
   if(order==null){failures[failure]=failures.GetValueOrDefault(failure)+1;continue;}
   generationCounts[order.Npc]++;if(order.Kind=="special")specials++;
   if(order.Npc=="Dwarf")
   {check(order.Quantity is >=1 and <=4&&order.MinimumQuality==0&&order.Gold==0&&order.Kind=="barter"&&order.Size=="small"&&order.Rewards.Count==1&&order.Rewards[0].Count is >=1 and <=5,"generated Dwarf full terms");quantities[order.Quantity]++;foodCounts[order.Item[3..]]++;generatedPairs.Add(order.Item+"/"+order.Quantity);generatedRewards.Add(order.Rewards[0].Id+"/"+order.Rewards[0].Count);}
  }
  check(generatedPairs.Count==24&&generatedRewards.Count==45,"real Generate covers all six-food x four requirements and all 45 rewards including Mega Bomb x5");
  double weightSum=Enumerable.Range(0,6).Sum(i=>1.0/(1+i));var theoretical=OrderRules.Npcs.Select((n,i)=>(n,p:(1-m-OrderMarket.DwarfChance)/(1+i)/weightSum)).ToDictionary(x=>x.n,x=>x.p);theoretical["Morris"]=m;theoretical["Dwarf"]=OrderMarket.DwarfChance;
  foreach(var pair in theoretical)check(Math.Abs(generationCounts[pair.Key]-100000*pair.Value)<=6*Math.Sqrt(100000*pair.Value*(1-pair.Value))+1,"production Generate NPC six-sigma tolerance "+pair.Key);
  int dwarfs=generationCounts["Dwarf"],morris=generationCounts["Morris"];
  foreach(var pair in quantities)check(pair.Value>0&&Math.Abs(pair.Value-dwarfs*.25)<=6*Math.Sqrt(dwarfs*.25*.75)+1,"all four Dwarf quantities uniform tolerance");
  check(foodCounts.All(x=>x.Value>0),"all six synthetic cooking candidates generated");
  check(Math.Abs(specials-morris*.25)<=6*Math.Sqrt(morris*.25*.75)+1,"Special conditional 25 percent tolerance");
  var noDwarf=full.Where(c=>c.Npc!="Dwarf").ToArray();var noMorris=full.Where(c=>c.Npc!="Morris").ToArray();int missingDwarf=0,missingMorris=0;
  for(int sequence=1;sequence<=10000;sequence++)
  {
   var expected=OrderMarket.Generate(928734,12,sequence,2,present,full,out _)!;
   foreach(var (pool,npc) in new[]{(noDwarf,"Dwarf"),(noMorris,"Morris")})
   {
    var result=OrderMarket.Generate(928734,12,sequence,2,present,pool,out string failure);
    if(expected.Npc==npc){check(result==null&&failure==npc+"/no-valid-template","missing special candidate fails without NPC redraw");if(npc=="Dwarf")missingDwarf++;else missingMorris++;}
    else check(result!=null&&JsonSerializer.Serialize(result)==JsonSerializer.Serialize(expected),"unrelated candidate removal does not re-roll existing selected NPC");
   }
  }
  return new{morrisPolicy=m,candidate=m==.055,seed=928734,day=12,year=2,attempts=100000,successful=generationCounts.Values.Sum(),failures,candidates="synthetic six-food fixture plus one candidate per other NPC; not runtime Content",presentCounts=OrderRules.Npcs.Select((n,i)=>new{npc=n,count=i}),counts=generationCounts,theoretical,specials,morrisOrdinary=morris-specials,specialConditional=specials/(double)morris,specialAllAttempts=specials/100000.0,quantities,foodCounts,missingPoolFailures=new{missingDwarf,missingMorris,attemptsPerPool=10000},rewards=new{seed=12345,draws=total,counts=rewardCounts,joint,numberCounts,totalItems,averageCount=totalItems/(double)total,maximum,bombs=rewardCounts.Take(3).Sum(x=>x.Value)/(double)total,expectedSample=totalValue/(double)total,cells},comparison,oldLow=OldSimulation(35,1),oldSaturated=OldSimulation(60,1),runtimeCatalog="BLOCKED: target Content/Data/Objects not supplied",tolerance="6 binomial standard errors + 1 observation, specified before run"};
 }
}

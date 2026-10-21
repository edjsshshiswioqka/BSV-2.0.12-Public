using BetterStardewValley.Orders;using BetterStardewValley.Transactions;using System.Text.Json;
int checks=0;var names=new HashSet<string>();void Check(bool b,string n){checks++;names.Add(n);if(!b)throw new Exception(n);}void Reject(Action f,string n){bool b=false;try{f();}catch{b=true;}Check(b,n);}
var pool=OrderMarket.Npcs.Select((n,i)=>new MarketCandidate("(O)"+(100+i),n,50,i%2,n=="Morris")).ToList();
// Catalog permits base prices up to 10000; quality-adjusted Morris quotes must survive reload.
foreach(int basePrice in new[]{1,6666,6667,6668,8000,8001,9999,10000})
{
 var priced=pool.Select(c=>c with{Price=basePrice}).ToArray();var qualities=new HashSet<int>();
 for(int sequence=1;sequence<=400;sequence++)
 {
  var quote=OrderMarket.Generate(928734,12,sequence,2,Array.Empty<MarketOrder>(),priced,out _)!;
  if(quote.Npc!="Morris")continue;qualities.Add(quote.MinimumQuality);
  Check(OrderMarket.Valid(quote),$"quality-adjusted quote validates at base price {basePrice}");
  var saved=new MarketBook{Available=new(){quote}};
  var loaded=MarketBook.Load(saved.Save()).Available.Single();
  Check(loaded.ReferencePrice==quote.ReferencePrice&&loaded.Gold==quote.Gold&&loaded.MinimumQuality==quote.MinimumQuality,"high-price quote survives unchanged serialization");
 }
 Check(qualities.SetEquals(new[]{1,2}),"boundary cases include both Morris quality grades");
}
var counts=OrderMarket.Npcs.ToDictionary(n=>n,n=>0);int special=0,failures=0;var samples=new List<MarketOrder>();
foreach(var (quality,limit) in new[]{(0,10000),(1,12500),(2,15000)})
{
 var quote=new MarketOrder("price-limit","Morris","(O)24",1,limit,180,limit*180/100,12,quality,"premium","small",new());
 Check(OrderMarket.Valid(quote)&&!OrderMarket.Valid(quote with{ReferencePrice=limit+1}),"quality-aware reference limit still rejects excessive prices");
}
for(int n=1;n<=100000;n++)
{
 var c=OrderMarket.Generate(928734,12,n,2,samples,pool,out var failure);if(c==null){failures++;continue;}counts[c.Npc]++;if(c.Kind=="special")special++;
 Check(OrderMarket.Valid(c),"generated schema valid");Check(c.Gold==OrderRules.Reward(c.ReferencePrice,c.Quantity,c.Percent),"fixed quoted formula");
 if(c.Npc=="Morris")Check(c.Percent==(c.Kind=="special"?80:180)&&c.MinimumQuality is 1 or 2,"Morris exact multiplier and silver/gold threshold");
 if(c.Kind=="special")Check(c.Rewards.Count==1&&c.Rewards[0]==new MarketReward("(BC)272",1),"native Auto-Petter once");
 if(c.Kind=="barter")
 {
  Check(c.Gold==0&&c.Quantity is >=1 and <=4&&c.Rewards.Count==1&&c.Rewards[0].Count is >=1 and <=5,"Dwarf total food cap and one reward per trade");
  var probabilities=OrderMarket.DwarfProbabilities();
  Check(Math.Abs(probabilities.Select((p,i)=>p*OrderMarket.DwarfRewards[i].ReplacementValue).Sum()*3-524.25)<1e-10,"new fixed expected replacement value; no old food-price cap");
 }
 if(samples.Count==40)samples.RemoveAt(0);samples.Add(c);
}
Check(failures==0,"complete templates have zero generation failures");
Check(Math.Abs(counts["Morris"]/100000.0-OrderMarket.MorrisChance)<.004,"Morris sample tolerance 0.4 percentage points");
Check(Math.Abs(counts["Dwarf"]/100000.0-1.0/35)<.003,"Dwarf sample tolerance 0.3 percentage points");
Check(Math.Abs(special/(double)counts["Morris"]-.25)<.025,"special conditional tolerance 2.5 percentage points");
Check(Math.Abs(special/100000.0-OrderMarket.MorrisChance*.25)<.002,"special overall tolerance 0.2 percentage points");
Check(OrderRules.Npcs.All(n=>counts[n]>10000)&&OrderRules.Npcs.Select(n=>counts[n]).Max()-OrderRules.Npcs.Select(n=>counts[n]).Min()<1800,"all six balanced NPCs appear; no Willy fallback");
// Old contracts of every historical size and state survive byte-equivalent JSON roundtrips.
foreach(int quantity in Enumerable.Range(1,8))
{
 var oldDwarf=new MarketOrder("old-dwarf","Dwarf","(O)216",quantity,60,0,0,1,0,"barter","small",new(){new("(O)287",2),new("(O)382",5)});
 var oldAccepted=oldDwarf with{Id="accepted",Actor=5,Accepted=2,State="legacy-active"};
 var oldPending=oldDwarf with{Id="pending",Actor=5,Accepted=2,State="pending"};
 var oldCompleted=oldDwarf with{Id="completed",Actor=5,Accepted=2,Completed=3,State="completed"};
 var migrationBook=new MarketBook{Available=new(){oldDwarf},Active=new(){oldAccepted,oldPending},Completed=new(){oldCompleted}};
 migrationBook.Migrated["DwarfFoodCap/1.1.14/old-dwarf"]="historical audit, retained verbatim";
 string migrationRaw=migrationBook.Save();
 Check(MarketBook.Load(migrationRaw).Save()==migrationRaw,"all saved Dwarf sizes/rewards/states and migration audit survive unchanged reload");
}
var policyReport=R7PolicyChecks.Run(Check);
var missing=new MarketBook();for(int d=0;d<100;d++){OrderMarket.GenerateDay(missing,Array.Empty<MarketCandidate>(),1,d,1);Check(missing.Available.Count==0,"invalid pools never fallback");}Check(missing.GenerationFailures.Count<=3,"bounded failed-template reasons persist");
var book=new MarketBook();OrderMarket.GenerateDay(book,pool,55,9,1);Check(book.Available.Count is 1 or 2,"first board generates one or two with no known-item qualification");string raw=book.Save();OrderMarket.GenerateDay(book,pool,55,9,1);Check(book.Save()==raw,"same day no reroll");
var reloaded=MarketBook.Load(raw);OrderMarket.GenerateDay(reloaded,pool,55,9,1);Check(raw==reloaded.Save(),"serialized reload no reroll");
var one=book.Available[0];OrderMarket.Commit(book,one,1,9,true);OrderMarket.GenerateDay(book,pool,55,9,1);Check(!book.Available.Any(c=>c.Id==one.Id)&&book.Available.Count==reloaded.Available.Count-1,"reject permanent and no same-day refill");
var daily=new Dictionary<int,int>{{1,0},{2,0}};for(int d=0;d<10000;d++){var b=new MarketBook();daily[OrderMarket.GenerateDay(b,pool,19,d,1)]++;}Check(Math.Abs(daily[1]-5000)<300,"daily one/two balanced 50 percent");
MarketOrder Example(int n)=>new("legacy-"+n,"Pierre","(O)24",20,35,125,875,1,0,"legacy","normal",new(),LegacyDue:6);
foreach(int size in new[]{39,40,45}){var b=new MarketBook{Available=Enumerable.Range(0,size).Select(Example).ToList()};OrderMarket.GenerateDay(b,pool,17,30,2);Check(b.Available.Count==Math.Max(size,40),"capacity 39/40/legacy-overflow");Check(b.Available.Take(size).Select(x=>x.Id).SequenceEqual(Enumerable.Range(0,size).Select(n=>"legacy-"+n)),"all old expired offers retained");Check(MarketBook.Load(b.Save()).Available.Count==b.Available.Count,"overflow migration persists");}
var legacy=new Contract("old","Gus","(O)348",10,400,150,6000,1,3,"normal",true,2,2);var migrated=OrderMarket.Migrate(legacy,42,"completed");Check(migrated.Id==legacy.Id&&migrated.Gold==legacy.Reward&&migrated.LegacyDue==legacy.Due&&migrated.Actor==42&&migrated.Accepted==2&&migrated.Completed==2,"legacy exact contract and paid status preserved");
Reject(()=>MarketBook.Load("{}"),"incomplete save rejected");Reject(()=>MarketBook.Load(raw.Replace("\"Schema\":2","\"Schema\":9")),"future schema preserved");
var lifetime=new List<object>();foreach(int years in new[]{1,3,5})
{
 var b=new MarketBook();var ids=new HashSet<string>();long paid=0;int largest=0;
 for(int day=0;day<112*years;day++)
 {
  OrderMarket.GenerateDay(b,pool,888,day,day/112+1);
  foreach(var c in b.Available.ToArray()){Check(ids.Add(c.Id),"long simulation no ID reuse or rejected revival");OrderMarket.Commit(b,c,1,day,day%3==0);}
  foreach(var c in b.Active.ToArray()){paid+=c.Gold;b.Active.Remove(c);b.Completed.Insert(0,c with{State="completed",Completed=day});}OrderMarket.Trim(b);
  b=MarketBook.Load(b.Save());string prior=b.Save();OrderMarket.GenerateDay(b,pool,888,day,1);Check(prior==b.Save(),"long simulation reload stable");largest=Math.Max(largest,b.Save().Length);
 }
 Check(b.Completed.Count<=25&&b.Active.Count==0&&b.Available.Count==0&&b.GenerationFailures.Count==0,"bounded useful history for five years");lifetime.Add(new{years,unique_ids=ids.Count,paid,largest_saved_bytes=largest});
}
for(int fault=0;fault<4;fault++)
{
 int goods=20,reward=0,ledger=0,state=0;var steps=new List<AtomicStep>{new(()=>goods=0,()=>goods=20),new(()=>reward=1,()=>reward=0),new(()=>ledger=80,()=>ledger=0),new(()=>state=1,()=>state=0)};
 var hit=steps[fault];steps[fault]=new(()=>{hit.Apply();throw new Exception("injected");},hit.Undo);Reject(()=>AtomicChange.Execute(()=>true,steps),"transaction injected failure");Check(goods==20&&reward==0&&ledger==0&&state==0,"all transaction phases roll back");
}
foreach(var (w,h) in new[]{(640,360),(800,600),(1280,720),(1920,1080),(2560,1440)}){var l=new OrdersLayout(w,h);foreach(var rect in new[]{l.Action,l.Reject,l.Close,l.Previous,l.Next}.Concat(Enumerable.Range(0,3).Select(l.Tab)).Concat(Enumerable.Range(0,4).Select(l.Row))){var s=l.Screen(rect);Check(s.X>=0&&s.Y>=0&&s.X+s.W<=w&&s.Y+s.H<=h,"UI scaled controls in bounds");}}
var en=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(args[0],"default.json")))!;var zh=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(args[0],"zh.json")))!;foreach(var key in en.Keys.Where(k=>k.StartsWith("orders.")))Check(zh.ContainsKey(key)&&zh[key].Length>0,"bilingual keys complete");
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks,scenarios=names,policyReport,probability=new{morris_policy=OrderMarket.MorrisChance,save_seed=928734,day=12,year=2,present="rolling last 40 successes; initially empty",candidates="synthetic one candidate per NPC, price 50, alternating band, Morris quality=true",attempts=100000,successful=100000-failures,failures,counts,special,conditional=special/(double)counts["Morris"],overall=special/100000.0},daily,lifetime,scope="production pure policy/serializer/transaction coordinator; synthetic candidate data and settlement driver",live_game=false,multiplayer=false}));

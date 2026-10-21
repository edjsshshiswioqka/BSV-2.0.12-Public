using System.Text.Json;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("OrdersMarketNativeChecks")]
namespace BetterStardewValley.Orders;

// BSV Orders 2.0 policy. Pure, deterministic and shared by runtime and regression tests.
internal sealed record MarketReward(string Id,int Count);
internal sealed record MarketOrder(string Id,string Npc,string Item,int Quantity,int ReferencePrice,int Percent,int Gold,
 int Created,int MinimumQuality,string Kind,string Size,List<MarketReward> Rewards,long Actor=0,int Accepted=-1,int Completed=-1,
 string State="available",int LegacyDue=-1);
internal sealed record MarketCandidate(string Id,string Npc,int Price,int Band=0,bool Quality=false);
internal sealed record MarketIntent(string Id,long Sequence,long Actor,string Action,string Order,string Location,int X,int Y,string Inventory);
internal sealed record MarketReceipt(string Id,long Sequence,bool Success,string Message,string Inventory="");
internal sealed class MarketBook
{
 public int Schema {get;set;}=2;
 public long Revision {get;set;}
 public int LastGeneratedAbsoluteDay {get;set;}=-1;
 public long Sequence {get;set;}
 public List<MarketOrder> Available {get;set;}=new();
 public List<MarketOrder> Active {get;set;}=new();
 public List<MarketOrder> Completed {get;set;}=new();
 public Dictionary<long,MarketReceipt> Receipts {get;set;}=new();
 public Dictionary<string,string> Migrated {get;set;}=new(StringComparer.Ordinal);
 public Dictionary<string,long> GenerationFailures {get;set;}=new(StringComparer.Ordinal);
 public int SettlementDay {get;set;}=-1;
 public Dictionary<long,int> SettlementTotals {get;set;}=new();
 public bool SettlementSeparate {get;set;}
 public string Save()=>JsonSerializer.Serialize(this);
 public MarketBook Copy()=>Load(Save());
 public static MarketBook Load(string raw)
 {
  if(raw.Length>8000000)throw new InvalidOperationException("Orders 2.0 record too large; preserved.");
  using var doc=JsonDocument.Parse(raw);
  foreach(string k in new[]{"Schema","Revision","Sequence","LastGeneratedAbsoluteDay","Available","Active","Completed","Receipts","Migrated","GenerationFailures","SettlementTotals"})
   if(!doc.RootElement.TryGetProperty(k,out _))throw new InvalidOperationException("Incomplete Orders 2.0 record; preserved.");
  var b=JsonSerializer.Deserialize<MarketBook>(raw)??throw new InvalidOperationException("Missing orders data.");
  if(b.Schema!=2||b.Revision<0||b.Sequence<0||b.LastGeneratedAbsoluteDay< -1||b.Available==null||b.Active==null||b.Completed==null||b.Receipts==null||b.Migrated==null||b.GenerationFailures==null||b.SettlementTotals==null
   ||b.Available.Count>10000||b.Active.Count>10000||b.Completed.Count>10000||b.Receipts.Count>256||b.GenerationFailures.Count>32
   ||b.Available.Concat(b.Active).Concat(b.Completed).Any(o=>!OrderMarket.Valid(o))
   ||b.Available.Concat(b.Active).Concat(b.Completed).GroupBy(o=>o.Id).Any(g=>g.Count()!=1)
   ||b.Available.Any(o=>o.State!="available")||b.Active.Any(o=>o.State is not("pending" or "legacy-active"))||b.Completed.Any(o=>o.State!="completed")
   ||b.Receipts.Values.Any(r=>r==null||r.Sequence<1||!Guid.TryParseExact(r.Id,"N",out _))||b.SettlementTotals.Values.Any(v=>v<0))
   throw new InvalidOperationException("Invalid Orders 2.0 record; preserved.");
  return b;
 }
}
internal static class OrderMarket
{
 internal const string Key=OrderRules.Prefix+"Market.v2",IntentKey=OrderRules.Prefix+"Intent.v2",SequenceKey=OrderRules.Prefix+"Sequence.v2";
 internal const int Capacity=40;
 // Formal policy. The unapproved 5.5% proposal changes this constant only.
 internal const double MorrisChance=.05, DwarfChance=1.0/35, SpecialChance=.25;
 internal sealed record RewardOption(string Id,int Weight,int ReplacementValue);
 internal static readonly string[] Npcs={"Pierre","Willy","Robin","Gus","Marnie","Clint","Morris","Dwarf"};
 internal static readonly RewardOption[] DwarfRewards={new("(O)286",24,100),new("(O)287",18,300),new("(O)288",8,1000),new("(O)378",15,15),new("(O)380",10,30),new("(O)384",5,50),new("(O)382",10,50),new("(O)768",5,40),new("(O)769",5,40)};
 internal static bool Valid(MarketOrder o)=>o!=null&&!string.IsNullOrWhiteSpace(o.Id)&&o.Id.Length<=200&&Npcs.Contains(o.Npc)
  &&o.Item?.StartsWith("(O)",StringComparison.Ordinal)==true&&o.Quantity is >0 and <=250
  &&o.ReferencePrice>0&&o.ReferencePrice<=QualityReference(10000,o.MinimumQuality)
  &&o.Gold is >=0 and <=10000000&&o.Percent is >=0 and <=180&&o.MinimumQuality is 0 or 1 or 2&&o.Created>=0
  &&o.Rewards!=null&&o.Rewards.Count<=4&&o.Rewards.All(r=>r!=null&&r.Count is >0 and <=999&&(r.Id=="(BC)272"||DwarfRewards.Any(d=>d.Id==r.Id)))
  &&o.Kind is "standard" or "premium" or "special" or "barter" or "legacy"
  &&o.State is "available" or "pending" or "completed" or "legacy-active";
 internal static ulong Next(ref ulong state)
 {state+=0x9E3779B97F4A7C15UL;ulong z=state;z=(z^(z>>30))*0xBF58476D1CE4E5B9UL;z=(z^(z>>27))*0x94D049BB133111EBUL;return z^(z>>31);}
 internal static double Unit(ref ulong state)=>(Next(ref state)>>11)*(1.0/9007199254740992.0);
 internal static int Pick(ref ulong state,int count)=>(int)(Unit(ref state)*count);
 internal static ulong Seed(ulong save,int day,long sequence)=>OrderRules.Hash(FormattableString.Invariant($"BSV/Orders2/{save}/{day}/{sequence}"));
 // One shared interval draw preserves Dwarf's unconditional 1/35 probability.
 internal static string RareNpc(double r)=>r<MorrisChance?"Morris":r<MorrisChance+DwarfChance?"Dwarf":"";
 internal static (string Npc,bool Special) Draw(ref ulong random,IReadOnlyList<MarketOrder> present,IReadOnlyList<MarketCandidate> pool)
 {
  string rare=RareNpc(Unit(ref random));
  if(rare=="Morris")return (rare,Unit(ref random)<SpecialChance);
  if(rare=="Dwarf")return (rare,false);
  var choices=OrderRules.Npcs.Where(n=>pool.Any(c=>c.Npc==n)).Select(n=>(Npc:n,Weight:1.0/(1+present.Count(o=>o.Npc==n)))).ToArray();
  if(choices.Length==0)return ("",false);
  double at=Unit(ref random)*choices.Sum(c=>c.Weight);
  foreach(var choice in choices){at-=choice.Weight;if(at<0)return (choice.Npc,false);}
  return (choices[^1].Npc,false);
 }
 internal static int QualityReference(int price,int q)=>checked(price*(q==2?150:q==1?125:100)/100);
 internal static MarketOrder? Generate(ulong save,int day,long sequence,int year,IReadOnlyList<MarketOrder> present,IReadOnlyList<MarketCandidate> pool,out string failure)
 {
  ulong rng=Seed(save,day,sequence);var draw=Draw(ref rng,present,pool);failure="";
  var choices=pool.Where(c=>c.Npc==draw.Npc&&c.Price is >0 and <=10000).OrderBy(c=>c.Id,StringComparer.Ordinal).ToArray();
  if(choices.Length==0){failure=(draw.Npc==""?"Regular":draw.Npc)+"/no-valid-template";return null;}
  var c=choices[Pick(ref rng,choices.Length)];int size=Pick(ref rng,year>=2?10:8);string label=size<3?"small":size<8?"normal":"bulk";
  int quantity=label switch{"small"=>2+Pick(ref rng,4),"normal"=>6+Pick(ref rng,7),_=>18+Pick(ref rng,13)};
  if(c.Band==1)quantity*=5;
  quantity=Math.Clamp(quantity,1,Math.Min(250,Math.Max(1,(year>=2?25000:8000)/c.Price)));
  int quality=draw.Npc=="Morris"&&c.Quality?(Pick(ref rng,2)==0?1:2):0;
  int price=QualityReference(c.Price,quality),percent=115+Pick(ref rng,21);string kind="standard";var rewards=new List<MarketReward>();
  if(draw.Npc=="Morris") {kind=draw.Special?"special":"premium";percent=draw.Special?80:180;if(draw.Special)rewards.Add(new("(BC)272",1));}
  if(draw.Npc=="Dwarf")
  {
   kind="barter";percent=0;quantity=1+Pick(ref rng,4);label="small";
   rewards.Add(DwarfReward(ref rng));
  }
  return new(FormattableString.Invariant($"bsv2-{save:x}-{day}-{sequence}"),draw.Npc,c.Id,quantity,price,percent,OrderRules.Reward(price,quantity,percent),day,quality,kind,label,rewards);
 }
 // New quotes only: one reward type, independently drawn count 1–5. Fixed 50% bombs;
 // replacement values document the economy, not stack sizes or a food-price cap.
 internal static double[] DwarfProbabilities()=>DwarfRewards.Select(r=>r.Weight/100.0).ToArray();
 internal static MarketReward DwarfReward(ref ulong rng)
 {
  var selected=DwarfRewardAt(Pick(ref rng,100));
  return selected with{Count=1+Pick(ref rng,5)};
 }
 internal static MarketReward DwarfRewardAt(int ticket)
 {
  if(ticket is <0 or >=100)throw new ArgumentOutOfRangeException(nameof(ticket));
  foreach(var option in DwarfRewards){ticket-=option.Weight;if(ticket<0)return new(option.Id,1);}
  throw new InvalidOperationException("Dwarf reward weights must total 100.");
 }
 // The DwarfFoodCap/1.1.14 migration is retired. Saved quotes and its audit markers
 // remain immutable, including old quantities above the new generation limit.
 internal static int GenerateDay(MarketBook book,IReadOnlyList<MarketCandidate> pool,ulong save,int day,int year)
 {
  if(day<=book.LastGeneratedAbsoluteDay)return 0;
  ulong rng=Seed(save,day,0);int attempts=Math.Min(Math.Max(0,Capacity-book.Available.Count),1+Pick(ref rng,2)),added=0;
  for(int i=0;i<attempts;i++)
  {
   var order=Generate(save,day,checked(++book.Sequence),year,book.Available,pool,out string fail);
   if(order==null){book.GenerationFailures[fail]=book.GenerationFailures.GetValueOrDefault(fail)+1;continue;} // one bounded attempt; no fallback NPC or future backlog
   book.Available.Add(order);added++;
  }
  book.LastGeneratedAbsoluteDay=day;book.Revision++;return added;
 }
 internal static MarketOrder? Find(MarketBook book,string id,long actor)=>book.Available.FirstOrDefault(o=>o.Id==id)??book.Active.FirstOrDefault(o=>o.Id==id&&o.State=="legacy-active"&&o.Actor==actor);
 internal static void Commit(MarketBook book,MarketOrder order,long actor,int day,bool reject)
 {
  if(Find(book,order.Id,actor)!=order)throw new InvalidOperationException("Contract no longer available.");
  book.Available.Remove(order);book.Active.Remove(order);
  if(!reject)
  {
   var next=order with{Actor=actor,Accepted=order.Accepted<0?day:order.Accepted,State=order.Gold>0?"pending":"completed",Completed=order.Gold>0?-1:day};
   if(next.Gold>0)book.Active.Add(next);else book.Completed.Insert(0,next);
   Trim(book);
  }
  book.Revision++;
 }
 internal static void Trim(MarketBook book)
 {
  // Preserve the previous per-farmer 25-record policy in the shared market.
  var keep=book.Completed.GroupBy(o=>o.Actor).SelectMany(g=>g.OrderByDescending(o=>o.Completed).ThenByDescending(o=>o.Id,StringComparer.Ordinal).Take(25)).ToHashSet();
  book.Completed.RemoveAll(o=>!keep.Contains(o));
 }
 internal static MarketOrder Migrate(Contract c,long actor,string state)=>new(c.Id,c.Npc,c.Item,c.Quantity,c.BasePrice,c.Premium,c.Reward,c.Created,0,"legacy",c.Size,new(),state=="available"?0:actor,c.Accepted,c.Completed,state,c.Due);
}

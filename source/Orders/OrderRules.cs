using System.Text.Json;
namespace BetterStardewValley.Orders;

// A — legacy v1 DTO/validation retained only for migration and escrow recovery. New policy is OrderMarket.
internal sealed record Contract(string Id,string Npc,string Item,int Quantity,int BasePrice,int Premium,int Reward,int Created,int Due,string Size,bool Urgent,int Accepted=-1,int Completed=-1);
internal sealed record Goods(string Xml,string Id,int Quality,int Count);
internal sealed record OrderRequest(string Id,long Sequence,long Actor,string Action,string Contract,string Location,int X,int Y,int Day,List<Goods> Goods);
internal sealed record OrderReceipt(string Id,long Sequence,bool Accepted,string Message);
internal sealed class OrderBook
{
 public int Schema {get;set;}=1;
 public int LastRefresh {get;set;}=-1;
 public bool Initialized {get;set;}
 public long LastSequence {get;set;}
 public OrderReceipt? Receipt {get;set;}
 public HashSet<string> Known {get;set;}=new(StringComparer.Ordinal);
 public List<Contract> Available {get;set;}=new();
 public List<Contract> Active {get;set;}=new();
 public List<Contract> Completed {get;set;}=new();
 public string Save()=>JsonSerializer.Serialize(this);
 public OrderBook Copy()=>Load(Save());
 public static OrderBook Load(string raw,bool allowLargeAvailable=false)
 {
  if(raw.Length>2000000)throw new InvalidOperationException("Oversized orders record; preserved.");
  using var doc=JsonDocument.Parse(raw);
  foreach(string field in new[]{"Schema","LastRefresh","Initialized","LastSequence","Receipt","Known","Available","Active","Completed"})
   if(doc.RootElement.ValueKind!=JsonValueKind.Object||!doc.RootElement.TryGetProperty(field,out _))throw new InvalidOperationException("Incomplete orders record; preserved.");
  var b=JsonSerializer.Deserialize<OrderBook>(raw)??throw new InvalidOperationException("Missing orders record.");
  if(b.Schema!=1||b.LastRefresh < -1||b.LastSequence<0||b.Known==null||b.Known.Count>4096||b.Known.Any(k=>string.IsNullOrEmpty(k)||!k.StartsWith("(O)",StringComparison.Ordinal))||b.Available==null||b.Active==null||b.Completed==null
   ||b.Available.Count>(allowLargeAvailable?10000:6)||b.Active.Count>64||b.Completed.Count>25||b.Available.Concat(b.Active).Concat(b.Completed).Any(o=>!OrderRules.Valid(o))
   ||b.Available.Concat(b.Active).Concat(b.Completed).GroupBy(o=>o.Id).Any(g=>g.Count()!=1)
   ||b.Available.Any(o=>o.Accepted!=-1||o.Completed!=-1)||b.Active.Any(o=>o.Accepted<0||o.Completed!=-1)||b.Completed.Any(o=>o.Completed<0)
   ||(b.LastSequence==0)!=(b.Receipt==null)||b.Receipt!=null&&(b.Receipt.Sequence!=b.LastSequence||!Guid.TryParseExact(b.Receipt.Id,"N",out _)))throw new InvalidOperationException("Invalid orders record; preserved.");
  return b;
 }
}
internal static class OrderRules
{
 internal const string TerminalId="Eddie.BetterStardewValley_OrderTerminal",Prefix="Eddie.BetterStardewValley/Orders/";
 internal const string PendingKey=Prefix+"Pending.v1",SequenceKey=Prefix+"Sequence",SpawnKey=Prefix+"Terminal.v1";
 internal static readonly string[] Npcs={"Pierre","Willy","Robin","Gus","Marnie","Clint"};
 internal static bool Valid(Contract o)=>o!=null&&!string.IsNullOrWhiteSpace(o.Id)&&Npcs.Contains(o.Npc)&&!string.IsNullOrEmpty(o.Item)&&o.Item.StartsWith("(O)",StringComparison.Ordinal)
  &&o.Quantity is >0 and <=250&&o.BasePrice is >0 and <=10000&&o.Created>=0&&o.Due>o.Created&&o.Due-o.Created<=8
  &&o.Premium==(o.Urgent?150:125)&&o.Reward==Reward(o.BasePrice,o.Quantity,o.Premium)&&o.Size is "small" or "normal" or "bulk";
 internal static int Reward(int price,int quantity,int premium)=>checked((int)(((long)price*quantity*premium+99)/100));
 // Fixed FNV-1a seed, rather than runtime-dependent string.GetHashCode or shared game RNG.
 internal static ulong Hash(string text){ulong n=14695981039346656037UL;foreach(char c in text){n^=c;n*=1099511628211UL;}return n;}
 internal static IReadOnlyList<(int Slot,int Count)>? Spend(IEnumerable<(int Slot,int Quality,int Count)> stock,int required)
 {
  var result=new List<(int,int)>();foreach(var item in stock.OrderBy(i=>i.Quality).ThenBy(i=>i.Slot))
  {int take=Math.Min(required,item.Count);if(take<=0)continue;result.Add((item.Slot,take));required-=take;if(required==0)return result;}
  return null;
 }
}

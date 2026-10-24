using StardewValley;
using StardewValley.Inventories;
using StardewValley.Network;
using SObject = StardewValley.Object;
namespace BetterStardewValley.Orders;

// The 48-slot order box that belongs to one placed Order Terminal. It is a native team "global inventory"
// (saved and synced by the game, with its own mutex), keyed by the terminal's entity id.
// Goods for a confirmed order stay in the box with a reservation tag until the host delivers them at night.
internal static class OrderBox
{
 internal const int Capacity=48,Columns=12,Rows=4;
 internal const string Prefix="Eddie.BetterStardewValley/OrderBox/";
 internal const string ReserveKey=OrderRules.Prefix+"Reserved.v1";
 internal static string Key(string terminalId)=>Prefix+terminalId;
 internal static Inventory Items(string key)=>Game1.player.team.GetOrCreateGlobalInventory(key);
 internal static NetMutex Mutex(string key)=>Game1.player.team.GetOrCreateGlobalInventoryMutex(key);
 internal static bool Exists(string key)=>Game1.player.team.globalInventories.ContainsKey(key);
 internal static bool IsEmpty(string key)=>!Exists(key)||Items(key).All(i=>i==null);
 internal static string? Tag(Item? item)=>item!=null&&item.modData.TryGetValue(ReserveKey,out var tag)?tag:null;

 /// <summary>Pending orders delivered from this box tonight.</summary>
 internal static IEnumerable<MarketOrder> Pending(MarketBook book,string key)=>book.Active.Where(o=>o.State=="pending"&&o.Box==key&&o.Reservation!="");
 /// <summary>A slot is locked only while a pending order still holds its tag; stale tags mean nothing.</summary>
 internal static bool Locked(Item? item,MarketBook book,string key)=>Tag(item) is {} tag&&Pending(book,key).Any(o=>o.Reservation==tag);
 internal static MarketOrder? LockedBy(Item? item,MarketBook book,string key)=>Tag(item) is {} tag?Pending(book,key).FirstOrDefault(o=>o.Reservation==tag):null;

 /// <summary>Goods the order accepts: same item, enough quality, no foreign state (only our own reservation tag).</summary>
 internal static bool Goods(Item? item,MarketOrder order)=>OrdersInventory.Eligible(item,order.Item)&&item!.Quality>=order.MinimumQuality
  &&item.modData.Keys.All(k=>k==ReserveKey)&&!item.specialItem&&!item.isLostItem&&!item.IsRecipe&&item is SObject o
  &&string.IsNullOrEmpty(o.questId.Value)&&string.IsNullOrEmpty(o.orderData.Value);
 internal static int Free(IList<Item> box)=>Enumerable.Range(0,Capacity).Count(i=>i>=box.Count||box[i]==null);
 /// <summary>Empty slots kept free for item rewards that pending orders will put into the box tonight.</summary>
 internal static int RewardSlotsNeeded(MarketBook book,IList<Item> box,string key,MarketOrder? extra=null,int extraReserved=0)
 {
  int need=0;
  foreach(var o in Pending(book,key))need+=Math.Max(0,o.Rewards.Count-box.Count(i=>Tag(i)==o.Reservation));
  if(extra!=null)need+=Math.Max(0,extra.Rewards.Count-extraReserved);
  return need;
 }
 internal static void Pad(IList<Item> box){while(box.Count<Capacity)box.Add(null!);}
 /// <summary>Removes tags that no pending order uses any more (a cancelled or refused confirmation).</summary>
 internal static void StripStale(IList<Item> box,MarketBook book,string key)
 {
  foreach(var item in box)if(item!=null&&Tag(item)!=null&&!Locked(item,book,key))item.modData.Remove(ReserveKey);
 }
}

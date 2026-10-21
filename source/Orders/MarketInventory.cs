using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Inventories;
using BetterStardewValley.Transactions;
using SObject=StardewValley.Object;
namespace BetterStardewValley.Orders;

internal sealed record MarketChest(GameLocation Location,Vector2 Tile,Chest Chest);
internal sealed record MarketTake(MarketChest Source,int Slot,Item Original,int Count);
internal static class MarketInventory
{
 internal static bool Container(Chest c)=>c.GetType()==typeof(Chest)&&c.playerChest.Value&&!c.fridge.Value&&!c.giftbox.Value
  &&c.QualifiedItemId is "(BC)130" or "(BC)232" or "(BC)BigChest" or "(BC)BigStoneChest"
  &&c.SpecialChestType is Chest.SpecialChestTypes.None or Chest.SpecialChestTypes.BigChest
  &&c.GlobalInventoryId==null&&c.modData.Pairs.All(p=>p.Key==AfkFishing.AfkFishingService.ChestKey&&Guid.TryParseExact(p.Value,"N",out _))
  &&!c.questItem.Value&&c.heldObject.Value==null;
 internal static bool Goods(Item? item,MarketOrder order)=>OrdersInventory.Eligible(item,order.Item)&&item!.Quality>=order.MinimumQuality
  &&item.modData.Count()==0&&!item.specialItem&&!item.isLostItem&&!item.IsRecipe&&item is SObject o
  &&string.IsNullOrEmpty(o.questId.Value)&&string.IsNullOrEmpty(o.orderData.Value);
 internal static List<MarketChest> Chests()
 {
  var locations=new List<GameLocation>();Utility.ForEachLocation(l=>{if(TerminalEntity.FarmMap(l)||TerminalEntity.HomeMap(l))locations.Add(l);return true;});
  return locations.Distinct().OrderBy(l=>l.NameOrUniqueName,StringComparer.Ordinal).SelectMany(l=>l.objects.Pairs
   .Where(p=>p.Value is Chest c&&Container(c)).OrderBy(p=>p.Key.Y).ThenBy(p=>p.Key.X).Select(p=>new MarketChest(l,p.Key,(Chest)p.Value))).ToList();
 }
 internal static bool Live(MarketChest c)=>c.Location.objects.TryGetValue(c.Tile,out var found)&&ReferenceEquals(found,c.Chest)&&Container(c.Chest)
  &&(TerminalEntity.FarmMap(c.Location)||TerminalEntity.HomeMap(c.Location));
 internal static List<MarketTake>? Plan(IReadOnlyList<MarketChest> sources,MarketOrder order,long actor)
 {
  int left=order.Quantity;var result=new List<MarketTake>();
  var stock=sources.SelectMany(s=>s.Chest.GetItemsForPlayer(actor).Select((item,slot)=>(s,item,slot)))
   .Where(x=>Goods(x.item,order)).OrderBy(x=>x.item.Quality).ThenBy(x=>x.s.Location.NameOrUniqueName,StringComparer.Ordinal).ThenBy(x=>x.s.Tile.Y).ThenBy(x=>x.s.Tile.X).ThenBy(x=>x.slot);
  foreach(var x in stock){int take=Math.Min(left,x.item.Stack);result.Add(new(x.s,x.slot,x.item,take));left-=take;if(left==0)return result;}
  return null;
 }
 internal static int Count(MarketOrder order,long actor)=>(int)Math.Min(int.MaxValue,Chests().SelectMany(s=>s.Chest.GetItemsForPlayer(actor)).Where(i=>Goods(i,order)).Sum(i=>(long)i.Stack));
 internal static Item Copy(Item item){var c=item.getOne();c.Stack=item.Stack;return c;}
 internal static Item?[]? Rewards(Farmer who,IReadOnlyList<MarketReward> rewards)
 {
  var after=who.Items.Cast<Item?>().ToArray();int capacity=Math.Min(after.Length,who.MaxItems);
  foreach(var reward in rewards)
  {
   Item item=ItemRegistry.Create(reward.Id,reward.Count,allowNull:true);if(item==null||item.QualifiedItemId!=reward.Id)return null;
   int left=reward.Count;
   for(int n=0;n<capacity&&left>0;n++)
   {
    var old=after[n];if(old==null||!old.canStackWith(item))continue;
    int take=Math.Min(left,old.maximumStackSize()-old.Stack);if(take<=0)continue;
    after[n]=Copy(old);after[n]!.Stack+=take;left-=take;
   }
   for(int n=0;n<capacity&&left>0;n++)if(after[n]==null){after[n]=Copy(item);after[n]!.Stack=Math.Min(left,item.maximumStackSize());left-=after[n]!.Stack;}
   if(left!=0)return null;
  }
  return after;
 }
 internal static string Fingerprint(IEnumerable<Item?> items)
 {
  var b=new StringBuilder();foreach(var i in items)
  {
   if(i==null){b.Append("empty;");continue;}
   b.Append(i.GetType().FullName).Append('|').Append(i.QualifiedItemId).Append('|').Append(i.Stack).Append('|').Append(i.Quality);
   foreach(var p in i.modData.Pairs.OrderBy(p=>p.Key,StringComparer.Ordinal))b.Append('|').Append(p.Key.Length).Append(':').Append(p.Key).Append('=').Append(p.Value.Length).Append(':').Append(p.Value);
   if(i is SObject o)b.Append('|').Append(o.preservedParentSheetIndex.Value).Append('|').Append(o.orderData.Value).Append('|').Append(o.questItem.Value);
   b.Append(';');
  }
  return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(b.ToString())));
 }
 internal static void WithLocks(IReadOnlyList<MarketChest> sources,Action run)
 {
  // Native off-map mutex owners are released after Update's callback. Complete nested work synchronously inside that callback.
  // Never borrow a lock already owned by a player's chest UI or another mod.
  if(sources.Count>128)throw new InvalidOperationException("More than 128 chests needed for one order.");
  void Acquire(int n)
  {
   if(n==sources.Count){run();return;}var c=sources[n];var mutex=c.Chest.GetMutex();mutex.Update(c.Location);
   if(!Live(c)||mutex.IsLocked())return;
   bool open=true;Exception? failure=null;
   mutex.RequestLock(()=>{try{if(open&&mutex.IsLockHeld())Acquire(n+1);}catch(Exception ex){failure=ex;}finally{if(mutex.IsLockHeld())mutex.ReleaseLock();}});
   try{mutex.Update(c.Location);}finally{open=false;}
   if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
  }
  Acquire(0);
 }
 internal static List<AtomicStep> Steps(IReadOnlyList<MarketTake> plan,Farmer who,Item?[] after)
 {
  var steps=new List<AtomicStep>();
  foreach(var t in plan)
  {
   IInventory items=t.Source.Chest.GetItemsForPlayer(who.UniqueMultiplayerID);Item? replacement=t.Count==t.Original.Stack?null:Copy(t.Original);
   if(replacement!=null)replacement.Stack-=t.Count;
   steps.Add(new(()=>items[t.Slot]=replacement!,()=>items[t.Slot]=t.Original));
  }
  for(int n=0;n<after.Length;n++)
  {
   int slot=n;var old=who.Items[slot];if(ReferenceEquals(old,after[slot]))continue;
   steps.Add(new(()=>who.Items[slot]=after[slot]!,()=>who.Items[slot]=old));
  }
  return steps;
 }
}

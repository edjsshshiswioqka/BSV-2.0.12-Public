using System.Xml;
using System.Xml.Serialization;
using StardewValley;
using SObject=StardewValley.Object;
namespace BetterStardewValley.Orders;

// A — inventory changes happen only on its owning client, with a persistent refundable escrow.
internal static class OrdersInventory
{
 private static readonly Lazy<XmlSerializer> Serializer=new(()=>new XmlSerializer(typeof(SObject)));
 internal static bool Eligible(Item? item,string id)=>item?.GetType()==typeof(SObject)&&item.QualifiedItemId==id&&item.Stack>0
  &&item is SObject o&&!o.questItem.Value&&o.heldObject.Value==null&&!o.bigCraftable.Value&&item.Quality is 0 or 1 or 2 or 4;
 internal static int Count(Farmer who,string id)=>(int)Math.Min(int.MaxValue,who.Items.Where(i=>Eligible(i,id)).Sum(i=>(long)i.Stack));
 private static Item Clone(Item i){var copy=i.getOne();copy.Stack=i.Stack;return copy;}
 internal static Goods Encode(Item item)
 {
  using var writer=new StringWriter(System.Globalization.CultureInfo.InvariantCulture);Serializer.Value.Serialize(writer,item);
  string xml=writer.ToString();if(xml.Length>262144)throw new InvalidOperationException("Item escrow exceeds size limit.");
  return new(xml,item.QualifiedItemId,item.Quality,item.Stack);
 }
 internal static Item Decode(Goods record)
 {
  if(record.Xml.Length>262144)throw new InvalidOperationException("Invalid escrow size.");
  using var reader=XmlReader.Create(new StringReader(record.Xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=262144});
  var item=Serializer.Value.Deserialize(reader) as SObject;
  if(!Eligible(item,record.Id)||item!.Stack!=record.Count||item.Quality!=record.Quality)throw new InvalidOperationException("Escrow item does not match its receipt.");
  return item;
 }
 internal static (Item?[] After,List<Goods> Goods)? Prepare(Farmer who,Contract contract)
 {
  var spend=OrderRules.Spend(who.Items.Select((i,n)=>(Item:i,Slot:n)).Where(x=>Eligible(x.Item,contract.Item)).Select(x=>(x.Slot,x.Item.Quality,x.Item.Stack)),contract.Quantity);
  if(spend==null)return null;var after=who.Items.Cast<Item?>().ToArray();var goods=new List<Goods>();
  foreach(var (slot,count) in spend)
  {
   var original=after[slot]!;var removed=Clone(original);removed.Stack=count;var record=Encode(removed);Decode(record);goods.Add(record);
   if(original.Stack==count)after[slot]=null;else{after[slot]=Clone(original);after[slot]!.Stack-=count;}
  }return (after,goods);
 }
 internal static Item?[]? Refund(Farmer who,IReadOnlyList<Goods> goods)
 {
  var after=who.Items.Cast<Item?>().ToArray();foreach(var g in goods)
  {
   var item=Decode(g);int left=item.Stack;
   for(int n=0;n<after.Length&&left>0;n++)
   {
    if(after[n] is not {} slot||!slot.canStackWith(item))continue;
    int put=Math.Min(left,slot.maximumStackSize()-slot.Stack);if(put<=0)continue;after[n]=Clone(slot);after[n]!.Stack+=put;left-=put;
   }
   for(int n=0;n<after.Length&&left>0;n++)if(after[n]==null){var copy=Clone(item);copy.Stack=Math.Min(left,item.maximumStackSize());after[n]=copy;left-=copy.Stack;}
   if(left>0)return null;
  }return after;
 }
 internal static void Set(Farmer who,IReadOnlyList<Item?> items)
 {if(who.Items.Count!=items.Count)throw new InvalidOperationException("Inventory changed during transaction.");for(int n=0;n<items.Count;n++)who.Items[n]=items[n]!;}
}

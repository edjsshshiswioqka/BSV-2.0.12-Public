using System.Reflection;using System.Runtime.Serialization;using System.Xml.Serialization;using BetterStardewValley.Orders;using StardewValley;using StardewValley.Menus;using StardewValley.BellsAndWhistles;using HarmonyLib;
partial class Program
{
 static void SaveChecks(Harmony h)
 {
  var c=Offer("special");Fresh(c);var chest=Chest(8,5);chest.Items.Add(Goods(20));var request=Intent(c);Call("Process",request);
  var nativeSave=new SaveGame{player=Host,farmhands=new(){Guest},locations=new(){Farm}};
  string xml;using(var text=new StringWriter()){SaveGame.serializer.Serialize(text,nativeSave);xml=text.ToString();}
  string file=Path.Combine(Path.GetTempPath(),"bsv-orders2-boundary-"+Guid.NewGuid().ToString("N")+".xml");File.WriteAllText(file,xml,System.Text.Encoding.Unicode);
  SaveGame restored;using(var reader=File.OpenRead(file))restored=(SaveGame)SaveGame.serializer.Deserialize(reader)!;File.Delete(file);
  var savedFarm=restored.locations.OfType<Farm>().Single();var savedBook=MarketBook.Load(savedFarm.modData[OrderMarket.Key]);
  Check(savedBook.Active.Single().State=="pending"&&savedBook.Receipts[Host.UniqueMultiplayerID].Success,"native SaveGame XML contains committed unpaid ledger and receipt together");
  Check(restored.player.Items.Count(i=>i?.QualifiedItemId=="(BC)272")==1&&savedFarm.objects.Pairs.Where(p=>p.Value is StardewValley.Objects.Chest).SelectMany(p=>((StardewValley.Objects.Chest)p.Value).Items).All(i=>i==null||i.QualifiedItemId!="(O)24"),"real disk/XML roundtrip contains reward and reduced chest inventory together");
  Call("Settle");using(var text=new StringWriter()){SaveGame.serializer.Serialize(text,new SaveGame{player=Host,locations=new(){Farm}});xml=text.ToString();}
  using(var reader=new StringReader(xml))restored=(SaveGame)SaveGame.serializer.Deserialize(reader)!;
  savedBook=MarketBook.Load(restored.locations.OfType<Farm>().Single().modData[OrderMarket.Key]);Check(savedBook.Active.Count==0&&savedBook.Completed.Single().Id==c.Id,"native next-day XML stores paid state; legacy ledger is not resurrected");
  // Execute actual native shipping parser and BSV postfix. No graphics device or fake sold items.
  h.Patch(typeof(Game1).GetMethod("setRichPresence",Any,null,new[]{typeof(string),typeof(object)},null)!,prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
  var menu=(ShippingMenu)FormatterServices.GetUninitializedObject(typeof(ShippingMenu));
  foreach(string field in new[]{"categoryTotals","categoryDials","categoryItems","itemValues","singleItemValues"}){var f=typeof(ShippingMenu).GetField(field,Any)!;f.SetValue(menu,Activator.CreateInstance(f.FieldType));}
  uint shipped=Host.stats.ItemsShipped;int money=Host.team.GetIndividualMoney(Host);menu.parseItems(new List<Item>());
  var totals=(List<int>)typeof(ShippingMenu).GetField("categoryTotals",Any)!.GetValue(menu)!;
  Check(totals[5]==c.Gold&&totals.Take(5).All(n=>n==0),"native income summary total includes separate Orders amount");
  Check(Host.stats.ItemsShipped==shipped&&Host.team.GetIndividualMoney(Host)==money,"income rendering does not invent shipping progress or pay twice");
  
 }
}

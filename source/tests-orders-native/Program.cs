// Real game Object, Inventory, XML and item-copy methods; no running game/SMAPI or GPU.
using BetterStardewValley.Orders;using StardewValley;using StardewValley.GameData.Objects;using System.Reflection;using System.Runtime.Serialization;using System.Text.Json;using SObject=StardewValley.Object;
int checks=0;var scenarios=new HashSet<string>();void Check(bool ok,string reason){checks++;scenarios.Add(reason);if(!ok)throw new Exception(reason);}
void Reject(Action f,string reason){bool rejected=false;try{f();}catch{rejected=true;}Check(rejected,reason);}
ItemRegistry.AddTypeDefinition(new StardewValley.ItemTypeDefinitions.ObjectDataDefinition());
Game1.objectData=new Dictionary<string,ObjectData>();foreach(var id in new[]{"24","348","390"})Game1.objectData[id]=new(){Name="Test"+id,DisplayName="Test"+id,Description="Headless object metadata",Price=35,Type="Basic",Category=-75,Texture="Maps/springobjects",SpriteIndex=24};
Farmer Farmer(){var who=(Farmer)FormatterServices.GetUninitializedObject(typeof(Farmer));var field=typeof(Farmer).GetField("netItems")!;var inventory=new StardewValley.Inventories.Inventory();field.SetValue(who,Activator.CreateInstance(field.FieldType,inventory));for(int i=0;i<12;i++)who.Items.Add(null!);return who;}
SObject Item(int stack,int quality=0,string id="24"){var o=new SObject(id,stack){Quality=quality};o.modData["BSV-Test"]= "keep-中文";return o;}
var contract=new Contract("c","Pierre","(O)24",20,35,125,875,10,15,"normal",false,10);
foreach(int q in new[]{0,1,2,4})
{
 var o=Item(17,q);var encoded=OrdersInventory.Encode(o);var restored=(SObject)OrdersInventory.Decode(encoded);
 Check(restored.QualifiedItemId=="(O)24"&&restored.Stack==17&&restored.Quality==q&&restored.modData["BSV-Test"]=="keep-中文","real native XML preserves ID, quality, quantity and modData");
 Reject(()=>OrdersInventory.Decode(encoded with{Count=18}),"escrow mismatch fails closed");
}
var wine=Item(6,2,"348");wine.preserve.Value=SObject.PreserveType.Wine;wine.preservedParentSheetIndex.Value="398";wine.Price=900;wine.Name="Grape Wine";var wineCopy=(SObject)OrdersInventory.Decode(OrdersInventory.Encode(wine));Check(wineCopy.preservedParentSheetIndex.Value=="398"&&wineCopy.Price==900&&wineCopy.Name==wine.Name,"native flavored wine fields survive escrow");
var who=Farmer();who.Items[0]=Item(10,4);who.Items[1]=Item(8,2);who.Items[2]=Item(7,1);who.Items[3]=Item(9,0);var original=who.Items.Select(i=>i==null?null:OrdersInventory.Encode(i).Xml).ToArray();var plan=OrdersInventory.Prepare(who,contract)!.Value;
Check(original.SequenceEqual(who.Items.Select(i=>i==null?null:OrdersInventory.Encode(i).Xml)),"prepare leaves real inventory completely unchanged");Check(plan.Goods.Select(g=>g.Quality).SequenceEqual(new[]{0,1,2}),"real native inventory uses lowest quality first");OrdersInventory.Set(who,plan.After);Check(who.Items[0].Stack==10&&who.Items[1].Stack==4&&who.Items[2]==null&&who.Items[3]==null,"native inventory applies exact planned slot changes");
var refund=OrdersInventory.Refund(who,plan.Goods)!;Check(refund!=null,"native stack matching accepts exact refund");OrdersInventory.Set(who,refund);Check(OrdersInventory.Count(who,"(O)24")==34&&who.Items.Where(i=>i!=null).All(i=>i.modData["BSV-Test"]=="keep-中文"),"native refund conserves goods and metadata");
who=Farmer();who.Items[0]=Item(19);Check(OrdersInventory.Prepare(who,contract)==null&&who.Items[0].Stack==19,"real insufficient stock never mutates");
var goods=new List<Goods>{OrdersInventory.Encode(Item(20))};for(int n=0;n<who.Items.Count;n++)who.Items[n]=Item(999,0,"390");Check(OrdersInventory.Refund(who,goods)==null&&who.Items.All(i=>i.Stack==999),"real full bag refund is all-or-nothing");
var quest=Item(30);quest.questItem.Value=true;Check(!OrdersInventory.Eligible(quest,"(O)24"),"quest objects protected");
Reject(()=>OrdersInventory.Decode(new("<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///missing'>]><Object>&x;</Object>","(O)24",0,20)),"XML external entity input rejected");
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,assertions=checks,scenarios,real_game=false,real_multiplayer=false,real_game_classes=true}));

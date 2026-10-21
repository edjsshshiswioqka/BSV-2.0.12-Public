using BetterStardewValley.Orders;using StardewValley;using Microsoft.Xna.Framework;using Microsoft.Xna.Framework.Graphics;using Microsoft.Xna.Framework.Input;using System.Text.Json;
int checks=0;void Check(bool yes,string name){checks++;if(!yes)throw new Exception(name);}var service=new OrdersService();
MarketOrder C(int n)=>new("contract"+n,OrderMarket.Npcs[n%8],"(O)24",20,35,125,875,10,n%3,n%8==7?"barter":n%8==6?"special":"standard","normal",n%8==6?new(){new("(BC)272",1)}:n%8==7?new(){new("(O)287",2)}:new());
for(int n=0;n<40;n++)service.Book.Available.Add(C(n));service.Book.Active.Add(C(0) with{Id="pending",Actor=1,Accepted=10,State="pending"});service.Book.Completed.Add(C(1) with{Id="completed",Actor=1,Accepted=10,Completed=10,State="completed"});Game1.player.Count=20;
var menu=new OrdersMenu(service,"FarmHouse",0,0);var layout=new OrdersLayout(1120,760);
void Click(OrderRect r){var s=layout.Screen(r);menu.receiveLeftClick(s.X+s.W/2,s.Y+s.H/2);}
void Render(string locale,string name){service.Strings=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(args[0],locale+".json")))!;var batch=new SpriteBatch();menu.draw(batch);Check(batch.Commands.Count>70,"complete panel/portrait/button render");string raw=JsonSerializer.Serialize(batch.Commands,new JsonSerializerOptions{WriteIndented=true});Check(!raw.Contains("Due in")&&!raw.Contains("Days Remaining"),"no expiry UI");File.WriteAllText(Path.Combine(args[1],name+".json"),raw);}
Render("default","orders2-available-en");Click(layout.Row(1));Click(layout.Action);Check(service.Requests.Last()=="accept:contract1","selection accepts matching order");
Click(layout.Reject);Check(service.Requests.Count==1,"reject first click confirms without transaction");Render("zh","orders2-reject-zh");Click(layout.Close);Check(!menu.Closed&&service.Requests.Count==1,"confirmation cancel safe");Click(layout.Reject);Click(layout.Reject);Check(service.Requests.Last()=="reject:contract1","confirmed reject one request");
Click(layout.Tab(1));Click(layout.Action);Check(service.Requests.Count==2,"pending settlement cannot deliver or pay again");Render("default","orders2-active-en");
Click(layout.Tab(2));Click(layout.Action);Check(service.Requests.Count==2,"completed cannot act");Render("default","orders2-completed-en");
Click(layout.Tab(0));for(int n=0;n<5;n++)menu.receiveKeyPress(Keys.Down);menu.receiveKeyPress(Keys.Down);Render("default","orders2-special-en");menu.receiveKeyPress(Keys.Down);Render("zh","orders2-barter-zh");
Check(Game1.content.Loaded.Contains("Portraits/Morris")&&Game1.content.Loaded.Contains("Portraits/Dwarf"),"runtime native rare portrait keys");
for(int n=0;n<40;n++)menu.receiveScrollWheelAction(-1);Render("default","orders2-scroll-end");Click(layout.Row(3));Click(layout.Action);Check(service.Requests.Last()=="accept:contract39","40th row scroll/select/accept");
service.HasPending=true;Click(layout.Action);int count=service.Requests.Count;menu.receiveKeyPress(Keys.Escape);Check(!menu.Closed&&service.Requests.Count==count,"inventory input held while host transaction pending");service.HasPending=false;
int beforeLanguageRequests=service.Requests.Count;
foreach(string file in Directory.GetFiles(args[0],"*.json"))
foreach(var (w,h) in new[]{(640,360),(800,600),(1280,720),(1920,1080)}){
 string locale=Path.GetFileNameWithoutExtension(file);
 Game1.uiViewport=new(0,0,w,h);menu.gameWindowSizeChanged(new(),new());Render(locale,$"orders2-{locale}-{w}x{h}");
 Check(service.Requests.Count==beforeLanguageRequests&&!menu.Closed,"language/viewport rendering does not submit or close orders "+locale);
}
// Exercise the real detail renderer in every shipped locale, all final counts.
foreach(string file in Directory.GetFiles(args[0],"*.json"))foreach(int amount in Enumerable.Range(1,5))
{
 var displayed=new OrdersService();displayed.Strings=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(file))!;
 displayed.Book.Available.Add(new MarketOrder("five","Dwarf","(O)216",4,60,0,0,10,0,"barter","small",new(){new("(O)288",amount)}));
 Game1.uiViewport=new(0,0,1120,760);var detail=new OrdersMenu(displayed,"FarmHouse",0,0);var batch=new SpriteBatch();string before=displayed.Book.Save();
 detail.draw(batch);detail.draw(new SpriteBatch());
 var commands=JsonSerializer.SerializeToElement(batch.Commands);
 Check(commands.EnumerateArray().Any(x=>x.TryGetProperty("text",out var text)&&text.GetString()==OrderCatalog.ItemName("(O)288")+" × "+amount),"all 13 locales render actual Mega Bomb quantity "+amount);
 Check(displayed.Book.Save()==before&&displayed.Requests.Count==0,"menu redraw neither rerolls nor submits reward");
}
service.Reach=false;menu.update(new());Check(menu.Closed,"out of terminal reach closes UI");
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks,scope="actual production OrdersMenu renderer/input; recorder uses approximate fonts and placeholder native-asset responses",real_game=false}));

using BetterStardewValley;
using BetterStardewValley.Totems;
using BetterStardewValley.Orders;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Reflection;
using System.Text.Json;
using SObject=StardewValley.Object;
int checks=0;var scenarios=new HashSet<string>();
void Check(bool ok,string why){checks++;scenarios.Add(why);if(!ok)throw new Exception(why);}
var helper=new IModHelper();var monitor=new IMonitor();var service=new TotemUnlockService(helper,monitor,new());
var recipes=TotemUnlockRules.Recipes;
TotemUnlockState State()=>TotemUnlockState.Load(Game1.player.modData[TotemUnlockRules.StateKey]);
void Reset(int day=10,int farming=10)
{
 helper.Events.GameLoop.Reset();Game1.player=new();Game1.player.farmingLevel.Value=farming;Game1.Farm=new();Game1.Date.TotalDays=day;Context.IsWorldReady=true;Game1.eventUp=false;IntegrationHost.Saving=false;monitor.Lines.Clear();
 DataLoader.Data=new(){["seed-potato"]=new(){HarvestItemId="192"},["seed-strawberry"]=new(){HarvestItemId="400"},["seed-flower"]=new(){HarvestItemId="591"},["495"]=new(){HarvestItemId="16"}};
 Game1.objectData=new(){["192"]=new(){Category=-75},["400"]=new(){Category=-79},["591"]=new(){Category=-80},["348"]=new(){Category=-26},["custom-artisan"]=new(){Category=-26},["787"]=new(){Category=-16},["16"]=new(){Category=-81}};
}
void Day(){Game1.Date.TotalDays++;helper.Events.GameLoop.Day();}
SObject Item(string id,int count,int q=0)=>new(){QualifiedItemId="(O)"+id,Stack=count,Quality=q};
void Shipment(List<Item> items,bool real=true)
{
 if(real)Game1.player.displayedShippedItems=items;
 object?[] args={items,null};typeof(TotemUnlockService).GetMethod("ShippingBefore",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
 // Simulated successful native settlement; distinct from calling production postfix after an exception.
 if(real)foreach(var item in items)if(item is SObject o)Game1.player.basicShipped[o.ItemId]=Game1.player.basicShipped.GetValueOrDefault(o.ItemId)+o.Stack;
 typeof(TotemUnlockService).GetMethod("ShippingAfter",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new[]{args[1]});
}
string[] expected={"709 20 335 5 338 10 787 2 726 5","709 30 771 100 Moss 10 465 10 725 5","709 30 335 10 336 5 382 20 787 2 725 5","709 50 336 10 337 5 368 25 74 1"};
for(int i=0;i<4;i++){Check(recipes[i].Ingredients==expected[i],"exact author ingredients "+recipes[i].Kind);Check(recipes[i].Data("name").Split('/')[3]=="true"&&recipes[i].Data("name").Split('/')[4]=="null","native big-craftable recipe has no skill auto-unlock "+i);}
Check(recipes.Select(r=>r.Farming).SequenceEqual(new[]{6,8,9,10}),"exact ordered base farming requirements");
foreach(var r in recipes)for(int farming=0;farming<=10;farming++)for(int n=0;n<=105;n++)
{
 var s=new TotemUnlockState{Battery=n>0,Crops=Math.Min(n,50),Artisan=Math.Min(n,25),GoldCrops=Math.Min(n,100)};
 int threshold=r.Kind switch{TotemKind.Thunderstorm=>1,TotemKind.Growth=>50,TotemKind.Processing=>25,_=>100};
 Check(TotemUnlockRules.Eligible(r,farming,s)==(farming>=r.Farming&&n>=threshold),"threshold matrix "+r.Kind);
}
Reset(farming:5);Game1.player.Items.Add(Item("787",1));helper.Events.GameLoop.Load();Check(State().Battery&&State().Entries[TotemKind.Thunderstorm].Eligible==-1,"farming 5 with battery remains locked");
Reset(farming:6);helper.Events.GameLoop.Load();Check(State().Entries[TotemKind.Thunderstorm].Eligible==-1,"farming 6 without battery remains locked");
var battery=Item("787",1);typeof(TotemUnlockService).GetMethod("ItemReceived",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{Game1.player,battery,1});helper.Events.GameLoop.Tick();
Check(State().Battery&&State().Entries[TotemKind.Thunderstorm].Scheduled==11&&Game1.player.mailbox.Count==0,"received-then-spent battery qualifies permanently; mail tomorrow");
for(int i=0;i<50;i++)helper.Events.GameLoop.Load();Check(Game1.player.craftingRecipes.Count==0,"eligibility and reload never silently learn recipes");Day();
Check(Game1.player.mailbox.SequenceEqual(new[]{recipes[0].Mail})&&Game1.player.craftingRecipes.Count==0,"mail delivered next morning; unread mail does not learn recipe");
for(int i=0;i<50;i++)helper.Events.GameLoop.Tick();Check(Game1.player.mailbox.Count==1,"unread letter not duplicated");
Game1.player.mailbox.Clear();Game1.player.mailReceived.Add(recipes[0].Mail);Game1.player.craftingRecipes[recipes[0].Id]=0;helper.Events.GameLoop.Load();Day();Check(Game1.player.mailbox.Count==0&&State().Entries[TotemKind.Thunderstorm].RecipeUnlocked,"read/known state never reissues letter");
Reset(farming:8);Game1.player.basicShipped["192"]=30;Game1.player.basicShipped["400"]=19;helper.Events.GameLoop.Load();Check(State().Crops==49&&State().Entries[TotemKind.Growth].Eligible==-1,"49 shipped crops locked");Game1.player.basicShipped["400"]++;helper.Events.GameLoop.Tick();Check(State().Crops==50&&State().Entries[TotemKind.Growth].Scheduled==11,"30 potatoes plus 20 strawberries unlock without 50 varieties");
Reset(farming:9);Game1.player.basicShipped["custom-artisan"]=24;helper.Events.GameLoop.Load();Check(State().Artisan==24&&State().Entries[TotemKind.Processing].Eligible==-1,"24 artisan locked; category recognized for unlisted artisan item");Game1.player.basicShipped["custom-artisan"]++;helper.Events.GameLoop.Tick();Check(State().Entries[TotemKind.Processing].Scheduled==11,"25 category-based artisan goods eligible");
Reset(day:100);Game1.player.basicShipped["192"]=10000;Game1.player.basicShipped["348"]=3000;Game1.player.basicShipped["787"]=1;helper.Events.GameLoop.Load();Check(State().Crops==50&&State().Artisan==25&&State().Battery&&State().GoldCrops==0,"legacy native quantities and battery restored; lost quality never invented");Check(State().Entries[TotemKind.Harvest].Eligible==-1,"large legacy totals cannot prove gold history");
Reset(day:20);foreach(var r in recipes.Take(3))Game1.player.craftingRecipes[r.Id]=0;helper.Events.GameLoop.Load();Shipment(new(){Item("192",999,0),Item("400",999,1),Item("16",999,4),Item("348",999,4)});Check(State().GoldCrops==0,"normal/silver crops, forage and gold artisan do not count as gold crops");Day();Shipment(new(){Item("192",99,2)});Check(State().GoldCrops==99&&State().Entries[TotemKind.Harvest].Eligible==-1,"99 gold crops locked");Shipment(new(){Item("192",99,2)});Check(State().GoldCrops==99,"same settlement day repeated does not double count");Day();Shipment(new(){Item("400",1,4)});helper.Events.GameLoop.Day();Check(State().GoldCrops==100&&Game1.player.mailbox.Contains(recipes[3].Mail),"100th crop iridium qualifies and settled overnight arrives in morning");
Reset();helper.Events.GameLoop.Load();Shipment(new(){Item("192",999,4)},false);Check(State().GoldCrops==0&&State().Crops==0,"chest/Orders/non-settlement lists ignored");Game1.player.Items.Add(Item("192",999,4));helper.Events.GameLoop.Tick();Check(State().GoldCrops==0&&State().Crops==0,"inventory possession is not shipping");
Reset(day:200);var mature=new TotemUnlockState{Battery=true,Crops=50,Artisan=25,GoldCrops=100};Game1.player.modData[TotemUnlockRules.StateKey]=mature.Save();helper.Events.GameLoop.Load();
for(int i=0;i<4;i++)
{
 Check(Game1.player.mailbox.Count==i,"no same-day mail burst");Day();Check(Game1.player.mailbox.Count==i+1&&Game1.player.mailbox[i]==recipes[i].Mail,"mature recorded save gets ordered daily mail "+i);
 string saved=Game1.player.modData[TotemUnlockRules.StateKey];Game1.player.modData[TotemUnlockRules.StateKey]=TotemUnlockState.Load(saved).Save();for(int n=0;n<5;n++)helper.Events.GameLoop.Load();Check(Game1.player.mailbox.Count==i+1,"save/reload mail deduplication "+i);
}
Check(Game1.player.craftingRecipes.Count==0,"four unread mails still grant no recipe");Day();Check(Game1.player.mailbox.Count==4,"no fifth mail");
Reset();Game1.player.basicShipped["192"]=50;Game1.player.craftingRecipes[recipes[1].Id]=2;helper.Events.GameLoop.Load();Day();Check(Game1.player.mailbox.Count==0&&Game1.player.craftingRecipes[recipes[1].Id]==2,"known recipe and crafted count preserved without mail");
Reset();var host=Game1.player;host.farmingLevel.Value=8;host.basicShipped["192"]=50;helper.Events.GameLoop.Load();var guest=new Farmer{UniqueMultiplayerID=2};guest.farmingLevel.Value=4;guest.basicShipped["192"]=50;Game1.player=guest;helper.Events.GameLoop.Load();Day();Check(guest.mailbox.Count==0,"farming 4 farmhand does not inherit host unlock");Game1.player=host;helper.Events.GameLoop.Day();Check(host.mailbox.Contains(recipes[1].Mail)&&guest.mailbox.Count==0,"each local farmer has personal mail and state");
Reset();Context.IsWorldReady=false;helper.Events.GameLoop.Load();Check(Game1.player.modData.Count==0,"no title-screen mutation");Context.IsWorldReady=true;IntegrationHost.Saving=true;helper.Events.GameLoop.Tick();Check(Game1.player.modData.Count==0,"no ordinary refresh while saving");IntegrationHost.Saving=false;Game1.player.IsLocalPlayer=false;helper.Events.GameLoop.Tick();Check(Game1.player.modData.Count==0,"no writes to remote farmer");
Reset();Game1.player.modData[TotemUnlockRules.StateKey]="{broken";helper.Events.GameLoop.Load();Check(Game1.player.modData[TotemUnlockRules.StateKey]=="{broken"&&Game1.player.mailbox.Count==0&&monitor.Lines.Count==1,"corrupt record preserved and unlock fails closed");
Reset();var book=new OrderBook();book.Known.Add("(O)787");Game1.Farm.modData[OrderRules.Prefix+"Farmer/1"]=book.Save();helper.Events.GameLoop.Load();Check(State().Battery&&State().Crops==0&&State().Artisan==0,"prior Orders observed battery proves possession but not shipping");
foreach(string lang in new[]{"default","zh"})
{
 BsvI18n.Text=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(args[0],lang+".json")))!;
 var mail=new AssetRequestedEventArgs{NameWithoutLocale=new(){Text="Data/mail"}};helper.Events.Content.Load(mail);
 var crafting=new AssetRequestedEventArgs{NameWithoutLocale=new(){Text="Data/CraftingRecipes"}};helper.Events.Content.Load(crafting);
 foreach(var r in recipes){Check(mail.Data.AsDictionary<string,string>().Data[r.Mail].Contains("%item craftingrecipe "+r.Id+" %%[#]"),"native craftingrecipe token and title "+lang+" "+r.Kind);Check(crafting.Data.AsDictionary<string,string>().Data[r.Id]==r.Data(BsvI18n.Text[r.Key+".name"]),"localized exact recipe asset "+lang+" "+r.Kind);}
}
Check(helper.ConsoleCommands.Items.Keys.SequenceEqual(new[]{"bsv_totem_status"}),"service exposes read-only status only");
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,assertions=checks,scenarios,real_game=false,real_multiplayer=false,scope="production unlock rules, service, mail/recipe assets using API doubles; native reading tested separately"}));

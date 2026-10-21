using System.Reflection;using System.Text.Json;using BetterStardewValley;using BetterStardewValley.Orders;using BetterStardewValley.Transactions;using StardewValley;using StardewValley.Objects;using StardewValley.Inventories;using StardewValley.GameData.Objects;using StardewValley.GameData.BigCraftables;using StardewModdingAPI;using HarmonyLib;using Microsoft.Xna.Framework;using SObject=StardewValley.Object;
partial class Program
{
 static OrdersService Service=null!;static long RequestSequence;
 static void Call(string name,params object[] args){try{typeof(OrdersService).GetMethod(name,Any)!.Invoke(Service,args);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();}}
 static MarketOrder Offer(string kind="standard",string id="test")=>new(id,kind=="barter"?"Dwarf":kind is "special" or "premium"?"Morris":"Pierre","(O)24",20,35,kind=="barter"?0:125,kind=="barter"?0:875,10,0,kind,"normal",kind=="special"?new(){new("(BC)272",1)}:kind=="barter"?new(){new("(O)287",2),new("(O)382",5)}:new());
 static SObject Goods(int count,int quality=0,string id="24")=>new(id,count){Quality=quality};
 static Chest Chest(int x,int y,GameLocation? location=null,string id="130")
 {var l=location??Farm;var c=new Chest(true,new Vector2(x,y),id);l.objects[new(x,y)]=c;return c;}
 static void Fresh(MarketOrder? order=null)
 {
  Reset();Farm.modData.Clear();Place();Host.team.useSeparateWallets.Value=false;Host.team.SetIndividualMoney(Host,1000);Online=new(){Host,Guest};RequestSequence=0;AtomicFault=-1;
  Farm.modData[OrderMarket.Key]=new MarketBook{Available=order==null?new():new(){order}}.Save();
 }
 static MarketIntent Intent(MarketOrder c,string action="accept",Farmer? who=null)
 {
  who??=Host;var r=new MarketIntent(Guid.NewGuid().ToString("N"),++RequestSequence,who.UniqueMultiplayerID,action,c.Id,"Farm",4,4,MarketInventory.Fingerprint(who.Items));who.modData[OrderMarket.IntentKey]=JsonSerializer.Serialize(r);return r;
 }
 static int Count(Chest c,string id="(O)24")=>c.Items.Where(i=>i?.QualifiedItemId==id).Sum(i=>i.Stack);
 static void MarketTests(Harmony h,IModHelper helper,IMonitor monitor)
 {
  foreach(string id in new[]{"24","128","388","346","184","382","472","216","194","195","211","210","196","378","380","384","386","286","287","288","768","769"})Game1.objectData[id]=new ObjectData{Name="Fixture "+id,DisplayName="Fixture "+id,Texture="fixture",SpriteIndex=0,Price=50,Type="Basic",Category=id=="24"?-75:id=="128"?-4:id=="472"?-74:0};
  foreach(string id in new[]{"232","BigChest","BigStoneChest","272","256","248"})Game1.bigCraftableData[id]=new BigCraftableData{Name="Fixture "+id,DisplayName="Fixture "+id,Texture="fixture",SpriteIndex=0};
  SetCookingFixtures();
  Service=new OrdersService(helper,monitor,(IManifest)Recorder.Make(typeof(IManifest)));World=(TerminalWorld)typeof(TerminalWorld).GetField("Instance",Any)!.GetValue(null)!;
  var pool=OrderCatalog.MarketCandidates(Game1.objectData.Select(p=>new OrderCatalog.CatalogItem(p.Key,p.Value.Price,p.Value.Category,p.Value.Type)),new HashSet<string>{"128"},new HashSet<string>{"472"});
  Check(OrderMarket.Npcs.All(n=>pool.Any(c=>c.Npc==n)),"actual production catalog classifier has all eight NPC pools without history");
  var policyItems=new[]{new OrderCatalog.CatalogItem("128",100,-4),new OrderCatalog.CatalogItem("24",35,-75),new OrderCatalog.CatalogItem("999999",500,-75,"Quest"),new OrderCatalog.CatalogItem("External.Mod_Fruit",500,-79)};
  var filtered=OrderCatalog.MarketCandidates(policyItems,new HashSet<string>{"128"},new HashSet<string>(),new[]{"(O)128"});
  Check(filtered.All(c=>c.Id!="(O)128"&&c.Id!="(O)999999"&&c.Id!="(O)External.Mod_Fruit"),"qualified exclusion, quest data and unreviewed foreign items do not enter generation");
  Check(filtered.Any(c=>c.Id=="(O)24"),"unrelated legal crops remain in order pool");
  var seed=OrderCatalog.MarketCandidates(new[]{new OrderCatalog.CatalogItem("472",0,-74)},new HashSet<string>(),new HashSet<string>{"472"});Check(seed.Single().Price==20,"zero-sale seed reference is 20g");
  var c=Offer("special");Fresh(c);var a=Chest(9,5);var b=Chest(8,5);a.Items.Add(Goods(9,0));a.Items.Add(Goods(20,4));b.Items.Add(Goods(7,1));b.Items.Add(Goods(8,2));
  var plan=MarketInventory.Plan(MarketInventory.Chests(),c,Host.UniqueMultiplayerID)!;Check(plan.Select(p=>p.Original.Quality).SequenceEqual(new[]{0,1,2}),"native multichest plan selects normal then silver then gold");
  int before=Host.team.GetIndividualMoney(Host);var req=Intent(c);Call("Process",req);Call("Process",req);
  Check(Count(a)+Count(b)==24&&Host.Items.Count(i=>i?.QualifiedItemId=="(BC)272")==1,"real service deducts once across chests and grants one native Auto-Petter");
  Check(Service.Read().Active.Single().State=="pending"&&Host.team.GetIndividualMoney(Host)==before,"accept credits no immediate gold");
  string saved=Farm.modData[OrderMarket.Key];Call("Settle");int paid=Host.team.GetIndividualMoney(Host);Call("Settle");
  Check(paid==before+c.Gold&&Host.team.GetIndividualMoney(Host)==paid&&Service.Read().Active.Count==0,"DayEnding actual native wallet pays once");
  string after=Farm.modData[OrderMarket.Key];Farm.modData[OrderMarket.Key]=after;Call("Settle");Check(Host.team.GetIndividualMoney(Host)==paid,"saved paid ledger cannot pay twice");
  Farm.modData[OrderMarket.Key]=saved;Host.team.SetIndividualMoney(Host,before);Call("Settle");Check(Host.team.GetIndividualMoney(Host)==paid,"pre-night save restores BOTH unpaid ledger and wallet before exactly one replay");
  Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));for(int n=0;n<Host.Items.Count;n++)Host.Items[n]=Goods(999,0,"390");req=Intent(c);Call("Process",req);
  Check(Count(a)==20&&Service.Read().Available.Count==1&&Service.Read().Receipts[Host.UniqueMultiplayerID].Message=="orders.reward-space","full reward inventory consumes nothing");
  c=Offer("barter");Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));Call("Process",Intent(c));Check(Count(a)==0&&Host.Items.Where(i=>i?.QualifiedItemId=="(O)287").Sum(i=>i.Stack)==2&&Host.Items.Where(i=>i?.QualifiedItemId=="(O)382").Sum(i=>i.Stack)==5&&Service.Read().Completed.Count==1&&Service.Read().Active.Count==0,"Dwarf native reward items complete immediately without coin ledger");Call("Settle");Check(Host.team.GetIndividualMoney(Host)==1000,"Dwarf pays no overnight gold");
  c=Offer();Fresh(c);a=Chest(8,5);a.Items.Add(Goods(19));Call("Process",Intent(c));Check(Count(a)==19&&Service.Read().Available.Count==1,"insufficient stock never partially deducted");
  foreach(int minimum in new[]{1,2})foreach(int quality in new[]{0,1,2,4}){var o=Offer("premium") with{MinimumQuality=minimum};var good=Goods(20,quality);Check(MarketInventory.Goods(good,o)==(quality>=minimum),"native minimum quality acceptance");}
  Fresh(c);a=Chest(8,5);var protectedItem=Goods(20);protectedItem.modData["third-party/protected"]="yes";a.Items.Add(protectedItem);a.Items.Add(Goods(20));a.Items[1].IsRecipe=true;Call("Process",Intent(c));Check(Count(a)==40,"unknown metadata and recipes excluded");
  foreach(string id in new[]{"130","232","BigChest","BigStoneChest"}){var box=Chest(12,12,null,id);Check(MarketInventory.Container(box),"four normal chest types accepted");box.fridge.Value=true;Check(!MarketInventory.Container(box),"fridge rejected");}
  foreach(string id in new[]{"248","256"})Check(!MarketInventory.Container(Chest(12,12,null,id)),"shipping and Junimo containers rejected");
  Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));a.GetMutex().RequestLock();a.GetMutex().Update(Farm);Call("Process",Intent(c));Check(Count(a)==20&&Service.Read().Receipts[Host.UniqueMultiplayerID].Message=="orders.chest-busy","already held chest lock is not borrowed");a.GetMutex().ReleaseLock();
  Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));req=Intent(c);Farm.objects.Remove(new Vector2(8,5));Call("Process",req);Check(Count(a)==20&&Service.Read().Available.Count==1,"removed chest transaction rejected");
  Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));Put(Guest,Farm,3,4);var guest=Intent(c,"accept",Guest);Put(Host,Farm,3,4);req=Intent(c);Call("Process",req);Call("Process",guest);
  Check(Count(a)==0&&Service.Read().Active.Count==1&&!Service.Read().Receipts[Guest.UniqueMultiplayerID].Success,"competing farmers accept one contract exactly once");
  Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));Put(Guest,Farm,3,4);guest=Intent(c,"reject",Guest);Put(Host,Farm,3,4);req=Intent(c);Call("Process",guest);Call("Process",req);Check(Count(a)==20&&Service.Read().Available.Count==0&&Service.Read().Active.Count==0,"reject wins accept/reject race without deductions");
  Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));Put(Guest,Farm,3,4);guest=Intent(c,"accept",Guest);Put(Host,Farm,3,4);Host.team.useSeparateWallets.Value=true;Host.team.SetIndividualMoney(Host,1000);Host.team.SetIndividualMoney(Guest,2000);Call("Process",guest);Online=new(){Host,Guest};Call("Settle");Check(Host.team.GetIndividualMoney(Guest)==2875&&Host.team.GetIndividualMoney(Host)==1000,"separate wallet belongs to accepting farmer");
  Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));Put(Guest,Farm,3,4);req=Intent(c,"accept",Guest);Put(Host,Farm,3,4);Online=new(){Host};Call("Process",req);Check(Count(a)==20,"disconnected accepting farmer does not commit");Online=new(){Host,Guest};Call("Process",req);Check(Count(a)==0,"reconnected durable intent commits once");
  Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));req=Intent(c);HostAuthority=false;Game1.multiplayerMode=1;Call("Process",req);Check(Count(a)==20,"client process cannot authorize host transaction");HostAuthority=true;Game1.multiplayerMode=0;
  Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));req=Intent(c);typeof(IntegrationHost).GetProperty("Saving",Any)!.SetValue(null,true);Call("Process",req);Check(Count(a)==20,"saving gate prevents inventory commits");typeof(IntegrationHost).GetProperty("Saving",Any)!.SetValue(null,false);
  // Inject after actual production AtomicStep.Apply: deterministic even if the CLR inlines native Inventory setters.
  h.Patch(typeof(AtomicChange).GetMethod("Execute",Any)!,prefix:new HarmonyMethod(typeof(Program),nameof(InjectAtomic)));
  for(int fail=1;fail<=3;fail++)
  {
   c=Offer("special");Fresh(c);a=Chest(8,5);b=Chest(9,5);a.Items.Add(Goods(10));b.Items.Add(Goods(10));req=Intent(c);AtomicFault=fail-1;bool thrown=false;try{Call("Process",req);}catch(InvalidOperationException){thrown=true;}AtomicFault=-1;
   Check(thrown&&Count(a)+Count(b)==20&&Host.Items.All(i=>i==null)&&Service.Read().Available.Count==1,"native partial inventory write exception rolls back goods/reward/contract");Call("Process",req);Check(Count(a)+Count(b)==0&&Host.Items.Count(i=>i?.QualifiedItemId=="(BC)272")==1,"retry after native rollback grants exactly once");
  }
  Fresh();Farm.modData.Remove(OrderMarket.Key);var old=new OrderBook();for(int n=0;n<45;n++)old.Available.Add(new("old"+n,"Pierre","(O)24",20,35,125,875,1,5,"normal",false));old.Active.Add(new("old-active","Gus","(O)346",5,200,125,1250,1,5,"small",false,2));old.Completed.Add(new("old-paid","Willy","(O)128",5,200,125,1250,1,5,"small",false,2,3));
  string key=OrderRules.Prefix+"Farmer/"+Host.UniqueMultiplayerID;Farm.modData[key]=old.Save();Call("Migrate");string migrated=Farm.modData[OrderMarket.Key];Call("Migrate");
  Check(Service.Read().Available.Count==45&&Service.Read().Active.Single().State=="legacy-active"&&Service.Read().Completed.Single().Id=="old-paid"&&migrated==Farm.modData[OrderMarket.Key],"legacy >40 offers and unpaid/paid distinction preserved idempotently");
  Check(Farm.modData[key]==old.Save()&&Farm.modData[OrderRules.Prefix+"Backup.v1/"+Host.UniqueMultiplayerID]==old.Save(),"verbatim legacy backup retained");Call("Settle");Check(Host.team.GetIndividualMoney(Host)==1000,"legacy unfulfilled and previously paid contracts never auto-pay");
  var escrow=OrdersInventory.Encode(Goods(20));var oldRequest=new OrderRequest(Guid.NewGuid().ToString("N"),1,Host.UniqueMultiplayerID,"deliver","old-active","Farm",4,4,10,new(){escrow});Host.modData[OrderRules.PendingKey]=JsonSerializer.Serialize(oldRequest);Call("RecoverLegacyEscrow");Call("RecoverLegacyEscrow");Check(Host.Items.Where(i=>i?.QualifiedItemId=="(O)24").Sum(i=>i.Stack)==20&&!Host.modData.ContainsKey(OrderRules.PendingKey),"legacy undecided escrow refunded once without paying");
  Fresh();Call("DayStarted");int morning=Service.Read().Available.Count;string morningRaw=Farm.modData[OrderMarket.Key];Call("DayStarted");Call("Migrate");Check(morning is 1 or 2&&morningRaw==Farm.modData[OrderMarket.Key],"actual service morning generation shared and same-day idempotent");
  c=Offer("special");Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));Put(Guest,Farm,3,4);guest=Intent(c,"accept",Guest);Put(Host,Farm,3,4);req=Intent(c);Call("Process",guest);Call("Process",req);Call("Process",guest);
  Check(Count(a)==0&&Host.Items.Concat(Guest.Items).Count(i=>i?.QualifiedItemId=="(BC)272")==1&&Service.Read().Active.Single().Actor==Guest.UniqueMultiplayerID,"competing Morris trades award exactly one Auto-Petter to winner");
  c=Offer("special");Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));Host.MaxItems=6;for(int n=0;n<6;n++)Host.Items[n]=Goods(999,0,"390");Call("Process",Intent(c));Check(Count(a)==20&&Host.Items.Skip(6).All(i=>i==null),"reward planner never fills locked backpack slots");
  c=Offer("barter");Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));for(int n=1;n<Host.Items.Count;n++)Host.Items[n]=Goods(999,0,"390");Call("Process",Intent(c));Check(Count(a)==20&&Host.Items[0]==null&&Service.Read().Available.Count==1,"barter requires space for ALL rewards before spending");
  c=Offer();Fresh(c);a=Chest(8,5);b=Chest(8,5,Home);a.Items.Add(Goods(9));b.Items.Add(Goods(11));Call("Process",Intent(c));Check(Count(a)+Count(b)==0,"Farm and FarmHouse chests jointly supply one order");
  Fresh();Farm.modData.Remove(OrderMarket.Key);Farm.modData[OrderRules.Prefix+"Farmer/"+Host.UniqueMultiplayerID]="{}";bool refused=false;try{Call("Migrate");}catch(InvalidOperationException){refused=true;}Check(refused&&!Farm.modData.ContainsKey(OrderMarket.Key),"incomplete legacy save fails closed without erasure");
  R7Checks(h);
  R8Checks();
  // AFK binds ordinary chests by adding a BSV-owned identity, not a protection flag.
  c=Offer();Fresh(c);a=Chest(8,5);a.Items.Add(Goods(20));
  string afkKey=BetterStardewValley.AfkFishing.AfkFishingService.ChestKey;
  string afkId=Guid.NewGuid().ToString("N");a.modData[afkKey]=afkId;
  Check(MarketInventory.Container(a)&&Service.Stock(c)==20,"AFK-bound ordinary chest remains visible to Orders");
  Call("Process",Intent(c));Call("Settle");
  Check(Count(a)==0&&Service.Read().Completed.Single().Id==c.Id&&Host.team.GetIndividualMoney(Host)==1875&&a.modData[afkKey]==afkId,"AFK-bound chest transaction preserves identity and settles once");
  a.modData["other/protected"]="yes";Check(!MarketInventory.Container(a),"AFK identity never overrides unknown chest metadata");
  a.modData.Remove("other/protected");a.modData[afkKey]="invalid";
  Check(!MarketInventory.Container(a),"malformed AFK chest identity fails closed");
  a.modData[afkKey]=afkId;a.fridge.Value=true;
  Check(!MarketInventory.Container(a),"AFK identity never overrides special-container exclusions");
  SaveChecks(h);
  Check(Harmony.GetAllPatchedMethods().Count(m=>m.DeclaringType==typeof(StardewValley.Menus.ShippingMenu)&&Harmony.GetPatchInfo(m)!.Owners.Contains("Eddie.BetterStardewValley.Orders"))==3,"three exact native ShippingMenu targets installed");
  foreach(var (w,he) in new[]{(640,360),(800,600),(1280,720),(1920,1080)}){var (rect,row)=OrderIncome.Geometry(w,he);Check(rect.X>=0&&rect.Y>=0&&rect.Bottom<=he&&row>=30,"seven-row income layout fits supported viewport");}
 }
}

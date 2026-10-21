using BetterStardewValley.Transactions;
using HarmonyLib;
using System.Text.Json;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
namespace BetterStardewValley.Orders;

// A — one farm market; synchronous host transactions on native network inventories.
internal sealed class OrdersService
{
 internal static OrdersService Instance=null!;
 internal readonly IModHelper Helper;private readonly IMonitor Monitor;private readonly string ModId,Version;
 internal readonly PerScreen<string> Notice=new(()=>"");private readonly HashSet<string> Errors=new();private bool Suspended;
 private readonly Func<IEnumerable<string>?> ItemExclusions;
 internal bool Enabled=>!Configuration.SharedPreferences.HasLocal||Configuration.SharedPreferences.Current.EnableOrdersSystem;
 internal OrdersService(IModHelper helper,IMonitor monitor,IManifest manifest,Func<IEnumerable<string>?>? itemExclusions = null)
 {
  Instance=this;Helper=helper;Monitor=monitor;ModId=manifest.UniqueID;Version=manifest.Version.ToString();
  ItemExclusions=itemExclusions??(()=>Array.Empty<string>());
  var h=new Harmony(ModId+".Orders");try{_=new OrderTerminal(helper,monitor,h);OrderIncome.Install(h);}catch{h.UnpatchAll(h.Id);throw;}
  helper.Events.GameLoop.UpdateTicked+=(_,e)=>{if(e.IsMultipleOf(12))Tick();};
  helper.Events.GameLoop.SaveLoaded+=(_,_)=>Guard(Migrate);
  helper.Events.GameLoop.DayStarted+=(_,_)=>Guard(DayStarted);
  helper.Events.GameLoop.DayEnding+=(_,_)=>Guard(Settle);
  helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>{Errors.Clear();Notice.ResetAllScreens();Suspended=false;};
 }
 internal string Tr(string key,object? tokens=null)=>BsvI18n.Get(Helper,key,tokens);
 internal string Number(int value)=>BsvI18n.Number(Helper,value,"N0");
 internal MarketBook Read(long actor=0)=>Game1.getFarm().modData.TryGetValue(OrderMarket.Key,out var raw)?MarketBook.Load(raw):new();
 private static void Write(MarketBook book)=>Game1.getFarm().modData[OrderMarket.Key]=book.Save();
 internal bool HasPending=>Game1.player.modData.ContainsKey(OrderMarket.IntentKey)||Game1.player.modData.ContainsKey(OrderRules.PendingKey);
 internal bool InReach(Farmer who,string location,int x,int y)=>TerminalEntity.CanUse(who,location,x,y);
 internal int Stock(MarketOrder order)=>MarketInventory.Count(order,Game1.player.UniqueMultiplayerID);
 // The menu already counted this refresh's stock. Process independently re-plans under chest locks.
 internal bool CanAccept(MarketOrder order,int displayedStock)=>Enabled&&!HasPending&&(order.State=="available"||order.State=="legacy-active"&&order.Actor==Game1.player.UniqueMultiplayerID)&&displayedStock>=order.Quantity&&MarketInventory.Rewards(Game1.player,order.Rewards)!=null;
 internal void Request(string action,MarketOrder order,string location,int x,int y)
 {
  if(!Enabled){Notice.Value="orders.disabled";return;}
  if(!Context.IsWorldReady||IntegrationHost.Saving||Suspended||HasPending||!InReach(Game1.player,location,x,y)||action is not("accept" or "reject"))return;
  if(!Context.IsMainPlayer&&!Context.IsSplitScreen&&Helper.Multiplayer.GetConnectedPlayer(Game1.MasterPlayer.UniqueMultiplayerID)?.GetMod(ModId)?.Version.ToString()!=Version)
  {Notice.Value="orders.host-version";return;}
  Guard(()=>
  {
   var who=Game1.player;var book=Read();long.TryParse(who.modData.TryGetValue(OrderMarket.SequenceKey,out var raw)?raw:"0",out long old);
   long sequence=checked(Math.Max(old,book.Receipts.GetValueOrDefault(who.UniqueMultiplayerID)?.Sequence??0)+1);
   var intent=new MarketIntent(Guid.NewGuid().ToString("N"),sequence,who.UniqueMultiplayerID,action,order.Id,location,x,y,MarketInventory.Fingerprint(who.Items));
   who.modData[OrderMarket.SequenceKey]=sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
   who.modData[OrderMarket.IntentKey]=JsonSerializer.Serialize(intent);Notice.Value="orders.pending";
  });
 }
 internal static MarketIntent? Intent(Farmer who)
 {
  if(!who.modData.TryGetValue(OrderMarket.IntentKey,out var raw))return null;
  if(raw.Length>10000)throw new InvalidOperationException("Oversized Orders intent; preserved.");
  var r=JsonSerializer.Deserialize<MarketIntent>(raw);
  if(r==null||r.Actor!=who.UniqueMultiplayerID||r.Sequence<=0||!Guid.TryParseExact(r.Id,"N",out _)||r.Action is not("accept" or "reject")||string.IsNullOrEmpty(r.Order)||string.IsNullOrEmpty(r.Location)||r.Inventory?.Length!=64)
   throw new InvalidOperationException("Invalid Orders intent; preserved.");
  return r;
 }
 private void Tick()
 {
  if(!Context.IsWorldReady||IntegrationHost.Saving||Suspended)return;
  Guard(RecoverLegacyEscrow);
  if(Context.IsMainPlayer)foreach(var who in Game1.getOnlineFarmers())Guard(()=>{var r=Intent(who);if(r!=null)Process(r);});
  Guard(()=>
  {
   var who=Game1.player;var r=Intent(who);if(r==null)return;
   var receipt=Read().Receipts.GetValueOrDefault(who.UniqueMultiplayerID);
   if(receipt?.Id!=r.Id||receipt.Sequence!=r.Sequence)return;
   // Receipt and inventory can arrive in different packets. Never locally grant another reward.
   if(receipt.Success&&receipt.Inventory!=""&&MarketInventory.Fingerprint(who.Items)!=receipt.Inventory)return;
   who.modData.Remove(OrderMarket.IntentKey);Notice.Value=receipt.Message;
  });
 }
 private void Process(MarketIntent request)
 {
  if(!IntegrationHost.CanMutate||Suspended)return;
  var who=Game1.getOnlineFarmers().FirstOrDefault(f=>f.UniqueMultiplayerID==request.Actor);if(who==null||Intent(who)!=request)return;
  var book=Read();if(request.Sequence<=(book.Receipts.GetValueOrDefault(request.Actor)?.Sequence??0))return;
  if(!Enabled){Receipt(book,request,false,"orders.disabled");return;}
  string? error=!InReach(who,request.Location,request.X,request.Y)||Game1.eventUp||who.UsingTool?"orders.return-terminal":null;
  var order=OrderMarket.Find(book,request.Order,request.Actor);
  if(order==null||request.Action=="reject"&&order.State!="available")error="orders.unavailable";
  if(MarketInventory.Fingerprint(who.Items)!=request.Inventory)error="orders.inventory-changed";
  if(error!=null){Receipt(book,request,false,error);return;}
  if(request.Action=="reject")
  {var next=book.Copy();OrderMarket.Commit(next,OrderMarket.Find(next,order!.Id,request.Actor)!,request.Actor,Game1.Date.TotalDays,true);Receipt(next,request,true,"orders.rejected");return;}
  var plan=MarketInventory.Plan(MarketInventory.Chests(),order!,request.Actor);
  if(plan==null){Receipt(book,request,false,"orders.chest-short");return;}
  if(MarketInventory.Rewards(who,order!.Rewards)==null){Receipt(book,request,false,"orders.reward-space");return;}
  bool finished=false;var sources=plan.Select(p=>p.Source).Distinct().OrderBy(p=>p.Location.NameOrUniqueName,StringComparer.Ordinal).ThenBy(p=>p.Tile.Y).ThenBy(p=>p.Tile.X).ToArray();
  if(sources.Length>128){Receipt(book,request,false,"orders.chest-limit");return;}
  MarketInventory.WithLocks(sources,()=>
  {
   var current=Read();var contract=OrderMarket.Find(current,request.Order,request.Actor);
   if(!Enabled){Receipt(current,request,false,"orders.disabled");finished=true;return;}
   if(current.Revision!=book.Revision||contract==null||contract.Id!=order.Id||!InReach(who,request.Location,request.X,request.Y)||Intent(who)!=request||MarketInventory.Fingerprint(who.Items)!=request.Inventory)return;
   var verified=MarketInventory.Plan(sources,order,request.Actor);var rewards=MarketInventory.Rewards(who,order.Rewards);if(verified==null||rewards==null)return;
   var next=current.Copy();OrderMarket.Commit(next,OrderMarket.Find(next,order.Id,request.Actor)!,who.UniqueMultiplayerID,Game1.Date.TotalDays,false);
   next.Receipts[request.Actor]=new(request.Id,request.Sequence,true,order.Gold>0?"orders.pending-settlement":"orders.barter-complete",order.Rewards.Count>0?MarketInventory.Fingerprint(rewards):"");
   string old=Game1.getFarm().modData[OrderMarket.Key];var steps=MarketInventory.Steps(verified,who,rewards);
   steps.Add(new(()=>Write(next),()=>Game1.getFarm().modData[OrderMarket.Key]=old));
   AtomicChange.Execute(()=>Enabled&&IntegrationHost.CanMutate&&!Suspended&&sources.All(c=>MarketInventory.Live(c)&&c.Chest.GetMutex().IsLockHeld())&&Game1.getOnlineFarmers().Contains(who),steps);finished=true;
  });
  if(!finished)Receipt(Read(),request,false,"orders.chest-busy");
 }
 private static void Receipt(MarketBook book,MarketIntent request,bool success,string message)
 {book.Receipts[request.Actor]=new(request.Id,request.Sequence,success,message);book.Revision++;Write(book);}
 private void DayStarted()
 {
  if(!IntegrationHost.CanMutate||Suspended)return;Migrate();var book=Read();
  if(!Enabled){if(book.LastGeneratedAbsoluteDay<Game1.Date.TotalDays){book.LastGeneratedAbsoluteDay=Game1.Date.TotalDays;book.Revision++;Write(book);}return;}
  if(book.LastGeneratedAbsoluteDay>=Game1.Date.TotalDays)return;
  OrderMarket.GenerateDay(book,OrderCatalog.BuildMarket(ItemExclusions()),Game1.uniqueIDForThisGame,Game1.Date.TotalDays,Game1.year);Write(book);
  if(book.GenerationFailures.Count>0)Monitor.Log("BSV Orders template failures (bounded attempts, no fallback): "+JsonSerializer.Serialize(book.GenerationFailures),LogLevel.Warn);
 }
 private void Migrate()
 {
  if(!IntegrationHost.CanMutate||Suspended)return;
  var book=Read();var steps=new List<AtomicStep>();bool changed=!Game1.getFarm().modData.ContainsKey(OrderMarket.Key);
  foreach(var pair in Game1.getFarm().modData.Pairs.Where(p=>p.Key.StartsWith(OrderRules.Prefix+"Farmer/",StringComparison.Ordinal)).OrderBy(p=>p.Key,StringComparer.Ordinal))
  {
   if(book.Migrated.ContainsKey(pair.Key))continue;
   if(!long.TryParse(pair.Key[(OrderRules.Prefix+"Farmer/").Length..],out long actor))throw new InvalidOperationException("Unknown legacy Orders owner; preserved.");
   var old=OrderBook.Load(pair.Value,true);
   if(old?.Schema!=1||old.Available==null||old.Active==null||old.Completed==null||old.Available.Concat(old.Active).Concat(old.Completed).Any(o=>!OrderRules.Valid(o)))throw new InvalidOperationException("Unknown legacy Orders schema; preserved.");
   foreach(var c in old.Available)book.Available.Add(OrderMarket.Migrate(c,actor,"available"));
   foreach(var c in old.Active)book.Active.Add(OrderMarket.Migrate(c,actor,"legacy-active"));
   foreach(var c in old.Completed)book.Completed.Add(OrderMarket.Migrate(c,actor,"completed"));
   book.Migrated[pair.Key]=OrderRules.Hash(pair.Value).ToString("x");
   string backup=OrderRules.Prefix+"Backup.v1/"+actor;
   if(!Game1.getFarm().modData.ContainsKey(backup))steps.Add(new(()=>Game1.getFarm().modData[backup]=pair.Value,()=>Game1.getFarm().modData.Remove(backup)));
   changed=true;
  }
  // Never re-price saved Dwarf quotes; retain historical DwarfFoodCap markers verbatim.
  if(!changed)return;
  book.Revision++;MarketBook.Load(book.Save());
  bool had=Game1.getFarm().modData.TryGetValue(OrderMarket.Key,out var previous);
  steps.Add(new(()=>Write(book),()=>{if(had)Game1.getFarm().modData[OrderMarket.Key]=previous!;else Game1.getFarm().modData.Remove(OrderMarket.Key);}));
  AtomicChange.Execute(()=>IntegrationHost.CanMutate&&!Suspended,steps);
 }
 private void RecoverLegacyEscrow()
 {
  var who=Game1.player;if(!who.modData.TryGetValue(OrderRules.PendingKey,out var raw))return;
  var request=JsonSerializer.Deserialize<OrderRequest>(raw)??throw new InvalidOperationException("Invalid legacy escrow; preserved.");
  if(request.Sequence<=0||!Guid.TryParseExact(request.Id,"N",out _)||request.Actor!=who.UniqueMultiplayerID||request.Goods==null||request.Goods.Count>128||request.Action is not("accept" or "deliver"))throw new InvalidOperationException("Invalid legacy escrow; preserved.");
  if(!Game1.getFarm().modData.TryGetValue(OrderRules.Prefix+"Farmer/"+request.Actor,out string original))return;
  var old=OrderBook.Load(original,true);
  bool consumed=old.Receipt is {Accepted:true} receipt&&receipt.Id==request.Id&&receipt.Sequence==request.Sequence;
  if(!consumed&&request.Goods.Count>0)
  {
   var before=who.Items.Cast<Item?>().ToArray();var after=OrdersInventory.Refund(who,request.Goods);
   if(after==null){Notice.Value="orders.refund-space";return;}
   AtomicChange.Execute(()=>!IntegrationHost.Saving,new[]{new AtomicStep(()=>OrdersInventory.Set(who,after),()=>OrdersInventory.Set(who,before)),new AtomicStep(()=>who.modData.Remove(OrderRules.PendingKey),()=>who.modData[OrderRules.PendingKey]=raw)});
  }
  else who.modData.Remove(OrderRules.PendingKey);
 }
 private void Settle()
 {
  if(!IntegrationHost.CanMutate||Suspended)return;
  var book=Read();
  var next=book.Copy();
  int day=Game1.Date.TotalDays;
  var team=Game1.MasterPlayer.team;
  bool separate=team.useSeparateWallets.Value;
  if(next.SettlementDay!=day){next.SettlementDay=day;next.SettlementTotals.Clear();next.SettlementSeparate=separate;}
  var farmers=Game1.getAllFarmers().ToDictionary(f=>f.UniqueMultiplayerID);var steps=new List<AtomicStep>();
  foreach(var group in next.Active.Where(o=>o.State=="pending"&&o.Accepted<=day).GroupBy(o=>separate?o.Actor:Game1.MasterPlayer.UniqueMultiplayerID).ToArray())
  {
   if(!farmers.TryGetValue(group.Key,out var farmer))continue;
   long total=group.Sum(o=>(long)o.Gold);int before=team.GetIndividualMoney(farmer);
   if(total+before>int.MaxValue){Monitor.Log("BSV Orders wallet full; unpaid ledger retained for farmer "+group.Key,LogLevel.Warn);continue;}
   int amount=checked((int)total);steps.Add(new(()=>team.AddIndividualMoney(farmer,amount),()=>team.SetIndividualMoney(farmer,before)));
   next.SettlementTotals[group.Key]=checked(next.SettlementTotals.GetValueOrDefault(group.Key)+amount);
   foreach(var o in group){next.Active.Remove(o);next.Completed.Insert(0,o with{State="completed",Completed=day});}
  }
  OrderMarket.Trim(next);next.Revision++;string old=book.Save();steps.Add(new(()=>Write(next),()=>Write(MarketBook.Load(old))));
  AtomicChange.Execute(()=>IntegrationHost.CanMutate&&!Suspended&&team.useSeparateWallets.Value==separate,steps);
 }
 internal int Income()
 {
  var book=Read();if(book.SettlementDay<Game1.Date.TotalDays-1||book.SettlementDay>Game1.Date.TotalDays)return 0;
  return book.SettlementSeparate?book.SettlementTotals.GetValueOrDefault(Game1.player.UniqueMultiplayerID):checked(book.SettlementTotals.Values.Sum());
 }
 private void Guard(Action run)
 {try{run();}catch(Exception ex){if(ex is AggregateException)Suspended=true;if(Errors.Add(ex.Message))Monitor.Log(Tr("orders.error")+"\n"+ex,LogLevel.Error);Notice.Value="orders.error";}}
}

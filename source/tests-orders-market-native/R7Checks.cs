using System.Text.Json;
using System.Xml.Serialization;
using BetterStardewValley;
using BetterStardewValley.Orders;
using BetterStardewValley.Transactions;
using HarmonyLib;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.Objects;
using SObject=StardewValley.Object;
partial class Program
{
 static readonly (string Id,string Name,int Price)[] CookingFixtures={ ("216","Bread",60),("194","Fried Egg",35),("195","Omelet",125),("211","Pancakes",80),("210","Hashbrowns",120),("196","Salad",110) };
 static void SetCookingFixtures()
 {
  // Synthetic values, not extracted game Content; deliberately labelled in the report.
  foreach(var f in CookingFixtures)Game1.objectData[f.Id]=new ObjectData{Name=f.Name,DisplayName=f.Name,Texture="fixture",SpriteIndex=0,Price=f.Price,Type="Cooking",Category=-7};
 }
 static string Quote(MarketOrder order)=>JsonSerializer.Serialize(order);
 static int AtomicFault=-1,ObservedSteps;
 static void InjectAtomic(ref IReadOnlyList<AtomicStep> steps)
 {
  if(AtomicFault<0)return;int fault=AtomicFault;AtomicFault=-1;ObservedSteps=steps.Count;
  var replaced=steps.ToArray();var original=replaced[fault];
  replaced[fault]=new(()=>{original.Apply();throw new InvalidOperationException("R7 injected after atomic step "+fault);},original.Undo);steps=replaced;
 }
 static Action? BeforeLocks;
 static void LockRace(){var run=BeforeLocks;BeforeLocks=null;run?.Invoke();}
 static void R7Checks(Harmony h)
 {
  var runtimeFixturePool=OrderCatalog.BuildMarket();
  Check(runtimeFixturePool.Where(c=>c.Npc=="Dwarf").Select(c=>c.Id).ToHashSet().SetEquals(CookingFixtures.Select(f=>"(O)"+f.Id)),"production DataLoader/catalog path sees precisely six synthetic Cooking fixtures");
  foreach(var f in CookingFixtures)
  {
   var item=ItemRegistry.Create<SObject>("(O)"+f.Id,allowNull:true);
   Check(item!=null&&item.QualifiedItemId=="(O)"+f.Id&&item.Type=="Cooking"&&item.Category==-7&&Game1.objectData[f.Id].Price==f.Price,"native object constructibility/type/price with synthetic food fixture "+f.Id);
   var excluded=OrderCatalog.BuildMarket(new[]{"(O)"+f.Id});Check(!excluded.Any(c=>c.Npc=="Dwarf"&&c.Id=="(O)"+f.Id),"Dwarf respects existing qualified exclusion "+f.Id);
   foreach(var bad in new[]{(Type:"Basic",Category:-7),(Type:"Cooking",Category:0),(Type:"Quest",Category:-7)})
   {
    var filtered=OrderCatalog.MarketCandidates(new[]{new OrderCatalog.CatalogItem(f.Id,f.Price,bad.Category,bad.Type)},new HashSet<string>(),new HashSet<string>());
    Check(filtered.All(c=>c.Npc!="Dwarf"),"whitelisted ID with changed non-cooking/quest data cannot become Dwarf template");
   }
  }
  foreach(var f in CookingFixtures)foreach(int quantity in Enumerable.Range(1,4))
  {
   var c=new MarketOrder("meal-"+f.Id+"-"+quantity,"Dwarf","(O)"+f.Id,quantity,f.Price,0,0,10,0,"barter","small",new(){new("(O)286",1)});
   Fresh(c);var a=Chest(8,5);a.Items.Add(Goods(quantity,0,f.Id));var req=Intent(c);int money=Host.team.GetIndividualMoney(Host);Call("Process",req);Call("Process",req);
   Check(Count(a,c.Item)==0&&Host.Items.Where(i=>i?.QualifiedItemId=="(O)286").Sum(i=>i.Stack)==1&&Service.Read().Completed.Single().Id==c.Id&&Service.Read().Active.Count==0&&Host.team.GetIndividualMoney(Host)==money,"six foods x four quantities consume exact requirement, reward once, immediate Completed with no gold");
   Call("Settle");Check(Host.team.GetIndividualMoney(Host)==money,"new Dwarf trade never creates overnight gold");
  }
  // Actual production service migration, generation and serializer preserve all saved quote states.
  foreach(int quantity in Enumerable.Range(1,8))
  {
   var c=Offer("barter","historical-"+quantity) with{Item="(O)216",Quantity=quantity,ReferencePrice=60};Fresh(c);
   var book=Service.Read();book.Active.Add(c with{Id="legacy-active",Actor=Host.UniqueMultiplayerID,Accepted=5,State="legacy-active"});book.Active.Add(c with{Id="pending",Actor=Host.UniqueMultiplayerID,Accepted=5,State="pending"});book.Completed.Add(c with{Id="completed",Actor=Host.UniqueMultiplayerID,Accepted=5,Completed=6,State="completed"});
   book.Migrated["DwarfFoodCap/1.1.14/"+c.Id]="historical migration audit; do not replay";book.Receipts[Host.UniqueMultiplayerID]=new(Guid.NewGuid().ToString("N"),10,true,"paid");
   Farm.modData[OrderMarket.Key]=book.Save();string raw=book.Save();Call("Migrate");Call("Migrate");
   Check(Farm.modData[OrderMarket.Key]==raw&&MarketBook.Load(raw).Save()==raw&&Host.Items.All(i=>i==null),"service migration preserves old 1..8 food quotes, multiple rewards, all states, receipt and migration markers byte-for-byte");
   Call("DayStarted");var after=Service.Read();
   Check(Quote(after.Available.Single(o=>o.Id==c.Id))==Quote(c)&&after.Migrated.Single().Value==book.Migrated.Single().Value&&JsonSerializer.Serialize(after.Active)==JsonSerializer.Serialize(book.Active)&&JsonSerializer.Serialize(after.Completed)==JsonSerializer.Serialize(book.Completed),"day generation leaves old terms and paid/pending states intact");
   // Fulfil the old available contract at its actual saved quantity/rewards, even eight meals.
   var a=Chest(8,5);a.Items.Add(Goods(quantity,0,"216"));RequestSequence=10;Call("Process",Intent(c));
   Check(Count(a,"(O)216")==0&&Host.Items.Where(i=>i?.QualifiedItemId=="(O)287").Sum(i=>i.Stack)==2&&Host.Items.Where(i=>i?.QualifiedItemId=="(O)382").Sum(i=>i.Stack)==5,"old saved 1..8 food contract executes its original multi-item reward");
  }
  foreach(int quantity in new[]{3,4})
  {
   MarketOrder? generated=null;for(int seq=1;seq<20000;seq++){var next=OrderMarket.Generate(928734,12,seq,2,Array.Empty<MarketOrder>(),runtimeFixturePool,out _);if(next?.Npc=="Dwarf"&&next.Quantity==quantity){generated=next;break;}}
   Check(generated!=null,"production generator finds new "+quantity+" food quote");var c=generated!;Fresh(c);string quoted=Quote(c);Call("Migrate");Call("DayStarted");
   var save=new SaveGame{player=Host,farmhands=new(){Guest},locations=new(){Farm}};string xml;using(var w=new StringWriter()){SaveGame.serializer.Serialize(w,save);xml=w.ToString();}
   SaveGame loaded;using(var r=new StringReader(xml))loaded=(SaveGame)SaveGame.serializer.Deserialize(r)!;
   Farm.modData[OrderMarket.Key]=loaded.locations.OfType<Farm>().Single().modData[OrderMarket.Key];Call("Migrate");TestDay++;Call("DayStarted");Call("DayStarted");
   Check(Quote(Service.Read().Available.Single(o=>o.Id==c.Id))==quoted,"new 3/4 quote and preselected reward survive native SaveGame XML, Migrate and day reentry");
   var a=Chest(8,5);a.Items.Add(Goods(quantity,0,c.Item[3..]));var req=Intent(c);Call("Process",req);Call("Process",req);
   Check(Count(a,c.Item)==0&&Host.Items.Where(i=>i!=null).Sum(i=>i.Stack)==c.Rewards.Single().Count&&Host.Items.Single(i=>i!=null).QualifiedItemId==c.Rewards.Single().Id,"new 3/4 saved quote trades once with its originally selected reward");
  }
  var dwarf=new MarketOrder("dwarf-safety","Dwarf","(O)216",4,60,0,0,10,0,"barter","small",new(){new("(O)287",1)});
  Fresh(dwarf);var chest=Chest(8,5);chest.Items.Add(Goods(3,0,"216"));Call("Process",Intent(dwarf));Check(Count(chest,dwarf.Item)==3&&Host.Items.All(i=>i==null)&&Quote(Service.Read().Available.Single())==Quote(dwarf)&&Service.Read().Active.Count==0,"short Dwarf stock changes no goods/rewards/contract/payment; failure receipt permitted");
  Fresh(dwarf);chest=Chest(8,5);chest.Items.Add(Goods(4,0,"216"));for(int i=0;i<Host.Items.Count;i++)Host.Items[i]=Goods(999,0,"390");Call("Process",Intent(dwarf));Check(Count(chest,dwarf.Item)==4&&Service.Read().Available.Count==1&&Service.Read().Completed.Count==0,"full Dwarf inventory rejects before spending");
  Fresh(dwarf);chest=Chest(8,5);chest.Items.Add(Goods(4,0,"216"));for(int i=0;i<Host.Items.Count;i++)Host.Items[i]=Goods(999,0,"390");Host.Items[0]=Goods(998,0,"287");Call("Process",Intent(dwarf));Check(Count(chest,dwarf.Item)==0&&Host.Items[0].Stack==999&&Service.Read().Completed.Count==1,"full backpack accepts one reward into existing stack space");
  foreach(var c in new[]{dwarf,Offer("special") with{Percent=80,Gold=560}})for(int fault=0;fault<4;fault++)
  {
   Fresh(c);var a=Chest(8,5);var b=Chest(9,5);a.Items.Add(Goods(c.Quantity/2,0,c.Item[3..]));b.Items.Add(Goods(c.Quantity-c.Quantity/2,0,c.Item[3..]));string before=Farm.modData[OrderMarket.Key];var req=Intent(c);AtomicFault=fault;bool threw=false;
   try{Call("Process",req);}catch(InvalidOperationException){threw=true;}
   Check(threw&&ObservedSteps==4&&Count(a,c.Item)+Count(b,c.Item)==c.Quantity&&Host.Items.All(i=>i==null)&&Farm.modData[OrderMarket.Key]==before&&Host.team.GetIndividualMoney(Host)==1000,"throw AFTER each production atomic step including book write rolls back exact old state");
   Call("Process",req);Call("Process",req);Check(Count(a,c.Item)+Count(b,c.Item)==0&&Host.Items.Where(i=>i!=null).Sum(i=>i.Stack)==1,"retry after each atomic-step rollback awards exactly once");
  }
  h.Patch(typeof(MarketInventory).GetMethod("WithLocks",Any)!,prefix:new HarmonyMethod(typeof(Program),nameof(LockRace)));
  Fresh(dwarf);chest=Chest(8,5);chest.Items.Add(Goods(4,0,"216"));BeforeLocks=()=>{var changed=Service.Read();changed.Revision++;Farm.modData[OrderMarket.Key]=changed.Save();};Call("Process",Intent(dwarf));Check(Count(chest,dwarf.Item)==4&&Host.Items.All(i=>i==null)&&Service.Read().Available.Count==1,"revision changed between plan and locks rejects transaction");
  Fresh(dwarf);chest=Chest(8,5);chest.Items.Add(Goods(4,0,"216"));BeforeLocks=()=>chest.Items[0].Stack=3;Call("Process",Intent(dwarf));Check(Count(chest,dwarf.Item)==3&&Host.Items.All(i=>i==null)&&Service.Read().Available.Count==1,"locked inventory replan catches missing food without partial spend");
  Fresh(dwarf);chest=Chest(8,5);chest.Items.Add(Goods(4,0,"216"));Put(Guest,Farm,3,4);var guest=Intent(dwarf,"accept",Guest);Put(Host,Farm,3,4);var host=Intent(dwarf);Call("Process",host);Call("Process",guest);Call("Process",host);
  Check(Count(chest,dwarf.Item)==0&&Host.Items.Concat(Guest.Items).Where(i=>i?.QualifiedItemId=="(O)287").Sum(i=>i.Stack)==1&&Service.Read().Completed.Count==1,"competing host/guest Dwarf requests commit once (serialized native service, no sockets)");
  var oldRequest=host with{Id=Guid.NewGuid().ToString("N"),Sequence=host.Sequence-1};Host.modData[OrderMarket.IntentKey]=JsonSerializer.Serialize(oldRequest);string committed=Farm.modData[OrderMarket.Key];Call("Process",oldRequest);Check(Farm.modData[OrderMarket.Key]==committed,"out-of-order lower request sequence cannot replay transaction");
  // Receipt may arrive before inventory replication: the client must retain intent, never grant.
  Host.modData[OrderMarket.IntentKey]=JsonSerializer.Serialize(host);var reward=Host.Items[0];Host.Items[0]=null;HostAuthority=false;Game1.multiplayerMode=1;Call("Tick");Check(Host.modData.ContainsKey(OrderMarket.IntentKey)&&Host.Items[0]==null,"delayed inventory replication retains intent without local reward");Host.Items[0]=reward;Call("Tick");Check(!Host.modData.ContainsKey(OrderMarket.IntentKey)&&Host.Items[0]==reward,"matching replicated inventory acknowledges receipt without duplication");HostAuthority=true;Game1.multiplayerMode=0;
  Fresh(dwarf);Put(Guest,Farm,3,4);HostAuthority=false;Game1.multiplayerMode=1;Recorder.PeerVersion="2.0.2";Service.Request("accept",dwarf,"Farm",4,4);Check(!Guest.modData.ContainsKey(OrderMarket.IntentKey)&&Service.Notice.Value=="orders.host-version","mismatched 2.0.2 host mod version blocks 2.0.4 client request");Recorder.PeerVersion="2.0.4";HostAuthority=true;Game1.multiplayerMode=0;
  // Existing classifier has no Joja-open dependency; simulate community completion flags.
  Fresh();Host.mailReceived.Add("ccIsComplete");Host.mailReceived.Add("ccMovieTheater");var postCc=OrderCatalog.BuildMarket();Check(postCc.Any(c=>c.Npc=="Morris"),"Morris candidate pool remains after CC completion/closed-Joja flags (headless fixture)");
  foreach(bool special in new[]{false,true})
  {
   MarketOrder? c=null;for(int seq=1;seq<10000;seq++){var next=OrderMarket.Generate(928734,12,seq,2,Array.Empty<MarketOrder>(),postCc,out _);if(next?.Npc=="Morris"&&(next.Kind=="special")==special){c=next;break;}}
   Check(c!=null&&c.Percent==(special?80:180)&&c.Gold==OrderRules.Reward(c.ReferencePrice,c.Quantity,c.Percent),"production Morris quote retains 180/80 and existing rounding");Fresh(c);chest=Chest(8,5);chest.Items.Add(Goods(c!.Quantity,c.MinimumQuality,c.Item[3..]));Call("Process",Intent(c));Check(Service.Read().Active.Single().State=="pending"&&Host.team.GetIndividualMoney(Host)==1000&&Host.Items.Count(i=>i?.QualifiedItemId=="(BC)272")== (special?1:0),"Morris generated contract immediate special item, no immediate gold");Call("Settle");Call("Settle");Check(Host.team.GetIndividualMoney(Host)==1000+c.Gold,"Morris generated 180/80 quote settles exactly once");
  }
 }
}

using System.Text.Json;
using BetterStardewValley.Orders;
using StardewValley;
partial class Program
{
 static SaveGame XmlRoundtrip()
 {
  string xml;using(var w=new StringWriter()){SaveGame.serializer.Serialize(w,new SaveGame{player=Host,farmhands=new(){Guest},locations=new(){Farm}});xml=w.ToString();}
  using var r=new StringReader(xml);return (SaveGame)SaveGame.serializer.Deserialize(r)!;
 }
 static void R8Checks()
 {
  var pool=OrderCatalog.BuildMarket();var generated=new Dictionary<string,MarketOrder>();MarketOrder? mega5=null,one=null;
  for(int seq=1;seq<=100000;seq++)
  {
   var c=OrderMarket.Generate(928734,12,seq,2,Array.Empty<MarketOrder>(),pool,out _);
   if(c?.Npc!="Dwarf")continue;
   generated.TryAdd(c.Item+"/"+c.Quantity,c);
   if(c.Rewards.Single()==new MarketReward("(O)288",5))mega5=c;
   if(c.Rewards.Single().Count==1)one=c;
   if(generated.Count==24&&mega5!=null&&one!=null)break;
  }
  Check(generated.Count==24&&mega5!=null&&one!=null,"R8 production Generate supplies six foods x four requirements, reward one and Mega Bomb five");
  foreach(var c in generated.Values.Append(mega5!).Append(one!))
  {
   Fresh(c);string quoted=Quote(c);Call("Migrate");Call("DayStarted");TestDay++;Call("DayStarted");Call("DayStarted");
   var loaded=XmlRoundtrip();Farm.modData[OrderMarket.Key]=loaded.locations.OfType<Farm>().Single().modData[OrderMarket.Key];Call("Migrate");
   Check(Quote(Service.Read().Available.Single(o=>o.Id==c.Id))==quoted,"R8 generated type/count and distinct food requirement survive XML, migration and day reentry");
   var chest=Chest(8,5);chest.Items.Add(Goods(c.Quantity,0,c.Item[3..]));var req=Intent(c);Call("Process",req);Call("Process",req);
   var reward=c.Rewards.Single();Check(Count(chest,c.Item)==0&&Host.Items.Where(i=>i?.QualifiedItemId==reward.Id).Sum(i=>i.Stack)==reward.Count&&Service.Read().Completed.Single().Id==c.Id&&Service.Read().Active.Count==0,"R8 production contract consumes food quantity and awards exact stored count once");
   loaded=XmlRoundtrip();var farm=loaded.locations.OfType<Farm>().Single();var saved=MarketBook.Load(farm.modData[OrderMarket.Key]);
   Check(saved.Completed.Single().Rewards.Single()==reward&&loaded.player.Items.Where(i=>i?.QualifiedItemId==reward.Id).Sum(i=>i.Stack)==reward.Count&&farm.objects.Pairs.Where(p=>p.Value is StardewValley.Objects.Chest).SelectMany(p=>((StardewValley.Objects.Chest)p.Value).Items).All(i=>i==null||i.QualifiedItemId!=c.Item),"R8 XML persists completed quote, complete reward and deducted chest together");
   Call("Settle");Call("Settle");Check(Host.team.GetIndividualMoney(Host)==1000,"R8 Dwarf has no overnight payment");
  }
  // Five-item transactions for every reward type, including single-type splitting.
  foreach(var option in OrderMarket.DwarfRewards)
  {
   var c=mega5! with{Id="five-"+option.Id,Rewards=new(){new(option.Id,5)}};
   foreach(int mode in Enumerable.Range(0,5))
   {
    Fresh(c);var chest=Chest(8,5);chest.Items.Add(Goods(c.Quantity,0,c.Item[3..]));
    for(int i=0;i<Host.Items.Count;i++)Host.Items[i]=Goods(999,0,"390");
    if(mode==0){Host.Items[0]=null;Host.MaxItems=1;} // exactly one usable empty slot
    if(mode==1){Host.Items[0]=Goods(997,0,option.Id[3..]);Host.Items[1]=Goods(996,0,option.Id[3..]);} // 2+3 across existing stacks
    if(mode==2){Host.Items[0]=Goods(997,0,option.Id[3..]);Host.Items[1]=null;} // 2 existing + 3 new
    if(mode==3)Host.Items[0]=Goods(995,0,option.Id[3..]); // capacity four, short by one
    if(mode==4){Host.MaxItems=1;Host.Items[1]=null;} // empty slot locked
    int before=Host.Items.Where(i=>i?.QualifiedItemId==option.Id).Sum(i=>i.Stack);string inventory=MarketInventory.Fingerprint(Host.Items);var req=Intent(c);Call("Process",req);Call("Process",req);
    int after=Host.Items.Where(i=>i?.QualifiedItemId==option.Id).Sum(i=>i.Stack);
    if(mode<3)Check(after-before==5&&Count(chest,c.Item)==0&&Service.Read().Completed.Single().Id==c.Id,"R8 five-item exact/stacked/split capacity delivers whole reward once "+option.Id+" mode "+mode);
    else Check(after==before&&inventory==MarketInventory.Fingerprint(Host.Items)&&Count(chest,c.Item)==c.Quantity&&Quote(Service.Read().Available.Single())==Quote(c)&&Service.Read().Completed.Count==0,"R8 capacity short one/locked slot rejects entire five-item trade "+option.Id);
   }
  }
  var trade=mega5! with{Quantity=4};
  for(int fault=0;fault<5;fault++)
  {
   Fresh(trade);var a=Chest(8,5);var b=Chest(9,5);a.Items.Add(Goods(2,0,trade.Item[3..]));b.Items.Add(Goods(2,0,trade.Item[3..]));
   for(int i=0;i<Host.Items.Count;i++)Host.Items[i]=Goods(999,0,"390");Host.Items[0]=Goods(997,0,"288");Host.Items[1]=Goods(996,0,"288");
   string before=Farm.modData[OrderMarket.Key],inventory=MarketInventory.Fingerprint(Host.Items);var req=Intent(trade);AtomicFault=fault;bool threw=false;
   try{Call("Process",req);}catch(InvalidOperationException){threw=true;}
   Check(threw&&ObservedSteps==5&&Count(a,trade.Item)+Count(b,trade.Item)==4&&MarketInventory.Fingerprint(Host.Items)==inventory&&Farm.modData[OrderMarket.Key]==before,"R8 rollback after each of two food/two reward/book writes restores all five steps");
   Call("Process",req);Call("Process",req);Check(Host.Items[0].Stack==999&&Host.Items[1].Stack==999&&Count(a,trade.Item)+Count(b,trade.Item)==0&&Service.Read().Completed.Count==1,"R8 retry after partial Mega Bomb five write grants only five total");
  }
  Fresh(trade);var supply=Chest(8,5);supply.Items.Add(Goods(3,0,trade.Item[3..]));Call("Process",Intent(trade));Check(Count(supply,trade.Item)==3&&Host.Items.All(i=>i==null)&&Service.Read().Available.Count==1,"R8 insufficient food never grants any of five rewards");
  Fresh(trade);supply=Chest(8,5);supply.Items.Add(Goods(4,0,trade.Item[3..]));Put(Guest,Farm,3,4);var guest=Intent(trade,"accept",Guest);Put(Host,Farm,3,4);var host=Intent(trade);Call("Process",guest);Call("Process",host);Call("Process",guest);
  Check(Count(supply,trade.Item)==0&&Host.Items.Concat(Guest.Items).Where(i=>i?.QualifiedItemId=="(O)288").Sum(i=>i.Stack)==5&&Service.Read().Completed.Single().Actor==Guest.UniqueMultiplayerID,"R8 competing native farmers get exactly one fixed Mega Bomb five reward; no sockets");
  foreach(var rewards in new[]{new List<MarketReward>{new("(O)288",1)},new List<MarketReward>{new("(O)287",7),new("(O)382",5)}})
  {
   var c=trade with{Id="old",Quantity=8,Rewards=rewards};Fresh(c);var book=Service.Read();
   book.Active.Add(c with{Id="old-active",Actor=Host.UniqueMultiplayerID,Accepted=2,State="legacy-active"});book.Active.Add(c with{Id="old-pending",Actor=Host.UniqueMultiplayerID,Accepted=2,State="pending"});book.Completed.Add(c with{Id="old-done",Actor=Host.UniqueMultiplayerID,Accepted=2,Completed=3,State="completed"});book.Migrated["DwarfFoodCap/1.1.14/old"]="unchanged audit";Farm.modData[OrderMarket.Key]=book.Save();string raw=book.Save();Call("Migrate");
   Check(raw==Farm.modData[OrderMarket.Key]&&MarketBook.Load(raw).Save()==raw&&Host.Items.All(i=>i==null),"R8 old fixed-one and historical multi-reward contracts in every state remain byte-identical, no catch-up reward");
  }
 }
}

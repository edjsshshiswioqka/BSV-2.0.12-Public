using BetterStardewValley;
using Newtonsoft.Json;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.GameData.Machines;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;
using SObject=StardewValley.Object;
partial class Program {
 static int checks;
 static void Check(bool b,string name){checks++;if(!b)throw new Exception("FAIL: "+name);}
 static void Main(){Data();Rarity();IndexChecks();Console.WriteLine(JsonConvert.SerializeObject(new {suite="wine-data-and-production-adapter",assertions=checks,status="PASS",boundary="real GameData types, production C# feature, fake game/network boundaries; not live gameplay"}));}
 static void Data(){
 var crops=new Dictionary<string,CropData>{["301"]=new(){DaysInPhase=new(){1,1,2,3,3},RegrowDays=3,IsRaised=true,HarvestItemId="398",HarvestMinStack=1,HarvestMaxStack=1,HarvestMinQuality=0,NeedsWatering=true},["472"]=new(){DaysInPhase=new(){1,1,1,1}}};
 string other=JsonConvert.SerializeObject(crops["472"]);WineData.EditCrops(crops);var c=crops["301"];
 Check(c.Seasons.SequenceEqual(new[]{Season.Spring,Season.Summer,Season.Fall}),"cross-season live data; no winter");
 Check(c.DaysInPhase.Sum()==15&&c.RegrowDays==7,"15 and 7 days");
 Check(c.HarvestItemId=="398"&&c.HarvestMinStack==1&&c.HarvestMaxStack==1&&c.IsRaised&&c.NeedsWatering,"grape yield and watering unchanged");
 Check(other==JsonConvert.SerializeObject(crops["472"]),"other crops unchanged");
 var shops=new Dictionary<string,ShopData>{["SeedShop"]=new(){Items=new(){new(){ItemId="301",Condition="SEASON fall",Price=60},new(){ItemId="(O)301",Condition="PLAYER_HAS_MAIL Current PierreStocklist",Price=90},new(){ItemId="472",Price=20}}},["Joja"]=new(){Items=new(){new(){ItemId="301",Price=75,UseObjectDataPrice=true,ApplyProfitMargins=true,IgnoreShopPriceModifiers=false}}},["Blacksmith"]=new(){Items=new(){new(){ItemId="382",Price=100}}}};
 WineData.EditShops(shops);WineData.EditShops(shops);
 Check(shops["SeedShop"].Items.Count(i=>i.ItemId=="(O)301")==1,"idempotent no duplicate seed entries");
 Check(shops["SeedShop"].Items.Single(i=>i.ItemId=="(O)301").Condition==null,"spring access without stocklist");
 Check(shops.Values.SelectMany(s=>s.Items).Where(i=>i.ItemId is "301" or "(O)301").All(i=>i.Price==1200&&!i.UseObjectDataPrice&&i.IgnoreShopPriceModifiers&&i.ApplyProfitMargins==false&&i.PriceModifiers==null),"all grape shop prices fixed 1200");
 Check(shops["Blacksmith"].Items[0].Price==100&&shops["SeedShop"].Items[0].Price==20,"coal and unrelated seeds intact");
 var objects=new Dictionary<string,ObjectData>{["348"]=new(){Name="Wine",Type="Basic",Category=-26,Price=400,Edibility=30,IsDrink=true,Texture="Maps/springobjects",SpriteIndex=348}};
 string oldWine=JsonConvert.SerializeObject(objects["348"]);
 WineData.EditObjects(objects,k=>"tr/"+k,"grapeTexture","bloodTexture");
 Check(objects.Count==3&&JsonConvert.SerializeObject(objects["348"])==oldWine,"vanilla wine unchanged");
 foreach(var pair in new[]{(WineRules.GrapeWineId,1800,"grapeTexture"),(WineRules.BloodOfJesusId,30000,"bloodTexture")}){
  var o=objects[pair.Item1];Check(o.Price==pair.Item2&&o.Category==-26&&o.Type=="Basic","native artisan eligibility");
  Check(o.Texture==pair.Item3&&o.SpriteIndex==0&&!o.ColorOverlayFromNextIndex&&o.IsDrink,"sprites, no native wine recolor overlay");
  Check(o.DisplayName.StartsWith("tr/")&&o.Description.StartsWith("tr/"),"localization keys used");
 }
 int[] winePrices={1800,2250,2700,3600},wineArtisan={2520,3150,3780,5040},bloodPrices={30000,37500,45000,60000},bloodArtisan={42000,52500,63000,84000};
 float[] quality={1f,1.25f,1.5f,2f};
 for(int i=0;i<4;i++){Check((int)(objects[WineRules.GrapeWineId].Price*quality[i])==winePrices[i],"wine quality price");Check((int)(winePrices[i]*1.4f)==wineArtisan[i],"wine native artisan arithmetic");Check((int)(objects[WineRules.BloodOfJesusId].Price*quality[i])==bloodPrices[i],"rare quality price");Check((int)(bloodPrices[i]*1.4f)==bloodArtisan[i],"rare native artisan arithmetic");}
 var wineRule=new MachineOutputRule{Id="Wine",MinutesUntilReady=10000,Triggers=new(){new(){Id="fruit",RequiredTags=new(){"category_fruits"}}},OutputItem=new(){new(){ItemId="FLAVORED_ITEM Wine DROP_IN_ID"}}};
 var beer=new MachineOutputRule{Id="Beer",MinutesUntilReady=1750,Triggers=new(){new(){RequiredItemId="(O)262"}},OutputItem=new(){new(){ItemId="(O)346"}}};
 var caskRule=new MachineOutputRule{Id="Wine",Triggers=new(){new(){RequiredItemId="(O)348"}},OutputItem=new(){new(){OutputMethod="StardewValley.Objects.Cask, Stardew Valley: OutputCask",CustomData=new(){["AgingMultiplier"]="1"}}}};
 var machines=new Dictionary<string,MachineData>{[WineRules.Keg]=new(){OutputRules=new(){beer,wineRule}},[WineRules.Cask]=new(){OutputRules=new(){caskRule}}};
 string wineBefore=JsonConvert.SerializeObject(wineRule),beerBefore=JsonConvert.SerializeObject(beer),caskBefore=JsonConvert.SerializeObject(caskRule);
 WineData.EditMachines(machines);WineData.EditMachines(machines);
 Check(machines[WineRules.Keg].OutputRules.Count==2,"vanilla keg has no BSV override");
 var rules=machines[WineRules.BsvKeg].OutputRules;Check(rules.Count==3&&rules[0].Id==WineRules.KegRuleId,"one priority grape recipe");
 Check(rules[0].Triggers.Count==1&&rules[0].Triggers[0].RequiredItemId=="(O)398","only grapes");
 Check(rules[0].MinutesUntilReady==10000&&!rules[0].RecalculateOnCollect,"wine time retained; never recalculate collect");
 Check(rules[0].OutputItem.Count==1&&rules[0].OutputItem[0].OutputMethod.EndsWith(": OutputGrapeWine"),"custom output hook");
 Check(JsonConvert.SerializeObject(wineRule)==wineBefore&&JsonConvert.SerializeObject(beer)==beerBefore,"other wine and beer unchanged");
 Check(JsonConvert.SerializeObject(caskRule)==caskBefore&&machines[WineRules.Cask].OutputRules.Count==3,"native cask rules unchanged");
 foreach(var r in machines[WineRules.Cask].OutputRules.Take(2))Check(r.OutputItem[0].OutputMethod.EndsWith(": OutputCask")&&r.OutputItem[0].CustomData["AgingMultiplier"]=="1","native wine aging method and speed");
 caskRule.Triggers[0].RequiredItemId=null;caskRule.Triggers[0].RequiredTags=new(){"item_wine"};WineData.EditMachines(machines);
 Check(machines[WineRules.Cask].OutputRules.Count==3,"native tag-based wine trigger supported");
 caskRule.Triggers[0].RequiredTags=new(){"preserve_wine"};WineData.EditMachines(machines);
 Check(machines[WineRules.Cask].OutputRules.Count==3,"native named Wine / OutputCask rule supported");
 }
 static SObject Pending(){var m=new SObject{QualifiedItemId=WineRules.BsvKeg,MinutesUntilReady=10000};m.heldObject.Value=(SObject)GrapeWineFeature.OutputGrapeWine(m,new SObject{QualifiedItemId="(O)398"},false,new(),new(),out _)!;return m;}
 static void Rarity(){
 Game1.IsMasterGame=true;
 Check(GrapeWineFeature.OutputGrapeWine(new(){QualifiedItemId=WineRules.Keg},new SObject{QualifiedItemId="(O)398"},false,new(),new(),out _)==null,"vanilla keg cannot invoke exclusive grape output");
 var original=Pending();original.QualifiedItemId=WineRules.Keg;original.heldObject.Value!.Stack=3;var prior=original.heldObject.Value;
 GrapeWineFeature.Resolve(original,_=>throw new Exception("legacy keg must not roll"));
 Check(ReferenceEquals(original.heldObject.Value,prior)&&prior.Stack==3&&original.MinutesUntilReady==10000&&!prior.modData.ContainsKey(WineRules.PendingKey),"legacy unresolved vanilla batch preserved without new reward roll");
 var unresolved=Pending();var restored=JsonConvert.DeserializeObject<SObject>(JsonConvert.SerializeObject(unresolved))!;
 GrapeWineFeature.Resolve(unresolved);GrapeWineFeature.Resolve(restored);
 Check(unresolved.heldObject.Value!.QualifiedItemId==restored.heldObject.Value!.QualifiedItemId,"saved pending batch entropy gives identical result after reload");
 Check(Enumerable.Range(0,150).Count(WineRules.IsRare)==1,"exact one winning outcome among 150");
 for(int i=0;i<10;i++){
  var probe=GrapeWineFeature.OutputGrapeWine(new(){QualifiedItemId=WineRules.BsvKeg},new SObject{QualifiedItemId="(O)398"},true,new(),new(),out var time);
  Check(probe!=null&&!probe.modData.ContainsKey(WineRules.PendingKey)&&time==null,"probe creates no roll state and does not override timer");
 }
 Check(GrapeWineFeature.OutputGrapeWine(new(){QualifiedItemId=WineRules.BsvKeg},new SObject{QualifiedItemId="(O)613"},false,new(),new(),out _)==null,"non-grape input rejected");
 int draws=0;int Draw(int denominator){Check(denominator==150,"fixed denominator");draws++;return 0;}
 var a=Pending();Game1.IsMasterGame=false;GrapeWineFeature.Resolve(a,Draw);Check(draws==0&&a.heldObject.Value!.modData.ContainsKey(WineRules.PendingKey),"farmhand never rolls");
 Game1.IsMasterGame=true;GrapeWineFeature.Resolve(a,Draw);Check(draws==1&&a.heldObject.Value!.QualifiedItemId==WineRules.BloodOfJesus&&a.heldObject.Value.Stack==1,"host replaces with one rare bottle");
 Check(a.MinutesUntilReady==10000&&!a.Ready,"roll leaves production time and finished state alone");
 for(int i=0;i<1000;i++)GrapeWineFeature.Resolve(a,Draw);Check(draws==1,"repeat ticks/clicks cannot reroll");
 var loaded=JsonConvert.DeserializeObject<SObject>(JsonConvert.SerializeObject(a))!;GrapeWineFeature.Resolve(loaded,Draw);Check(draws==1&&loaded.heldObject.Value!.QualifiedItemId==WineRules.BloodOfJesus,"serialized resolved rare output remains fixed");
 var normal=Pending();GrapeWineFeature.Resolve(normal,n=>{draws++;return 149;});Check(normal.heldObject.Value!.QualifiedItemId==WineRules.GrapeWine&&!normal.heldObject.Value.modData.ContainsKey(WineRules.PendingKey),"failure produces normal only");
 loaded=JsonConvert.DeserializeObject<SObject>(JsonConvert.SerializeObject(normal))!;int before=draws;GrapeWineFeature.Resolve(loaded,Draw);Check(draws==before,"normal output also fixed on reload");
 for(int i=0;i<500;i++){var m=Pending();GrapeWineFeature.Resolve(m,_=>1);Check(m.heldObject.Value!.QualifiedItemId==WineRules.GrapeWine,"500 losses: no pity");}
 for(int i=0;i<2;i++){var m=Pending();GrapeWineFeature.Resolve(m,_=>0);Check(m.heldObject.Value!.QualifiedItemId==WineRules.BloodOfJesus,"consecutive rare wins allowed");}
 var pending=Pending();loaded=JsonConvert.DeserializeObject<SObject>(JsonConvert.SerializeObject(pending))!;before=draws;GrapeWineFeature.Resolve(loaded,Draw);Check(draws==before+1,"pending farmhand output resolves once after host load");
 var b=Pending();b.QualifiedItemId=WineRules.Cask;before=draws;GrapeWineFeature.Resolve(b,Draw);Check(before==draws,"cask never rare-rolls");
 b=Pending();b.heldObject.Value!.QualifiedItemId="(O)348";GrapeWineFeature.Resolve(b,Draw);Check(before==draws,"legacy ordinary wine remains ordinary");
 b=Pending();ItemRegistry.FailRare=true;bool failed=false;try{GrapeWineFeature.Resolve(b,Draw);}catch{failed=true;}ItemRegistry.FailRare=false;
 Check(failed&&before==draws&&b.heldObject.Value!.modData.ContainsKey(WineRules.PendingKey),"registry failure before draw keeps pending state");
 GrapeWineFeature.Resolve(b,Draw);Check(draws==before+1,"recovery rolls only once");
 }
}

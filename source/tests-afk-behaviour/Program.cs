using BetterStardewValley.AfkFishing;
using System.Text.Json;
class Program
{
 static readonly List<string> Names=new();
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Names.Add(name);}
 static void Main()
 {
  double left=AfkReturnRules.RemainingSeconds(2540,0,7000);
  Check(left==14,"1:40 leaves fourteen real seconds on the native seven-second clock");
  Check(AfkReturnRules.ClassifyManual(2540,left,40)==AfkNightPolicy.LateManual,"1:40 plus long legal route is late manual activation");
  Check(AfkReturnRules.ClassifyManual(2540,left,3)==AfkNightPolicy.Protected,"1:40 plus short bed route remains protected");
  Check(AfkReturnRules.ClassifyManual(2510,35,40)==AfkNightPolicy.LateManual,"route budget, not a hardcoded 1:40 threshold");
  Check(AfkReturnRules.ClassifyManual(900,700,40)==AfkNightPolicy.Protected,"normal daytime activation is protected");
  Check(AfkReturnRules.TravelSeconds(60,2,1,128)>AfkReturnRules.TravelSeconds(60,0,0,128),"door/gate/transition time included");
  Check(AfkReturnRules.TravelSeconds(60,0,0,64)>AfkReturnRules.TravelSeconds(60,0,0,128),"slower legal speed departs earlier");
  Check(AfkReturnRules.Depart(2400,84,70),"long return budget triggers departure before 00:30");
  Check(!AfkReturnRules.Depart(2200,168,30),"short return route allows earlier-night fishing");
  Check(AfkReturnRules.Depart(2430,63,1),"00:30 latest ordinary departure remains");
  foreach(int time in new[]{2500,2540,2550,2600})
   Check(!AfkReturnRules.Emergency(AfkNightPolicy.LateManual,time,1,double.PositiveInfinity,true,false),"late manual never invokes emergency at "+time);
  Check(AfkReturnRules.Emergency(AfkNightPolicy.Protected,2540,14,double.PositiveInfinity,true,false),"normal session path failure retains emergency protection");
  Check(!AfkReturnRules.Emergency(AfkNightPolicy.Protected,2540,14,3,true,false),"short safe legal route continues walking");
  Check(!AfkReturnRules.Emergency(AfkNightPolicy.Protected,2540,14,100,true,true),"emergency is at most once in a game day");
  Check(!AfkReturnRules.Emergency(AfkNightPolicy.Protected,2600,0,100,true,false),"never intercept native 2AM pass-out boundary");
  var saved=new AfkSave{Enabled=true,NightPolicy=AfkNightPolicy.LateManual,SafetyDay=10,ActivationDay=10,ActivationTime=2540,ReturnToSpot=true,WakePending=true,Spot=new(){Location="Beach",X=12,Y=8,Facing=2,RodId="(T)BambooPole",RodSlot=3},ChestId="optional",ChestMap="Beach",ChestX=11,ChestY=8};
  var restore=JsonSerializer.Deserialize<AfkSave>(JsonSerializer.Serialize(saved))!;
  Check(restore.Enabled&&restore.NightPolicy==AfkNightPolicy.LateManual&&restore.ReturnToSpot&&restore.WakePending&&restore.Spot?.Location=="Beach"&&restore.ChestId=="optional","save roundtrip retains activation responsibility, wake and fishing snapshot/chest link");
  var legacy=JsonSerializer.Deserialize<AfkSave>("{\"Schema\":1,\"Enabled\":true,\"Spot\":{\"Location\":\"Beach\",\"Facing\":2}}")!;
  Check(legacy.NightPolicy==AfkNightPolicy.Unclassified&&legacy.SafetyDay==-1&&legacy.Enabled,"old schema-one state loads with explicit migration defaults");
  var items=new Dictionary<string,AfkIngredient>();
  void Item(string id,int category=0,bool crop=false)=>items[id]=new(id,category,crop);
  foreach(string id in new[]{"128","163","715","716","717","718","719","720","721","722","723"})Item(id,-4);
  foreach(string id in new[]{"152","153","157","372","SeaJelly","SmokedFish"})Item(id);
  foreach(string id in new[]{"246","245","419","20","22","424","176","254"})Item(id);
  foreach(string id in new[]{"24","192","254","256","258","260","248"})Item(id,-75,true);
  foreach(string id in new[]{"216","196","206","211","195","223","227","228","213","730","265","custom","unknown","cycleA","cycleB","ambiguous","wildcard"})Item(id,-7);
  var recipes=new[]{"246 1/fixture/216", "20 1 22 1 419 1/fixture/196", "246 1 256 1 424 1/fixture/206", "246 1 176 1/fixture/211", "176 1 184 1/fixture/195", "-4 1/fixture/227", "-4 1 152 1/fixture/228", "130 1 266 1/fixture/213", "715 1 184 1/fixture/730", "269 1 269 1/fixture/265", "128 1/fixture/223", "227 1 216 1/fixture/custom", "missing 1/fixture/unknown", "cycleB 1/fixture/cycleA", "cycleA 1/fixture/cycleB", "246 1/fixture/ambiguous", "128 1/fixture/ambiguous", "-75 1/fixture/wildcard"};
  var food=new AfkFoodRules(items,new[]{"128","163"},recipes);
  foreach(string id in new[]{"128","163","715","716","717","718","719","720","721","722","723","152","153","157","372","SeaJelly","SmokedFish","227","228","213","730","265","223","custom"})
   Check(!food.Allows(id),"aquatic raw/recipe chain forbidden: "+id);
  foreach(string id in new[]{"216","196","206","211","424","24","192","254","256","258","260"})
   Check(food.Allows(id),"verified terrestrial food/crop allowed: "+id);
  foreach(string id in new[]{"unknown","cycleA","cycleB","ambiguous","missing"})Check(!food.Allows(id),"incomplete/cyclic/ambiguous food evidence rejected: "+id);
  Check(food.Allows("wildcard"),"terrestrial-only category recipe accepted");
  Item("aquatic-crop",-75,true);var tainted=new AfkFoodRules(items,new[]{"aquatic-crop"},recipes);
  Check(!tainted.Allows("wildcard")&&!tainted.Allows("aquatic-crop"),"aquatic identity wins over crop/category evidence");
  Check(!AfkRules.AcceptFood(false,0,false,false),"inedible crop cannot be food even with terrestrial origin");
  Check(!AfkRules.AcceptFood(true,20,true,false),"quest-protected edible crop rejected");
  Check(AfkRules.Hungry(30,100)&&!AfkRules.Hungry(30.01f,100),"thirty percent food threshold follows latest author rule");
  Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=Names.Count,scenarios=Names,live_game=false}));
 }
}

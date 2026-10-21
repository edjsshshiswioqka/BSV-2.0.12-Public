using BetterStardewValley.AfkFishing;
using HarmonyLib;
using StardewValley;
using StardewValley.Objects;
using SObject=StardewValley.Object;
partial class Program
{
 static Chest FoodChest=null!;static bool FoodLockHeld,RejectEating,FailEating;static string EatenId="";
 static bool EatBoundary(Farmer __instance,SObject o,bool overrideFullness)
 {
  FoodLockHeld=FoodChest.GetMutex().IsLockHeld();EatenId=o.ItemId;
  if(FailEating)throw new InvalidOperationException("Injected eat-start failure");
  if(!RejectEating){__instance.isEating=true;Eaten++;}return false;
 }
 static void FoodChecks(Harmony h,Action<Type,string,string,Type[]> patch)
 {
  patch(typeof(Farmer),"eatObject",nameof(EatBoundary),new[]{typeof(SObject),typeof(bool)});
  foreach(string id in new[]{"128","163","715","716","717","718","719","720","721","722","723","152","153","157","372","227","228","213","219","730","265","216","196","206","211","195","223","424","246","245","419","20","22","176","184","24","192","254","256","258","260","unknownFood"})
   Game1.objectData[id]=new(){Name=id,DisplayName=id,Type="Basic",Category=id is "128" or "163" or "715" or "716" or "717" or "718" or "719" or "720" or "721" or "722" or "723"?-4:0,Edibility=20,Texture="fixture",SpriteIndex=0,Price=100};
  foreach(string id in new[]{"24","192","254","256","258","260","248"})CropData[id]=new(){HarvestItemId=id};
  Game1.objectData["248"]=new(){Name="248",DisplayName="248",Type="Basic",Edibility=-300,Texture="fixture",SpriteIndex=0};
  Recipes=new(){["Bread"]="246 1/fixture/216",["Salad"]="20 1 22 1 419 1/fixture/196",["Pizza"]="246 1 256 1 424 1/fixture/206",["Pancakes"]="246 1 176 1/fixture/211",["Omelet"]="176 1 184 1/fixture/195",["Sashimi"]="-4 1/fixture/227",["Maki"]="-4 1 152 1/fixture/228",["FishTaco"]="130 1 266 1/fixture/213",["TroutSoup"]="138 1 153 1/fixture/219",["LobsterBisque"]="715 1 184 1/fixture/730",["Seafoam"]="267 1 269 1 814 1/fixture/265",["Cookie"]="246 1 245 1 176 1/fixture/223"};
  Game1.content=new TestContent();AfkFood.Invalidate();
  foreach(string id in new[]{"128","163","715","716","717","718","719","720","721","722","723","152","153","157","372","227","228","213","219","730","265"})
   foreach(int quality in new[]{0,2,4})Check(!AfkFishingService.Food(new SObject(id,1,quality:quality)),"native edible aquatic/seafood object refused at quality "+quality+": "+id);
  foreach(string id in new[]{"216","196","206","211","195","223","424","24","192","254","256","258","260"})
   Check(AfkFishingService.Food(new SObject(id,1)),"native verified nonseafood food/crop accepted: "+id);
  Check(!AfkFishingService.Food(new SObject("248",1)),"native inedible crop refused");
  var quest=new SObject("216",1);quest.questItem.Value=true;Check(!AfkFishingService.Food(quest),"native quest-protected bread refused");
  Check(!AfkFishingService.Food(new SObject("unknownFood",1)),"unknown edible provenance fails closed");
  HostTransactions=true;Who.UniqueMultiplayerID=119;
  Reset();FoodChest=new Chest(true);Map.objects[new(5,6)]=FoodChest;
  var fish=new SObject("128",3,quality:4);var seaweed=new SObject("152",4);var sashimi=new SObject("227",5);var bread=new SObject("216",3);var salad=new SObject("196",4);
  FoodChest.Items.Add(fish);FoodChest.Items.Add(seaweed);FoodChest.Items.Add(sashimi);FoodChest.Items.Add(bread);FoodChest.Items.Add(salad);
  Who.Stamina=Who.MaxStamina*.09f;Eaten=0;F8();
  Check(Data.Enabled&&Who.isEating&&Eaten==1&&EatenId=="216"&&bread.Stack==2&&salad.Stack==4,"mixed chest skips aquatic slots and consumes first legal food exactly once");
  Check(fish.Stack==3&&seaweed.Stack==4&&sashimi.Stack==5&&FoodLockHeld&&!FoodChest.GetMutex().IsLocked(),"native chest mutex held during consumption and released with fish untouched");
  Run("Tick");Check(Eaten==1&&bread.Stack==2,"next tick during eating cannot consume twice");
  Ready();RejectEating=true;Clock(20);Run("Tick");Check(bread.Stack==2&&Eaten==1,"native eating refusal restores exact food stack");RejectEating=false;
  Ready();FailEating=true;Clock(25);Run("Tick");
  Check(bread.Stack==2&&fish.Stack==3&&!FoodChest.GetMutex().IsLocked(),"injected food start exception rolls back and releases mutex");FailEating=false;
  Reset();FoodChest=new Chest(true);Map.objects[new(5,6)]=FoodChest;FoodChest.Items.Add(new SObject("128",7));FoodChest.Items.Add(new SObject("152",9));
  Who.Stamina=Who.MaxStamina*.09f;Eaten=0;F8();Check(Data.Enabled&&Eaten==0&&BeginCalls==1&&FoodChest.Items[0].Stack==7,"fish-only chest is no available food and does not eat outputs");
  Ready();Who.Stamina=0;Clock(20);Run("Tick");Check(Field<bool>("HomeIntent")&&Eaten==0,"exhaustion with aquatic-only chest starts return home");
  Reset();FoodChest=new Chest(true);Map.objects[new(5,6)]=FoodChest;F8();
  for(int slot=1;slot<Who.Items.Count;slot++)Who.Items[slot]=new SObject("388",999);
  var newFish=new SObject("128",2,quality:4);Capture(newFish);Ready();Who.Stamina=Who.MaxStamina*.09f;Eaten=0;Clock(20);Run("Tick");
  Check(Pending.Count==0&&FoodChest.Items.Where(i=>i?.ItemId=="128").Sum(i=>i.Stack)==2&&Eaten==0,"fresh caught overflow is stored under mutex and never selected as food");
  Reset();Who.Stamina=Who.MaxStamina*.09f;Who.Items[2]=new SObject("216",3);Eaten=0;F8();Check(Eaten==1&&Who.Items[2].Stack==2,"no linked chest still eats safe carried food under current backpack-first policy");
  Reset();FoodChest=new Chest(true);Map.objects[new(5,6)]=FoodChest;FoodChest.Items.Add(new SObject("216",3));FoodChest.GetMutex().RequestLock();FoodChest.GetMutex().Update(Map);Who.Stamina=Who.MaxStamina*.09f;Eaten=0;F8();
  Check(Eaten==0&&FoodChest.Items[0].Stack==3,"busy chest does not consume or steal a mutex");FoodChest.GetMutex().ReleaseLock();
  Reset();HostTransactions=false;
 }
}

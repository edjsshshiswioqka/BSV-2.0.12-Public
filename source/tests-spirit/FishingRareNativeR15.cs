using System.Reflection;
using System.Text.Json;
using BetterStardewValley;
using BetterStardewValley.GiantLobster;
using BetterStardewValley.MutantLobsterKing;
using BetterStardewValley.SpiritFish;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Tools;
using SObject=StardewValley.Object;
internal static class FishingRareNativeR15
{
 static void Storage(Farmer player,IMonitor monitor,Action<bool,string> check)
 {
  var helper=(IModHelper)Recorder.Make(typeof(IModHelper));var mod=new Eddie.FishingMachine.ModEntry(helper,monitor,(IManifest)Recorder.Make(typeof(IManifest)));
  Eddie.FishingMachine.ModEntry.Instance=mod;mod.Production=new Eddie.FishingMachine.ProductionService(mod);
  var h=new Harmony("BSV.R15.Capacity");h.CreateClassProcessor(typeof(Eddie.FishingMachine.MachineCapacityPatch)).Patch();
  Game1.bigCraftableData=new Dictionary<string,StardewValley.GameData.BigCraftables.BigCraftableData>{[Eddie.FishingMachine.ModEntry.Id]=new(){Name="machine",DisplayName="machine"}};
  ItemRegistry.AddTypeDefinition(new StardewValley.ItemTypeDefinitions.BigCraftableDataDefinition());
  var c=new Chest(true,new(1,1),Eddie.FishingMachine.ModEntry.Id){SpecialChestType=Chest.SpecialChestTypes.BigChest};
  c.modData[Eddie.FishingMachine.ProductionService.StateKey]=new ProductionState().Save();
  for(int i=0;i<71;i++)Game1.objectData["r15slot"+i]=new(){Name="slot"+i,DisplayName="slot"+i,Type="Basic",Category=0};
  for(int i=0;i<36;i++)check(c.addItem(ItemRegistry.Create("(O)r15slot"+i))==null,"T19 native insert slot "+i);
  var extra=ItemRegistry.Create("(O)r15slot36");check(ReferenceEquals(c.addItem(extra),extra)&&c.Items.Count==36,"T19 native 37th insertion rejected");
  var menu36=new StardewValley.Menus.InventoryMenu(0,0,false,c.Items,null,36,3);
  check(menu36.inventory.Count==36&&menu36.inventory[35].myID==35,"T19 native 36-slot component/navigation layout");
  for(int i=36;i<70;i++)c.Items.Add(ItemRegistry.Create("(O)r15slot"+i));
  check(c.GetActualCapacity()==36&&c.addItem(ItemRegistry.Create("(O)r15slot70"))?.QualifiedItemId=="(O)r15slot70","T19 overflow does not expand actual insertion limit");
  Eddie.FishingMachine.MachineOverflowMenuPatch.Opening=c;
  int visible=c.GetActualCapacity();Eddie.FishingMachine.MachineOverflowMenuPatch.Opening=null;
  check(visible==70&&c.GetActualCapacity()==36,"T19 only native menu construction exposes overflow");
  var menu70=new StardewValley.Menus.InventoryMenu(0,0,false,c.Items,null,visible,5);
  check(menu70.inventory.Count==70&&menu70.inventory[69].myID==69&&menu70.inventory[69].leftNeighborID==68,"T19 native last overflow slot and controller neighbor");
  var bounds=menu70.inventory[69].bounds;var taken=menu70.leftClick(bounds.Center.X,bounds.Center.Y,null,false);
  check(taken?.QualifiedItemId=="(O)r15slot69"&&c.Items[69]==null,"T19 actual native last-slot click withdraws old overflow");
  h.UnpatchAll(h.Id);
 }
 sealed class Zero:Random{public override double NextDouble()=>0;}
 internal static object Run(Farmer player,Dictionary<string,string> fish,IMonitor monitor,Action<bool,string> check)
 {
  if(Game1.getLocationFromName("Farm") is not Farm){var f=new Farm();f.name.Value="Farm";Game1.game1._locations.Add(f);Game1._locationLookup.Clear();}
  var farm=Game1.getFarm();farm.modData.Remove(MutantWildQuota.Key);MutantWildQuota.Initialize(true,monitor);
  MutantData.Objects(Game1.objectData,k=>k);LobsterData.Objects(Game1.objectData,k=>k);
  Game1.objectData["710"]=new(){Name="CrabPot",DisplayName="CrabPot",Type="Basic",Category=0};
  var sewer=MutantChecks.Fixture();Game1.locationData["Sewer"]=new();
  CrabPot a=new CrabPot{Location=sewer,TileLocation=new(4,4)},b=new CrabPot{Location=sewer,TileLocation=new(5,4)};
  Game1.year=1;Game1.season=Season.Spring;Game1.dayOfMonth=1;
  check(MutantWildQuota.AvailableAt(0),"T17 actual new-save farm has its first allowance");
  LobsterCrabPots.SelectOrJunk(new Zero(),0,a,99,4);
  check(a.heldObject.Value?.QualifiedItemId==MutantLobsterRules.Item&&a.heldObject.Value.Stack==1,"T14/T15 native pot selector commits exactly one despite quantity 99");
  LobsterCrabPots.SelectOrJunk(new Zero(),0,b,99,4);
  check(b.heldObject.Value==null&&!MutantWildQuota.AvailableAt(0),"T15 second native pot cannot win same farm/year");
  int generated=0;
  check(!MutantWildQuota.CommitAt(0,"machine-other-owner",_=>generated++,()=>generated--),"T15 machine and different owner share pot's farm ledger");
  var saved=farm.modData[MutantWildQuota.Key];farm.modData[MutantWildQuota.Key]=saved;
  check(!MutantWildQuota.AvailableAt(0)&&MutantWildQuota.Read().Claims.Count==1,"T15 serialized farm ledger survives reload");
  Game1.year=2;LobsterCrabPots.SelectOrJunk(new Zero(),0,b,9,0);
  check(b.heldObject.Value?.Stack==1&&a.heldObject.Value?.Stack==1&&MutantWildQuota.Read().Claims.Count==2,"T15 new year yields one while old pot remains uncollected");
  Game1.year=3;bool threw=false;
  try{MutantWildQuota.CommitAt(Game1.Date.TotalDays,"failure",_=>{generated++;throw new Exception("fixture");},()=>generated--);}catch{threw=true;}
  check(threw&&generated==0&&MutantWildQuota.AvailableAt(Game1.Date.TotalDays),"T15 output failure rolls back only its own year");
  farm.modData.Remove(MutantWildQuota.Key);MutantWildQuota.Initialize(false,monitor);
  check(!MutantWildQuota.AvailableAt(Game1.Date.TotalDays)&&MutantWildQuota.Read().Claims.Count==0,"T17 missing old history records migration lock, no invented catch");
  MutantWildQuota.Initialize(false,monitor);check(MutantWildQuota.Read().MigrationLockedThroughYear==3,"T17 repeated migration does not move lock boundary");
  Game1.year=4;check(MutantWildQuota.AvailableAt(Game1.Date.TotalDays),"T17 next year opens");
  farm.modData[MutantWildQuota.Key]="{bad";threw=false;try{MutantWildQuota.AvailableAt(Game1.Date.TotalDays);}catch{threw=true;}
  check(threw&&farm.modData[MutantWildQuota.Key]=="{bad","T17 corrupt ledger preserved and fail-closed");farm.modData[MutantWildQuota.Key]=saved;
  Storage(player,monitor,check);
  // Actual production postfix against native constructor field signature, with
  // the audited native +12 Deluxe path as reference. No GPU constructor claim.
  var bar=AccessTools.Method(typeof(LobsterBaitBar),"Apply");
  foreach(int height in new[]{96,176,200,224,500})
  {
   object[] args={GiantLobsterRules.Bait,height,568f-height};bar.Invoke(null,args);
   check((int)args[1]==Math.Min(568,(int)Math.Floor((height+12)*1.2))&&(float)args[2]>=0,"T13 actual bar postfix matches reference "+height);
   object[] plain={"(O)685",height,568f-height};bar.Invoke(null,plain);check((int)plain[1]==height,"T13 different bait unchanged");
  }
  Game1.locationContextData=new Dictionary<string,StardewValley.GameData.LocationContexts.LocationContextData>{["Default"]=new()};
  var location=new GameLocation();location.name.Value="R15WeightFixture";location.map=sewer.map;
  Game1.locationData[location.Name]=new(){Fish=new(){new(){Id="low",ItemId="(O)128",Chance=1,IgnoreFishDataRequirements=true},new(){Id="high",ItemId="(O)129",Chance=1,IgnoreFishDataRequirements=true}}};
  foreach(var (id,price) in new[]{("128",299),("129",300)})
  {
   Game1.objectData[id]=new(){Name=id,DisplayName=id,Type="Fish",Category=-4,Price=price};
   fish[id]="Fixture/20/mixed/1/20/600 2600/spring summer fall winter/both/0/0/1/1/0";
  }
  ItemRegistry.AddTypeDefinition(new StardewValley.ItemTypeDefinitions.ToolDataDefinition());
  Game1.toolData??=new Dictionary<string,StardewValley.GameData.Tools.ToolData>();Game1.toolData["IridiumRod"]=new(){ClassName="StardewValley.Tools.FishingRod",Name="IridiumRod",UpgradeLevel=3};
  var rod=new FishingRod(3);AccessTools.Field(typeof(Tool),"lastUser").SetValue(rod,player);player.Items.Clear();player.Items.Add(rod);player.CurrentToolIndex=0;player.currentLocation=location;player.Position=new Vector2(2*64,4*64);rod.isFishing=true;rod.attachments[0]=ItemRegistry.Create<SObject>(GiantLobsterRules.Bait,1);
  Game1.timeOfDay=1200;Game1.random=new Random(71715);int high=0,low=0;
  const int n=20000;
  for(int i=0;i<n;i++)
  {
   SpiritSelection.Clear();var item=GameLocation.GetFishFromLocationData(location.Name,new(4,4),4,player,false,false,location);
   if(item?.QualifiedItemId=="(O)129")high++;else if(item?.QualifiedItemId=="(O)128")low++;else throw new Exception("Unexpected native ordinary selection");
  }
  check(high+low==n&&Math.Abs(high-n*5d/9)<=6*Math.Sqrt(n*5d/9*4d/9),"T13 real native ordinary selection + production rejection weighting follows 5/9");
  check(rod.GetBait()!.Stack==1,"T11 weight selection never consumes bait a second time");
  // Real per-cast eligibility snapshot and shared manual lease, no actual minigame input.
  location.name.Value="Mountain";Game1.locationData["Mountain"]=Game1.locationData["R15WeightFixture"];
  foreach(int skill in new[]{9,10})foreach(int clock in new[]{1750,1800,2550,2600})foreach(string map in new[]{"Mountain","Beach"})
  {
   player.fishingLevel.Value=skill;Game1.timeOfDay=clock;location.name.Value=map;
   check(SpiritService.Eligible(player,location)==(skill==10&&clock>=1800&&clock<2600&&map=="Mountain"),$"T12 real eligibility {skill}/{clock}/{map}");
  }
  var used=rod.GetBait();rod.attachments[0]=null;player.Items.Add(used);check(!SpiritSelection.HasBait(player),"T11 inventory bait without attachment cannot qualify");
  Game1.objectData["685"]=new(){Name="Bait",DisplayName="Bait",Type="Basic",Category=-21};rod.attachments[0]=ItemRegistry.Create<SObject>("(O)685",1);check(!SpiritSelection.HasBait(player),"T11 ordinary attached bait cannot qualify even with special bait in backpack");
  player.Items.Remove(used);rod.attachments[0]=used;location.name.Value="Mountain";
  Game1.timeOfDay=1800;player.fishingLevel.Value=10;SpiritSelection.Clear();Game1.random=new Zero();
  var tick=AccessTools.Method(typeof(SpiritQuota),"Tick");tick.Invoke(SpiritQuota.Instance,new object[]{0L});
  check(SpiritQuota.CanCatch(player),"T11 host manual lease granted to actual attached bait");
  var spirit=GameLocation.GetFishFromLocationData("Mountain",new(4,4),4,player,false,false,location);
  check(spirit?.QualifiedItemId==SpiritRules.Fish&&SpiritSelection.ValidCapture(rod,player,out _),"T11 last attached bait creates a valid single cast snapshot");
  var landing=AccessTools.Method(typeof(SpiritService),"Landing");object[] landingArgs={rod,SpiritRules.Fish,false,9};
  check((bool)landing.Invoke(null,landingArgs)!&&(int)landingArgs[3]==3,"T12 actual landing guard caps Spirit at three");
  rod.attachments[0]=ItemRegistry.Create<SObject>(GiantLobsterRules.Bait,1);
  check(!SpiritSelection.ValidCapture(rod,player,out _),"T11 replacing bait object invalidates old snapshot");
  SpiritSelection.Clear();check(!SpiritSelection.ValidCapture(rod,player,out _),"T11 reset/reconnect clears snapshot");
  SpiritQuota.Caught(player);check(!SpiritQuota.CanCatch(player),"T12 single successful manual settlement releases lease and consumes annual allowance");
  Game1.year=5;Game1.season=Season.Spring;b.heldObject.Value=null;
  check(MutantWildQuota.CommitAt(Game1.Date.TotalDays,"machine-first",_=>generated++,()=>generated--),"T14 reverse direction machine commits first native farm allowance");
  LobsterCrabPots.SelectOrJunk(new Zero(),0,b,99,0);check(b.heldObject.Value==null,"T14 native pot cannot win after machine claim");
  check(BetterStardewValley.LobsterKing.KingWildQuota.Available(),"T16 mutant annual claim leaves ordinary seasonal quota free");
  string source=BetterStardewValley.LobsterKing.KingWildQuota.Source(b,"machine")+":cycle:0";string kingToken="";
  check(BetterStardewValley.LobsterKing.KingWildQuota.Commit(source,t=>kingToken=t,()=>kingToken=""),"T16 native farm seasonal ledger commits independently");
  var kingLedger=BetterStardewValley.LobsterKing.KingWildQuota.Read();kingLedger.Claims.Single().Committed=false;
  farm.modData[BetterStardewValley.LobsterKing.KingWildQuota.Key]=JsonSerializer.Serialize(kingLedger);
  BetterStardewValley.LobsterKing.KingWildQuota.ReconcileSource(b,"machine",new[]{kingToken});
  check(BetterStardewValley.LobsterKing.KingWildQuota.Read().Claims.Single().Committed,"T16 new cycle/ordinal source suffix reconciles its own evidence");
  Game1.season=Season.Summer;check(BetterStardewValley.LobsterKing.KingWildQuota.Available()&&!MutantWildQuota.AvailableAt(Game1.Date.TotalDays),"T16 next season opens ordinary quota but not mutant year");
  Game1.season=Season.Winter;check(!BetterStardewValley.LobsterKing.KingWildQuota.Available(),"T16 native winter ordinary quota is closed");
  return new{actual_native_selection=n,high,low,expected_high=5d/9,bar_fixture="actual production postfix plus audited native IL; constructor rendering not run",live_game=false,real_multiplayer=false};
 }
}

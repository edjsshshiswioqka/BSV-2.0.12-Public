using BetterStardewValley.AfkFishing;
using HarmonyLib;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewModdingAPI;
using Microsoft.Xna.Framework;
using SObject=StardewValley.Object;
partial class Program
{
 static int FixtureScreenValue;
 static int FixtureScreen { get=>FixtureScreenValue;set{FixtureScreenValue=value;if(Game1.game1!=null)Game1.game1.instanceId=value;typeof(Context).GetProperty("IsWorldReady",Any)!.SetValue(null,true);} }
 static bool ScreenBoundary(ref int __result){__result=FixtureScreen;return false;}
 static void MorningFixture()
 {
  Reset();Who.speed=4;Who.homeLocation.Value=Home.Name;Data.Enabled=true;Data.Phase=AfkPhase.Sleeping;
  Data.Spot=new(){Location=Map.Name,X=5,Y=5,Facing=1,WaterX=9,WaterY=5,RodId=Rod.QualifiedItemId,RodSlot=0};
  Put(Who,Home,3,3);GameDay++;Who.Stamina=Who.MaxStamina;Run("NewDay");
 }
 static void RestEdgeChecks(Harmony h)
 {
  MorningFixture();var blocked=new Chest(true);Map.objects[new(5,5)]=blocked;
  RestUntil(()=>BeginCalls==1);
  Check(Who.currentLocation==Map&&BeginCalls==1&&Data.Spot!.Tile==new TileNode(Map.Name,5,5)&&Who.TilePoint!=new Point(5,5)
    &&Math.Abs(Who.TilePoint.X-5)<=4&&Math.Abs(Who.TilePoint.Y-5)<=4&&Field<AfkArea>("Area").AllGroundValid,
   "blocked original stand retains original intent and uses reachable temporary fishing water with valid chest area");
  Check(ReferenceEquals(Map.objects[new(5,5)],blocked),"alternative stand never removes blocking player object");
  MorningFixture();Who.Items[0]=null;RestUntil(()=>Data.Phase==AfkPhase.PausedMissingRod);
  Check(Data.Enabled&&Data.Phase==AfkPhase.PausedMissingRod&&Data.Spot!.Location==Map.Name&&BeginCalls==0,
   "missing morning rod pauses instead of erasing saved rest session");
  Who.Items[4]=Rod;Clock(Game1.currentGameTime.TotalGameTime.TotalSeconds+3);RestUntil(()=>BeginCalls==1);
  Check(BeginCalls==1&&Who.CurrentToolIndex==4&&Data.Spot!.RodSlot==4,"saved rod found in a new slot resumes morning route and native cast");
  MorningFixture();RestUntil(()=>Field<bool>("WakePrepared"));var walls=Map.Map.GetLayer("Buildings");
  for(int y=0;y<24;y++)for(int x=0;x<8;x++)walls.Tiles[x,y]=new xTile.Tiles.StaticTile(walls,Map.Map.TileSheets[0],xTile.Tiles.BlendMode.Alpha,1);
  Run("TopologyChanged",Map); // explicit map-change notification, as the game content/world events provide
  for(int i=0;i<5;i++){int before=Field<int>("RouteFailures");Clock(Game1.currentGameTime.TotalGameTime.TotalSeconds+65);RestUntil(()=>Field<int>("RouteFailures")>before,200);}
  Check(Data.Enabled&&Data.Phase==AfkPhase.WaitingForRoute&&Field<int>("RouteFailures")>=5&&Who.currentLocation==Home&&BeginCalls==0,
   "no legal same-water stand stops after bounded retries, never changes map/fishing region or teleports");
  for(int y=0;y<24;y++)for(int x=0;x<8;x++)walls.Tiles[x,y]=null;
  Run("TopologyChanged",Map);RestUntil(()=>BeginCalls==1);
  Check(BeginCalls==1&&Who.currentLocation==Map,"restored geometry re-arms bounded morning route and resumes");

  // Use the actual native home resolver and actual Cabin object. Only screen context is
  // injected; this is not a claim of running two network clients or rendering split screen.
  Reset();Who.homeLocation.Value=Home.Name;F8();Ready();Who.Stamina=Who.MaxStamina*.05f;Run("Tick");Run("Persist");
  var primary=Who;var primaryRod=Rod;var primaryState=Data;var primarySpot=Data.Spot!.Tile;
  var screen=typeof(Context).GetProperty("ScreenId")!.GetMethod!;h.Patch(screen,prefix:new(typeof(Program),nameof(ScreenBoundary)));
  FixtureScreen=1;Who=new Farmer{UniqueMultiplayerID=202};Who.buffs.SetOwner(Who);Who.buffs.Dirty=false;
  typeof(Game1).GetField("_player",Any)!.SetValue(null,Who);Reset();Who.speed=4;
  var cabin=new Cabin();cabin.name.Value="Cabin";cabin.uniqueName.Value="Cabin_202";cabin.map=Fixture();
  var cabinBuilding=new StardewValley.Buildings.Building();cabinBuilding.tileX.Value=1;cabinBuilding.tileY.Value=6;
  cabinBuilding.humanDoor.Value=new Point(1,1);cabinBuilding.indoors.Value=cabin;Map.buildings.Add(cabinBuilding);
  cabin.isStructure.Value=true;cabin.map.Properties["EntryLocation"]="5 6";
  Game1.netWorldState.Value.farmhandData[Who.UniqueMultiplayerID]=Who;cabin.farmhandReference.Value=Who;
  Who.homeLocation.Value=cabin.NameOrUniqueName;
  var bed=new BedFurniture();bed.bedType=BedFurniture.BedType.Single;bed.TileLocation=new(2,2);
  bed.boundingBox.Value=new Rectangle(128,128,128,192);cabin.furniture.Add(bed);
  Map.warps.Add(new Warp(2,7,cabin.NameOrUniqueName,5,6,false));cabin.warps.Add(new Warp(6,6,Map.Name,2,5,false));
  Check(Utility.getHomeOfFarmer(Who)==cabin&&new AfkWorld(Who).Bed()?.Map==cabin.NameOrUniqueName,
   "native farmer homeLocation resolves farmhand cabin and its own bed, not main farmhouse");
  F8();Console.Error.WriteLine($"CABIN fixture screen={Context.ScreenId} local={Who.IsLocalPlayer} world={Context.IsWorldReady} enabled={Data.Enabled} notices={string.Join(",",Notices)}");Ready();Who.Stamina=Who.MaxStamina*.05f;Run("Tick");int sleeps=SleepCalls;
  RestUntil(()=>SleepCalls>sleeps);
  Check(Data.Enabled&&Who.currentLocation==cabin&&SleepCalls==sleeps+1&&Data.Spot!.Location==Map.Name,
   "second-screen farmer follows own native cabin route and submits only their sleep request");
  var secondary=Who;var secondaryState=Data;
  FixtureScreen=0;Who=primary;Rod=primaryRod;typeof(Game1).GetField("_player",Any)!.SetValue(null,Who);Game1.game1.instanceGameLocation=Who.currentLocation;
  Check(ReferenceEquals(Data,primaryState)&&Data.RestReason==AfkRestReason.LowEnergy&&Data.Spot!.Tile==primarySpot
    &&Utility.getHomeOfFarmer(Who)==Home,"first-screen session and home remain independent of farmhand sleep");
  Run("Stop");Check(secondaryState.Enabled&&secondaryState.Phase==AfkPhase.Sleeping,"manual stop on first screen cannot cancel the other player's rest");
  // Actual title callback clears all screens, including references and pending routes.
  var title=Recorder.Events.Single(e=>e.Name=="add_ReturnedToTitle"&&e.Handler.Method.DeclaringType!.FullName!.StartsWith(typeof(AfkFishingService).FullName!)).Handler;
  title.DynamicInvoke(null,null);Check(!Data.Enabled&&Field<Chest?>("Chest")==null,"title clears current AFK session and binding");
  FixtureScreen=1;Check(!Data.Enabled&&Field<Chest?>("Chest")==null,"title clears other split-screen session and binding");
  FixtureScreen=0;h.Unpatch(screen,HarmonyPatchType.Prefix,h.Id);Map.buildings.Remove(cabinBuilding);Reset();
 }
}

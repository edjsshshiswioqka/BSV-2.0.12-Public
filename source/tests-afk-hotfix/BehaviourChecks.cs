using BetterStardewValley.AfkFishing;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.Menus;
using StardewValley.Tools;
using SObject=StardewValley.Object;
partial class Program
{
 static FarmHouse Home=null!;
 static bool Master(ref Farmer __result){__result=Who;return false;}
 static int EmergencyWarps,SleepCalls,Eaten;
 static bool ReturnLong;static int LegalWarps;
 static bool LegalWarpBoundary(Farmer __instance,Warp __0,int __1)
 {
  LegalWarps++;Run("StopWalking");
  Put(__instance,Game1.getLocationFromName(__0.TargetName),__0.TargetX,__0.TargetY);
  Run("InvalidatePath",0d);return false;
 }
 static bool HomeBoundary(ref FarmHouse __result){__result=Home;return false;}
 static bool SleepBoundary(string questionAndAnswer,ref bool __result){if(questionAndAnswer=="Sleep_Yes")SleepCalls++;__result=true;return false;}
 static bool EmergencyRequestBoundary(){EmergencyWarps++;return false;}
 static bool EmergencyBoundary(string locationName,int tileX,int tileY,int facingDirectionAfterWarp){EmergencyWarps++;return false;}
 static bool LongRoute(ref List<RouteStep>? __result)
 {__result=Enumerable.Range(0,ReturnLong?200:1).Select(i=>new RouteStep(new("FishingFixture",i,1),new("FishingFixture",i+1,1))).ToList();return false;}
 static Vector2 Pos=>(Vector2)typeof(Farmer).GetProperty("Position")!.GetValue(Who)!;
 static void Put(Farmer p,GameLocation map,int x,int y)
 {p.currentLocation=map;Game1.game1.instanceGameLocation=map;typeof(Farmer).GetProperty("Position")!.SetValue(p,new Vector2(x*64,y*64+16));}
 static void Field(string field,object? value)=>Session.GetType().GetField(field,Any)!.SetValue(Session,value);
 static T Field<T>(string field)=>(T)Session.GetType().GetField(field,Any)!.GetValue(Session)!;
 static void NativeFrame(TileNode target)
 {
  Clock(Game1.currentGameTime.TotalGameTime.TotalSeconds+.016);
  // The game's input polling halts an actor when there is no real key down.
  // Both native movement and native animation methods run unmodified.
  Who.Halt();Run("RequestWalk",target,false);
  Who.MovePosition(Game1.currentGameTime,Game1.viewport,Who.currentLocation);
  Who.updateMovementAnimation(Game1.currentGameTime);
 }
 static void BehaviourChecks(Harmony h,Action<Type,string,string,Type[]> patch)
 {
  patch(typeof(Game1),"get_MasterPlayer",nameof(Master),Type.EmptyTypes);
  patch(typeof(FarmHouse),"updateMap",nameof(Skip),Type.EmptyTypes);
  patch(typeof(Furniture),"minutesElapsed",nameof(Skip),new[]{typeof(int)});
  patch(typeof(FarmerSprite),"checkForFootstep",nameof(Skip),Type.EmptyTypes);
  patch(typeof(Game1),"shouldTimePass",nameof(Yes),new[]{typeof(bool)});
  Game1.content=new TestContent();
  Home=new FarmHouse();Home.name.Value="FarmHouse";Home.map=Fixture();Game1.locations.Add(Home);
  h.Unpatch(typeof(AfkWorld).GetMethod("Route",Any)!,HarmonyPatchType.Prefix,h.Id);
  patch(typeof(GameLocation),"answerDialogueAction",nameof(SleepBoundary),new[]{typeof(string),typeof(string[])});
  patch(typeof(Game1),"warpFarmer",nameof(EmergencyBoundary),new[]{typeof(string),typeof(int),typeof(int),typeof(int)});
  patch(typeof(Game1),"warpFarmer",nameof(EmergencyRequestBoundary),new[]{typeof(LocationRequest),typeof(int),typeof(int),typeof(int)});
  // Bed data and geometry are native; no wake coordinate helper is invoked by BSV.
  var bed=new BedFurniture();bed.bedType=BedFurniture.BedType.Single;bed.TileLocation=new(2,2);
  bed.boundingBox.Value=new Rectangle(2*64,2*64,2*64,3*64);Home.furniture.Add(bed);
  Reset();Ready();Put(Who,Home,3,3);Who.FarmerSprite.SetOwner(Who);
  Data.Enabled=true;Data.Phase=AfkPhase.ReturningToSpot;
  Check(AfkWorld.StaticClear(Home,new(3,3),Who),"native bed middle row is a legal route origin");
  var world=new AfkWorld(Who);Check(world.Route(new(Home.Name,3,3),new(Home.Name,6,6))!=null,"bed-to-room exit has a legal local route without position edits");
  foreach(var pair in new[]{(0,3,1),(1,6,3),(2,3,6),(3,0,3)})
  {
   Put(Who,Home,3,3);Run("StopWalking");Who.FarmerSprite.PauseForSingleAnimation=false;Who.CanMove=true;
   // Avoid bed head/foot collision in the four-direction animation fixture.
   Home.furniture.Clear();
   var frames=new HashSet<int>();Vector2 before=Pos;
   for(int i=0;i<24;i++){NativeFrame(new(Home.Name,pair.Item2,pair.Item3));frames.Add(Who.FarmerSprite.CurrentFrame);}
   Check(Pos!=before&&Who.FacingDirection==pair.Item1&&frames.Count>1,"native movement advances visible walking frames and correct direction "+pair.Item1);
   Run("StopWalking");Vector2 stop=Pos;Who.updateMovementAnimation(Game1.currentGameTime);
   Check(Who.movementDirections.Count==0&&Pos==stop,"stop cancels native walk flags in direction "+pair.Item1);
  }
  Put(Who,Home,3,3);for(int i=0;i<16;i++)NativeFrame(new(Home.Name,4,3));NativeFrame(new(Home.Name,4,4));
  Check(Who.FacingDirection==2,"turn updates native direction rather than sliding backwards");
  Run("InvalidatePath",0d);Check(Who.movementDirections.Count==0&&!Field<bool>("WalkActive"),"repath cancels stale walk intent");
  NativeFrame(new(Home.Name,4,3));Check(Field<bool>("WalkActive"),"repath resumes through the native movement controller");
  var wall=Home.Map.GetLayer("Buildings");wall.Tiles[4,3]=new xTile.Tiles.StaticTile(wall,Home.Map.TileSheets[0],xTile.Tiles.BlendMode.Alpha,1);
  Vector2 beforeWall=Pos;NativeFrame(new(Home.Name,4,3));
  Check(Pos==beforeWall&&!Field<bool>("WalkActive"),"new static obstacle halts displacement and animation pending replan");wall.Tiles[4,3]=null;
  Data.Phase=AfkPhase.WaitingForRoute;Run("StopWalking");Check(Who.movementDirections.Count==0,"WaitingForRoute is idle");
  Home.furniture.Add(bed);
  Home.warps.Add(new Warp(6,6,Map.Name,3,5,false));
  Map.warps.Add(new Warp(3,7,Home.Name,5,6,false));
  patch(typeof(Farmer),"warpFarmer",nameof(LegalWarpBoundary),new[]{typeof(Warp),typeof(int)});
  for(int day=1;day<=3;day++)
  {
   Reset();GameDay=day;Data.Enabled=true;Data.Phase=AfkPhase.Sleeping;Data.Spot=new(){Location=Map.Name,X=5,Y=5,Facing=1,RodSlot=0,RodId=Rod.QualifiedItemId,WaterX=9,WaterY=5};
   Data.NightPolicy=AfkNightPolicy.LateManual;Data.SafetyDay=GameDay-2;Data.WakePending=true;
   Put(Who,Home,3,3);Who.CanMove=false;Who.freezePause=999;Who.FarmerSprite.PauseForSingleAnimation=true;
   Run("NewDay");Check(Data.Enabled&&Data.Phase==AfkPhase.WakingUp&&Data.NightPolicy==AfkNightPolicy.Protected,"day "+day+" preserves AFK and creates protected morning continuation");
   Game1.freezeControls=true;Run("ResumeAfterWake");Run("ResumeAfterWake");
   Check(!Who.CanMove&&Data.WakePending,"day "+day+" waits for world control initialization");Game1.freezeControls=false;
   Game1.activeClickableMenu=(IClickableMenu)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(LevelUpMenu));
   Run("ResumeAfterWake");Check(Data.WakePending,"day "+day+" does not dismiss a player-decision menu");Game1.activeClickableMenu=null;
   Vector2 wakePos=Pos;Run("ResumeAfterWake");Check(Data.WakePending&&!Who.CanMove&&Who.freezePause==999,"day "+day+" does not erase a native timer");
   Who.freezePause=0; // native timer expiration is an explicit headless boundary
   Run("ResumeAfterWake");
   Check(Data.WakePending&&Data.Phase==AfkPhase.LeavingBed&&Who.CanMove&&Who.freezePause==0&&!Who.FarmerSprite.PauseForSingleAnimation&&Pos==wakePos,"day "+day+" releases its sleep lock, schedules bedside departure and retains recovery intent without coordinate edits");
   for(int i=0;i<38;i++)NativeFrame(new(Home.Name,4,3));
   Check(Who.TilePoint.X>=4,"day "+day+" legally walks out of native bed collision row");
   for(int i=0;i<900&&BeginCalls==0;i++)
   {
    Clock(Game1.currentGameTime.TotalGameTime.TotalSeconds+.016);
    Run("Tick");Who.Halt();Who.MovePosition(Game1.currentGameTime,Game1.viewport,Who.currentLocation);
    Who.updateMovementAnimation(Game1.currentGameTime);
   }
   Check(Who.currentLocation==Map&&BeginCalls==1&&LastPower==1&&Who.FacingDirection==1,"day "+day+" native walk and legal warp boundary returns to spot and MAX casts");
   Ready();
   Run("Persist");Run("Load");Check(Data.Enabled&&Data.Spot?.Location==Map.Name&&Data.Phase==AfkPhase.ResumeCountdown,"day "+day+" save/load preserves enabled snapshot with cancellation window");
   Run("Stop");Run("NewDay");Check(!Data.Enabled&&!Data.WakePending,"day "+day+" explicit disable prevents morning resume");
  }
  Reset();Data.Enabled=true;Field("HomeEstimateReady",false);Game1.timeOfDay=600;
  Check(!(bool)typeof(AfkFishingService).GetMethod("ReturnDue",Any)!.Invoke(Service,null)!,"morning unknown route estimate cannot send the farmer back to bed");
  // Activation uses route-derived budget. Boundary routes isolate distance policy here;
  // actual tree/chest/gate/multi-map shortest paths are exercised by collision/path suites.
  h.Unpatch(typeof(AfkWorld).GetMethod("Route",Any)!,HarmonyPatchType.Prefix,h.Id);
  patch(typeof(AfkWorld),"Route",nameof(LongRoute),new[]{typeof(TileNode),typeof(TileNode)});
  Reset();ReturnLong=true;Game1.timeOfDay=2540;F8();
  Check(Data.Enabled&&Data.NightPolicy==AfkNightPolicy.LateManual&&BeginCalls==0&&Field<bool>("HomeIntent")&&Notices.Contains("afk.late-manual")&&Game1.activeClickableMenu==null,"1:40 long-route F8 accepts, warns without confirmation, returns with no cast");
  Field("HomeEstimate",double.PositiveInfinity);Run("TryEmergencyReturn");Check(EmergencyWarps==0,"late manual route loss never triggers emergency warp");
  Game1.timeOfDay=2600;Run("TryEmergencyReturn");Check(Data.Enabled&&EmergencyWarps==0,"late manual leaves 2AM vanilla pass-out unmodified and AFK enabled");
  Reset();ReturnLong=false;Game1.timeOfDay=2540;F8();
  Check(Data.Enabled&&Data.NightPolicy==AfkNightPolicy.Protected&&BeginCalls==0&&!Notices.Contains("afk.late-manual"),"same 1:40 short-route activation remains protected and immediately returns");
  Reset();ReturnLong=true;Game1.timeOfDay=900;F8();Ready();Game1.timeOfDay=2540;Run("StartHome");Field("HomeEstimate",double.PositiveInfinity);
  Run("TryEmergencyReturn");Check(Data.NightPolicy==AfkNightPolicy.Protected&&EmergencyWarps==1&&Data.EmergencyUsedDay==GameDay-1,"normal session route failure stays protected and invokes own-bed emergency once");
  Run("TryEmergencyReturn");Check(EmergencyWarps==1,"duplicate ticks cannot repeat an emergency warp");
  Reset();Data.Enabled=true;Data.Spot=new(){Location=Map.Name,X=5,Y=5,Facing=1,RodSlot=0,RodId=Rod.QualifiedItemId,WaterX=9,WaterY=5};
  h.Unpatch(typeof(AfkWorld).GetMethod("Bed",Any)!,HarmonyPatchType.Prefix,h.Id);
  Put(Who,Map,5,5);Run("StartHome");int beforeSleep=SleepCalls;
  for(int i=0;i<1200&&SleepCalls==beforeSleep;i++)
  {
   Clock(Game1.currentGameTime.TotalGameTime.TotalSeconds+.016);Run("Tick");
   Who.Halt();Who.MovePosition(Game1.currentGameTime,Game1.viewport,Who.currentLocation);Who.updateMovementAnimation(Game1.currentGameTime);
  }
  Check(SleepCalls==beforeSleep+1&&Data.Phase==AfkPhase.Sleeping&&Data.WakePending&&Who.currentLocation==Home&&Who.movementDirections.Count==0,"normal legal home route ends at own bed with native sleep request and idle animation");
  Reset();ReturnLong=false;
  h.Unpatch(typeof(AfkWorld).GetMethod("Route",Any)!,HarmonyPatchType.Prefix,h.Id);
  patch(typeof(AfkWorld),"Route",nameof(Route),new[]{typeof(TileNode),typeof(TileNode)});
 }
}

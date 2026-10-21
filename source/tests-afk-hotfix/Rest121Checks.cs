using BetterStardewValley.AfkFishing;
using HarmonyLib;
using StardewValley;
using StardewValley.Tools;
using StardewValley.Objects;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Enchantments;
using Microsoft.Xna.Framework;
using SObject=StardewValley.Object;
partial class Program
{
 static void RestFrame()
 {
  Clock(Game1.currentGameTime.TotalGameTime.TotalSeconds+.016);
  // Native event polling normally belongs to FishingRod.tickUpdate. Flush just that
  // event here; graphics/minigame render loops are not available in this headless host.
  var evt=typeof(FishingRod).GetField("doneFishingEvent",Any)!.GetValue(Rod)!;
  evt.GetType().GetMethod("Poll")!.Invoke(evt,null);
  Run("Tick");Who.Halt();Who.MovePosition(Game1.currentGameTime,Game1.viewport,Who.currentLocation);
  Who.updateMovementAnimation(Game1.currentGameTime);
 }
 static int RestUntil(Func<bool> complete,int frames=2200)
 {int n=0;while(!complete()&&n++<frames){RestFrame();if(n%500==0)Console.Error.WriteLine($"REST121 frame={n} phase={Data.Phase} tile={Who.TilePoint} using={Who.UsingTool} cast={Rod.isCasting} move={Who.CanMove} home={Field<bool>("HomeIntent")} goal={Field<TileNode?>("Goal")} target={Field<TileNode?>("WalkTarget")} failures={Field<int>("RouteFailures")}");}return n;}
 static void Rest121Checks(Harmony h)
 {
  foreach(int maximum in new[]{100,270,508})
  {
   Reset();Who.maxStamina.Value=maximum;F8();Ready();int before=BeginCalls;
   Who.Stamina=maximum*.05f;Run("Tick");
   Check(Field<bool>("HomeIntent")&&Data.Enabled&&Data.RestReason==AfkRestReason.LowEnergy&&BeginCalls==before,
    "at five percent of actual maximum "+maximum+" latch rest, keep session and block new cast");
   int generation=Field<int>("Generation");Run("Tick");Run("Tick");
   Check(Field<int>("Generation")==generation,"one low-energy event has one latched generation "+maximum);
   Reset();Who.maxStamina.Value=maximum;Who.Stamina=maximum*.05f+1;F8();
   Check(Data.RestReason==AfkRestReason.LowEnergy&&BeginCalls==0,"predict native next-cast cost before crossing reserve "+maximum);
  }
  Who.maxStamina.Value=270;
  Reset();Rod.enchantments.Add(new EfficientToolEnchantment());Who.Stamina=Who.MaxStamina*.05f+1;F8();
  Check(BeginCalls==1&&AfkFishingService.NextCastCost(Who,Rod)==0,"Efficient rod has native zero cast cost, no invented energy charge");
  Ready();Who.Stamina=Who.MaxStamina*.05f;Run("Tick");
  Check(Data.RestReason==AfkRestReason.LowEnergy,"Efficient still respects actual five-percent rest threshold");
  Reset();Who.fishingLevel.Value=10;
  Check(AfkFishingService.NextCastCost(Who,Rod)==7,"forecast uses actual native level-dependent cost, not fixed stamina points");
  Reset();F8();Who.Stamina=Who.MaxStamina*.05f;Rod.castedButBobberStillInAir=true;
  Run("Tick");
  Check(Data.Enabled&&Field<bool>("HomeIntent")&&!Field<TileNode?>("WalkTarget").HasValue&&BeginCalls==1,
   "low energy in cast flight latches rest without starting movement or another cast");
  Ready();Rod.lastUser=Who;Rod.fishCaught=true;Rod.whichFish=ItemRegistry.GetMetadata("(O)128");Rod.fishQuality=2;Rod.numberOfFishCaught=2;
  for(int i=1;i<Who.Items.Count;i++)Who.Items[i]=new SObject("388",999);
  Run("Tick");
  Check(Pending.Count==1&&Pending[0].ItemId=="128"&&Pending[0].Stack==2&&Pending[0].Quality==2,
   "native doneHoldingFish commits low-energy in-flight catch once into existing durable overflow when backpack full");
  RestFrame();RestFrame();
  Check(Data.Phase==AfkPhase.ReturningHome&&Pending.Sum(i=>i?.Stack??0)==2&&BeginCalls==1,
   "full backpack cannot block rest; native event finalized fish is not granted twice");
  Run("Persist");Run("Load");Check(Pending.Count==1&&Pending[0].Stack==2,"pending low-energy catch survives session reload without re-settlement");
  Game1.timeOfDay=2550;Field("HomeEstimate",double.PositiveInfinity);int emergency=EmergencyWarps;Run("TryEmergencyReturn");
  Check(EmergencyWarps==emergency,"low-energy route failure never uses emergency teleport");

  // Restore a pending return with only persisted scalars, no retained Session references.
  Reset();F8();Ready();Who.Stamina=Who.MaxStamina*.05f;Run("Tick");var spot=Data.Spot!.Tile;
  Run("Persist");Run("Load");Run("NewDay");
  Check(Field<bool>("HomeIntent")&&Data.RestReason==AfkRestReason.LowEnergy&&Data.Spot!.Tile==spot&&Data.Phase==AfkPhase.ResumeCountdown,
   "same-day save/load and load-DayStarted preserve home intent and original spot");
  Field("HomeEstimateReady",true);Field("HomeEstimate",double.PositiveInfinity);Game1.timeOfDay=600;
  Check(!(bool)typeof(AfkFishingService).GetMethod("ReturnDue",Any)!.Invoke(Service,null)!,
   "unknown/unreachable home estimate is not a fabricated morning bedtime");
  Run("Stop");Run("Persist");GameDay++;Run("Load");Run("NewDay");
  Check(!Data.Enabled&&!Data.ReturnToSpot&&!Data.WakePending&&!Field<bool>("HomeIntent"),
   "manual cancel survives save/load and next day without reclaiming player");

  h.Unpatch(typeof(AfkFishingService).GetMethod("UpdateReturnBudget",Any)!,HarmonyPatchType.Prefix,h.Id);
  h.Unpatch(typeof(AfkWorld).GetMethod("Route",Any)!,HarmonyPatchType.Prefix,h.Id);
  h.Unpatch(typeof(Utility).GetMethod("getHomeOfFarmer",Any)!,HarmonyPatchType.Prefix,h.Id);
  // Full dispatcher cycle; unlike the historical test there is no injected walk out
  // of bed. The ONLY transport boundary is the already verified native warp request.
  Reset();Who.speed=4;Who.homeLocation.Value=Home.Name;GameDay=12;HostTransactions=true;
  var box=AddChest(5,6);box.Items.Add(new SObject("128",997));for(int i=1;i<36;i++)box.Items.Add(new SObject("388",999));var backup=AddChest(4,6);
  F8();Ready();Check(Data.Enabled&&BeginCalls==1,"full rest cycle starts with real F8 handler and optional nine-cell boxes");
  for(int cycle=0;cycle<3;cycle++)
  {
   var saved=Data.Spot!.Tile;int cast=BeginCalls;
   Capture(new SObject("128",7));Ready();for(int i=0;i<5&&Field<AfkCatchLedger>("Catches").HasOutput(Who.Items);i++)Run("Tick");
   // Storage was allowed before threshold; once latched no remote box work is allowed.
   Who.Stamina=Who.MaxStamina*.05f;Run("Tick");int beforeSleep=SleepCalls;
   Console.Error.WriteLine($"REST121 threshold map={Who.currentLocation.Name} tile={Who.TilePoint} phase={Data.Phase} using={Who.UsingTool} cast={Rod.isCasting} fishing={Rod.isFishing} home={Field<bool>("HomeIntent")} chest={Field<Chest?>("Chest")?.TileLocation}");
   Check(Data.Enabled&&Data.RestReason==AfkRestReason.LowEnergy&&Data.Spot!.Tile==saved,"cycle "+cycle+" low energy preserves fishing snapshot");
   if(cycle==0)
   {
    // Persist mid-route, then discard all in-memory route/box state and finish same trip.
    RestUntil(()=>Who.currentLocation==Home,1000);Run("Persist");Run("Load");Run("NewDay");
    Check(Field<bool>("HomeIntent")&&Field<Chest?>("Chest")==null&&Data.Spot!.Tile==saved,
     "mid-return load in own house resumes rest, no stale chest/route references");
   }
   int frames=RestUntil(()=>SleepCalls>beforeSleep);
   Console.Error.WriteLine($"REST121 cycle={cycle} homeFrames={frames} map={Who.currentLocation.Name} phase={Data.Phase} rest={Data.RestReason}");
   Check(SleepCalls==beforeSleep+1&&Who.currentLocation==Home&&Data.Phase==AfkPhase.Sleeping&&Data.Enabled&&BeginCalls==cast,
    "cycle "+cycle+" native movement/cross-map route reaches own bed and submits exactly one native sleep request");
   RestFrame();RestFrame();Check(SleepCalls==beforeSleep+1,"cycle "+cycle+" waiting for overnight cannot resubmit sleep");
   // Model the native world's day transition, not BSV's own day-skip; production never
   // changes date/stamina/coordinates. Use real native bed wake positioning in the fixture.
   GameDay++;Game1.timeOfDay=600;Who.Stamina=Who.MaxStamina;Ready();
   Put(Who,Home,3,3);BedFurniture.ShiftPositionForBed(Who);
   Who.CanMove=false;Who.freezePause=999;Who.FarmerSprite.PauseForSingleAnimation=true;
   Run("NewDay");Game1.freezeControls=true;Run("Tick");Run("Tick");
   Check(Data.WakePending&&!Who.CanMove,"cycle "+cycle+" wake waits for initialization barrier");Game1.freezeControls=false;Who.freezePause=0; // explicit expiration of native wake timer

   if(cycle==1){Run("Persist");Run("Load");Run("NewDay");}
   int previousStored=box.Items.Concat(backup.Items).Where(i=>i?.ItemId=="128").Sum(i=>i.Stack);
   frames=RestUntil(()=>BeginCalls>cast);
   Console.Error.WriteLine($"REST121 cycle={cycle} spotFrames={frames} map={Who.currentLocation.Name} tile={Who.TilePoint} phase={Data.Phase}");
   Check(Who.currentLocation==Map&&BeginCalls==cast+1&&LastPower==1&&Data.Spot!.Tile==saved&&!Data.ReturnToSpot&&Data.RestReason==AfkRestReason.None,
    "cycle "+cycle+" full dispatcher wakes/leaves bed/crosses map/returns and MAX casts once");
   Check(box.Items.Concat(backup.Items).Where(i=>i?.ItemId=="128").Sum(i=>i.Stack)==previousStored,
    "cycle "+cycle+" travel/wake does not repeat settled inventory transfer");
   Check(Field<AfkArea>("Area").Center==AfkWorld.At(Who)&&Field<AfkArea>("Area").AllGroundValid,
    "cycle "+cycle+" current nine-cell UI and binding are re-evaluated at arrival");
   Ready();
  }
  Check(box.Items.Where(i=>i?.ItemId=="128").Sum(i=>i.Stack)==999&&backup.Items.Where(i=>i?.ItemId=="128").Sum(i=>i.Stack)==19,
   "three-day catches conserved across primary partial-stack and backup chest");
  Run("Stop");GameDay++;Run("NewDay");int casts=BeginCalls;for(int i=0;i<10;i++)RestFrame();
  Check(!Data.Enabled&&BeginCalls==casts&&!Field<TileNode?>("WalkTarget").HasValue,"manual stop after multiple days cancels all future automatic movement/casts");
  RestEdgeChecks(h);
  HostTransactions=false;Reset();
 }
}

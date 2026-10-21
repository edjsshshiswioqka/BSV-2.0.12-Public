using BetterStardewValley.AfkFishing;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using System.Runtime.Serialization;
partial class Program
{
 static int BedQuestions;
 // Only graphical menu construction/localized text are replaced; native touch and
 // native shouldTimePass run. A real DialogueBox instance still blocks the clock.
 static bool BedMenuConstructor(){BedQuestions++;return false;}
 static bool SleepText(string path,ref string __result)
 {if(!path.EndsWith("FarmHouse_Bed_GoToSleep"))return true;__result="Sleep?";return false;}
 static bool SleepResponses(ref Response[] __result)
 {__result=new[]{new Response("Yes","Yes"),new Response("No","No")};return false;}
 static void Morning127Checks(Harmony h)
 {
  h.Unpatch(typeof(Game1).GetMethod("shouldTimePass",Any)!,HarmonyPatchType.Prefix,h.Id);
  h.Unpatch(typeof(AfkFishingService).GetProperty("SafeWorld",Any)!.GetMethod!,HarmonyPatchType.Prefix,h.Id);
  h.Patch(typeof(LocalizedContentManager).GetMethod("LoadString",new[]{typeof(string)})!,prefix:new(typeof(Program),nameof(SleepText)));
  h.Patch(typeof(GameLocation).GetMethod("createYesNoResponses",Any)!,prefix:new(typeof(Program),nameof(SleepResponses)));
  h.Patch(typeof(DialogueBox).GetConstructor(new[]{typeof(string),typeof(Response[]),typeof(int)})!,prefix:new(typeof(Program),nameof(BedMenuConstructor)));
  // Framework graphics/fade host is absent; set up its neutral state, not a BSV gate override.
  var fade=typeof(Game1).GetField("screenFade",Any)!;if(fade.GetValue(null)==null)fade.SetValue(null,FormatterServices.GetUninitializedObject(fade.FieldType));
  typeof(StardewModdingAPI.Context).GetProperty("IsWorldReady",Any)!.SetValue(null,true);
  Reset();Game1.eventUp=false;Game1.newDay=false;Game1.multiplayerMode=0;Game1.paused=false;Game1.isTimePaused=false;
  Game1.freezeControls=false;Game1.showingEndOfNightStuff=false;Game1.pauseTime=0;Game1.overlayMenu=null;
  Game1.fadeToBlack=false;Game1.farmEvent=null;Game1.currentMinigame=null;Who.passedOut=false;
  Who.homeLocation.Value=Home.Name;Game1.content=new TestContent();
  Put(Who,Home,3,3);Data.Enabled=true;Data.Phase=AfkPhase.Sleeping;Data.WakePending=true;Data.ReturnToSpot=true;
  Data.SessionDay=GameDay-2;Data.Spot=new(){Location=Map.Name,X=5,Y=5,Facing=1,RodSlot=0,RodId=Rod.QualifiedItemId,WaterX=9,WaterY=5};
  Run("NewDay");Run("Tick");Run("Tick");Who.hasMoved=true;
  Home.performTouchAction(new[]{"Sleep"},Who.Tile);
  Console.Error.WriteLine($"MORNING127 native bed touch: phase={Data.Phase} menu={Game1.activeClickableMenu?.GetType().Name??"none"} canMove={Who.CanMove} shouldTimePass={Game1.shouldTimePass()} questions={BedQuestions}");
  if(System.Environment.GetEnvironmentVariable("BSV_AFK_MORNING_BASELINE")=="1")
  {
   for(int i=0;i<120;i++){Clock(20+i*.016);Run("Tick");}
   Check(Game1.activeClickableMenu is DialogueBox&&!Game1.shouldTimePass()&&Field<TileNode?>("WalkTarget")==null,
    "BASELINE reproduced: native bed re-entry prompt suspends clock and prevents return travel indefinitely");
   return;
  }
  Check(BedQuestions==0&&Game1.activeClickableMenu==null&&Game1.shouldTimePass(),"morning native bed touch cannot reopen sleep prompt; real clock predicate stays true");
  // Keep the real time predicate and real SafeWorld for an additional three dispatcher
  // cycles. Sleep/day loading and warp transport remain explicit headless boundaries.
  Reset();Who.homeLocation.Value=Home.Name;Who.speed=4;GameDay=20;HostTransactions=true;
  h.Unpatch(typeof(AfkWorld).GetMethod("Route",Any)!,HarmonyPatchType.Prefix,h.Id);
  h.Unpatch(typeof(AfkWorld).GetMethod("Bed",Any)!,HarmonyPatchType.Prefix,h.Id);
  h.Unpatch(typeof(Utility).GetMethod("getHomeOfFarmer",Any)!,HarmonyPatchType.Prefix,h.Id);
  Home.map=Fixture();Home.furniture.Clear();
  var bed=new BedFurniture();bed.bedType=BedFurniture.BedType.Single;bed.TileLocation=new(2,2);bed.boundingBox.Value=new(128,128,128,192);Home.furniture.Add(bed);
  Home.warps.Clear();Home.warps.Add(new Warp(6,6,Map.Name,3,5,false));
  Map.warps.Clear();Map.warps.Add(new Warp(3,7,Home.Name,5,6,false));
  Map.Map.GetLayer("Back").Tiles[6,5].Properties["Water"]="T";
  var morningChest=AddChest(4,5);
  F8();Ready();
  for(int cycle=0;cycle<3;cycle++)
  {
   int cast=BeginCalls,sleep=SleepCalls;var saved=Data.Spot!.Tile;
   Who.Stamina=Who.MaxStamina*.05f;Run("Tick");
   int steps=RestUntil(()=>SleepCalls>sleep);
   Check(SleepCalls==sleep+1&&Who.currentLocation==Home&&Data.Enabled,"native-clock cycle "+cycle+" low-energy route reaches own bed");
   Console.Error.WriteLine($"MORNING127 day={GameDay} rest steps={steps} phase={Data.Phase} saved={saved}");
   Map.objects.Clear();morningChest=AddChest(4,5); // each new day uses a different actual chest instance
   GameDay++;Game1.timeOfDay=600;Who.Stamina=Who.MaxStamina;Ready();
   Put(Who,Home,3,3);BedFurniture.ShiftPositionForBed(Who);Who.CanMove=false;
   Who.FarmerSprite.PauseForSingleAnimation=true;
   if(cycle!=2)Run("NewDay"); // third cycle tests date reconciliation when event was missed.
   if(cycle==1){Run("Persist");Run("Load");Run("NewDay");}
   if(cycle!=2){int generation=Field<int>("Generation");Run("NewDay");Check(Field<int>("Generation")==generation,"duplicate same-day event cannot restart morning task");}
   int questions=BedQuestions;int frames=0;bool leftBed=false,clockObserved=false;
   for(;frames<2200&&BeginCalls==cast;frames++)
   {
    Clock(Game1.currentGameTime.TotalGameTime.TotalSeconds+.016);
    Run("Tick");
    if(Data.Phase is AfkPhase.WakingUp or AfkPhase.ResumeCountdown)continue;
    if(!Field<bool>("HomeIntent")&&Who.currentLocation==Home&&BedFurniture.IsBedHere(Home,Who.TilePoint.X,Who.TilePoint.Y))
    {Who.hasMoved=true;Home.performTouchAction(new[]{"Sleep"},Who.Tile);}
    Check(Game1.activeClickableMenu==null,"native-clock morning "+cycle+" has no bed re-entry menu");
    if(Game1.shouldTimePass())
    {
     clockObserved=true;Who.Halt();Who.MovePosition(Game1.currentGameTime,Game1.viewport,Who.currentLocation);Who.updateMovementAnimation(Game1.currentGameTime);
    }
    else throw new Exception("Native clock stopped during return: "+typeof(AfkFishingService).GetMethod("RuntimeState",Any)!.Invoke(Service,null));
    if(Who.currentLocation!=Home||!BedFurniture.IsBedHere(Home,Who.TilePoint.X,Who.TilePoint.Y))leftBed=true;
   }
   Check(clockObserved&&leftBed&&BedQuestions==questions&&Who.currentLocation==Map&&BeginCalls==cast+1&&LastPower==1&&Data.Spot!.Tile==saved,
    "native-clock cycle "+cycle+" leaves bed, crosses map, resumes one MAX cast without F8");
   Check(ReferenceEquals(Field<Chest?>("Chest"),morningChest)&&Field<AfkArea>("Area").Cells.Any(c=>c.Ground==AfkGround.Water),
    "shoreline morning "+cycle+" reconnects the new chest instance despite adjacent water");
   Console.Error.WriteLine($"MORNING127 day={GameDay} return frames={frames} map={Who.currentLocation.Name} tile={Who.TilePoint} cast={BeginCalls} clockAllowed={Game1.shouldTimePass()}");
   Ready();
  }
  // Player menus/foreign pauses remain owned by vanilla.
  Put(Who,Home,3,3);Data.WakePending=true;Data.ReturnToSpot=true;Data.Phase=AfkPhase.WakingUp;
  var menu=(LevelUpMenu)FormatterServices.GetUninitializedObject(typeof(LevelUpMenu));Game1.activeClickableMenu=menu;
  Run("Tick");Check(ReferenceEquals(Game1.activeClickableMenu,menu)&&!Game1.shouldTimePass(),"profession/menu pause is not force-cleared");
  Game1.activeClickableMenu=null;Game1.paused=true;Run("Tick");Check(Game1.paused&&Data.WakePending,"explicit native pause is respected");Game1.paused=false;
  Field("WakeOwnsSleep",false);Field("WakeReadyFrames",0);Who.CanMove=false;
  Run("Tick");Run("Tick");Check(!Who.CanMove&&Data.WakePending,"unowned movement lock is not cleared");Who.CanMove=true;
  Run("Stop");Run("Persist");GameDay++;Run("Load");Run("NewDay");
  Check(!Data.Enabled&&!Data.WakePending&&!Field<TileNode?>("WalkTarget").HasValue,"manual cancel persists with no automatic return task");
  Ready();Who.hasMoved=true;Home.performTouchAction(new[]{"Sleep"},Who.Tile);
  Check(Game1.activeClickableMenu is DialogueBox&&!Who.CanMove&&!Game1.shouldTimePass(),"manual bed touch retains native sleep prompt and pause");
  Game1.activeClickableMenu=null;Ready();Game1.dialogueUp=false;Reset();
  var settings=(BetterStardewValley.ModConfig)typeof(AfkFishingService).GetField("Config",Any)!.GetValue(Service)!;
  foreach(bool disableAll in new[]{false,true})
  {
   Reset();Put(Who,Home,3,3);Data.Enabled=true;Data.Phase=AfkPhase.Sleeping;Data.WakePending=true;
   Data.SessionDay=Game1.Date.TotalDays-1;Data.Spot=new(){Location=Map.Name,X=5,Y=5,Facing=1,RodSlot=0,RodId=Rod.QualifiedItemId,WaterX=9,WaterY=5};
   var spot=Data.Spot;Run("NewDay");Who.CanMove=false;Who.FarmerSprite.PauseForSingleAnimation=true;
   settings.AfkFishing.ReturnToFishingSpotNextDay=false;settings.AfkFishing.Enabled=!disableAll;
   Run("Tick");Run("Tick");
   Check(!Data.Enabled&&!Data.ReturnToSpot&&!Data.WakePending&&Who.CanMove&&!Who.FarmerSprite.PauseForSingleAnimation
      &&ReferenceEquals(Data.Spot,spot)&&!Field<TileNode?>("WalkTarget").HasValue,
      "morning configuration OFF cancels trip, preserves spot and releases only owned sleep lock; total OFF="+disableAll);
   settings.AfkFishing.Enabled=true;settings.AfkFishing.ReturnToFishingSpotNextDay=true;
   Run("Tick");Check(!Data.Enabled,"reenabling preference cannot resume a canceled session");
  }
  Reset();
 }
}

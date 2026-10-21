using BetterStardewValley.AfkFishing;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using System.Runtime.Serialization;
using SObject=StardewValley.Object;
partial class Program
{
 static void RecoveryR9Checks()
 {
  MorningFixture();Put(Who,Home,5,6);Who.CanMove=false;Who.freezePause=0;Who.noMovementPause=0;
  Who.FarmerSprite.PauseForSingleAnimation=true;var before=Who.Position;Run("Tick");
  Check(Who.CanMove&&!Who.FarmerSprite.PauseForSingleAnimation&&Who.Position==before&&Data.WakePending,
   "R9 native beside-bed wake releases only recorded sleep control without teleport or premature completion");
  Run("Stop");Check(!Data.Enabled&&!Field<TileNode?>("WalkTarget").HasValue,"R9 cancel after wake removes departure intent");

  MorningFixture();Data.SleepRequestedDay=Game1.Date.TotalDays;Data.PlayerId=Who.UniqueMultiplayerID;
  Field("WakeOwnsSleep",false);Run("Stop");Run("Persist");GameDay++;
  Run("Load");Run("NewDay");Who.CanMove=false;Who.freezePause=0;Who.FarmerSprite.PauseForSingleAnimation=true;Run("Tick");
  Console.Error.WriteLine($"CANCELED_SLEEP enabled={Data.Enabled} move={Who.CanMove} animation={Who.FarmerSprite.PauseForSingleAnimation} cast={BeginCalls} marker={Data.SleepRequestedDay} fixtureday={GameDay} nativeday={Game1.Date.TotalDays} owns={Field<bool>("WakeOwnsSleep")} released={Field<bool>("WakeControlReleased")} blocker={typeof(AfkFishingService).GetMethod("WakeBlocker",Any)!.Invoke(Service,null)}");
  Check(!Data.Enabled&&Who.CanMove&&!Who.FarmerSprite.PauseForSingleAnimation&&BeginCalls==0&&Data.SleepRequestedDay==-1,
   "R9 cancel before committed sleep finishes survives reload and cleans own wake lock without resuming AFK");

  MorningFixture();Data.PlayerId=Who.UniqueMultiplayerID+999;Data.SleepRequestedDay=Game1.Date.TotalDays-1;Run("Persist");Run("Load");
  Who.CanMove=false;Run("Tick");Check(!Data.Enabled&&!Who.CanMove&&Data.SleepRequestedDay==-1,
   "R9 another player's serialized sleep receipt cannot claim local controls");Who.CanMove=true;

  MorningFixture();var menu=(LevelUpMenu)FormatterServices.GetUninitializedObject(typeof(LevelUpMenu));
  Game1.activeClickableMenu=menu;Pending.Add(new SObject("128",7));Run("Tick");double started=Field<double>("RecoveryStartedAt");
  Clock(started+90);Run("Tick");Game1.activeClickableMenu=null;Game1.paused=true;Run("Tick");
  Check(Data.Enabled&&Field<double>("RecoveryStartedAt")==started,"R9 changing blockers cannot restart total wake deadline");
  Clock(started+181);Run("Tick");
  Check(!Data.Enabled&&Game1.paused&&Pending.Sum(i=>i?.Stack??0)==7&&BeginCalls==0&&!Field<TileNode?>("WalkTarget").HasValue,
   "R9 180-second wake deadline stops once, preserves committed output and respects foreign pause despite frozen world clock");
  Game1.paused=false;

  MorningFixture();RestUntil(()=>Field<bool>("WakePrepared"));started=Field<double>("RecoveryStartedAt");
  Game1.activeClickableMenu=menu;Clock(started+599);Run("Tick");Check(Data.Enabled,"R9 departure has finite 600-second total budget");
  Clock(started+601);Run("Tick");Check(!Data.Enabled&&ReferenceEquals(Game1.activeClickableMenu,menu)&&BeginCalls==0,
   "R9 total return deadline includes departed travel and does not close profession menu");Game1.activeClickableMenu=null;

  MorningFixture();Field("WakeOwnsSleep",false);Who.CanMove=false;Run("Tick");started=Field<double>("RecoveryStartedAt");
  Who.controller=(StardewValley.Pathfinding.PathFindController)FormatterServices.GetUninitializedObject(typeof(StardewValley.Pathfinding.PathFindController));
  Who.setMoving(2);Clock(started+181);Run("Tick");
  Check(!Data.Enabled&&!Who.CanMove&&Who.controller!=null&&Who.movementDirections.Contains(1),
   "R9 timeout cannot release foreign control lock or erase external movement controller input");
  Who.controller=null;Who.CanMove=true;Who.Halt();Reset();
 }
}

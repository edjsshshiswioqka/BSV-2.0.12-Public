using StardewValley;
using StardewValley.Tools;
using StardewValley.Objects;
using Microsoft.Xna.Framework;
using StardewModdingAPI;

namespace BetterStardewValley.AfkFishing;

internal sealed partial class AfkFishingService
{
    private static int CurrentDay => Game1.Date.TotalDays;
    private static bool ValidSnapshot(FishingSpot? spot) => spot != null && !string.IsNullOrWhiteSpace(spot.Location)
        && spot.Location.Length <= 256 && spot.Facing is >= 0 and <= 3
        && spot.X is >= 0 and < 10000 && spot.Y is >= 0 and < 10000
        && spot.WaterX is >= 0 and < 10000 && spot.WaterY is >= 0 and < 10000
        && spot.RodSlot >= 0 && !string.IsNullOrWhiteSpace(spot.RodId);
    private void RestoreSafetyDay()
    {
        var data = Current.Data;
        if (data.SafetyDay != CurrentDay || data.NightPolicy == AfkNightPolicy.Unclassified)
        {
            // Only a manual Enable may create LateManual. Loading, failures and automatic
            // morning continuation are protected, including old v1.1.8 saves.
            data.NightPolicy = AfkNightPolicy.Protected;
            data.SafetyDay = CurrentDay;
        }
    }
    private static bool CommittedCatch(FishingRod? rod) => rod != null && (rod.fishCaught || rod.hit || rod.isReeling
        || rod.pullingOutOfWater || rod.showingTreasure || rod.castedButBobberStillInAir || rod.isCasting || rod.isTimingCast);
    // Only suppress the native automatic bed touch while our local morning trip owns
    // control. Manual input cancels AFK before the next touch; nighttime Sleep_Yes stays native.
    private static bool BeforeBedTouch(GameLocation __instance, string[] action, Vector2 playerStandingPosition)
    {
        var service = Instance;
        if (service == null || action.Length != 1 || action[0] != "Sleep" || !Context.IsWorldReady) return true;
        var s = service.Current; var who = Game1.player;
        bool morning = s.Data.WakePending && s.Data.SessionDay < CurrentDay
            || s.Data.Phase is AfkPhase.WakingUp or AfkPhase.ResumeCountdown
            || Traveling(s.Data.Phase);
        if (!s.Data.Enabled || s.HomeIntent || !s.Data.ReturnToSpot || !morning
            || !who.IsLocalPlayer || who.controller != null || who.UsingTool || who.isEating
            || !service.SafeWorld || Game1.showingEndOfNightStuff || Game1.activeClickableMenu != null
            || Game1.freezeControls || !ReferenceEquals(__instance, who.currentLocation)
            || !ReferenceEquals(__instance, Utility.getHomeOfFarmer(who))
            || !BedFurniture.IsBedHere(__instance, (int)playerStandingPosition.X, (int)playerStandingPosition.Y)) return true;
        service.WalkReason("morning bed touch suppressed: continuing departure");
        return false;
    }
    private string? WakeBlocker()
    {
        if (!Context.IsWorldReady || Current.Saving) return "world/save initialization";
        if (Game1.showingEndOfNightStuff) return "end-of-night sequence";
        if (Game1.activeClickableMenu != null) return "menu:" + Game1.activeClickableMenu.GetType().Name;
        if (!SafeWorld || Game1.farmEvent != null) return "event/fade/new-day/minigame";
        if (Game1.paused || Game1.isTimePaused || Game1.overlayMenu != null || Game1.HostPaused) return "native pause";
        if (Game1.freezeControls || Game1.pauseTime > 0) return "native control/pause timer";
        if (Game1.locationRequest != null) return "location transition";
        var who = Game1.player;
        if (!who.IsLocalPlayer || who.currentLocation?.Map == null || !ReferenceEquals(Game1.currentLocation, who.currentLocation)) return "player/location initialization";
        if (who.controller != null) return "external movement controller";
        if (who.isEating || who.passedOut || CommittedCatch(who.CurrentTool as FishingRod) || who.UsingTool) return "native action/catch";
        return null;
    }
    private bool WakeWorldReady() => WakeBlocker() == null;
    private void WakeWait(string reason)
    {
        var s = Current;
        StopWalking();
        if (s.WakeWaitReason != reason)
        {
            s.WakeWaitReason = reason; s.WakeWaitAt = Now; s.WakeWaitWarned = false;
            Monitor.Log("BSV AFK wake waiting: " + reason + "; " + RuntimeState(), LogLevel.Debug);
        }
        if (!s.WakeWaitWarned && Now - s.WakeWaitAt >= 10)
        {
            s.WakeWaitWarned = true;
            Monitor.Log("BSV AFK wake still waiting: " + reason + "; " + RuntimeState(), LogLevel.Warn);
            Notice("afk.wake-blocked");
        }
    }
    private string RuntimeState()
    {
        var who = Game1.player; var s = Current;
        return $"player={who.UniqueMultiplayerID} day={CurrentDay} time={Game1.timeOfDay} phase={s.Data.Phase}"
            + $" wake={s.Data.WakePending} home={s.HomeIntent} generation={s.Generation} map={who.currentLocation?.NameOrUniqueName} tile={who.TilePoint}"
            + $" canMove={who.CanMove} usingTool={who.UsingTool} freezePause={who.freezePause} noMovementPause={who.noMovementPause}"
            + $" bed={who.isInBed.Value} menu={Game1.activeClickableMenu?.GetType().Name ?? "none"} controller={who.controller?.GetType().Name ?? "none"}"
            + $" paused={Game1.paused} timePaused={Game1.isTimePaused} controlsFrozen={Game1.freezeControls} pauseTime={Game1.pauseTime}"
            + $" night={Game1.showingEndOfNightStuff} newDay={Game1.newDay} event={Game1.eventUp} clockAllowed={Game1.shouldTimePass()}";
    }
    private bool RecoveryWithinDeadline()
    {
        var s = Current;
        if (s.RecoveryStartedAt < 0) s.RecoveryStartedAt = Now;
        double limit = s.WakePrepared ? 600 : 180;
        if (Now - s.RecoveryStartedAt < limit) return true;
        Monitor.Log("BSV AFK recovery deadline reached; pending output retained; " + RuntimeState(), LogLevel.Warn);
        Stop(); Notice("afk.recovery-stopped"); return false;
    }
    private void ReleaseOwnedWakeControl()
    {
        var s = Current;
        // A canceled, already-committed sleep can finish after Stop or SaveLoaded.
        // Keep only its cleanup receipt; it must never re-enable movement or fishing.
        if (s.Data.SleepRequestedDay >= 0 && s.Data.SleepRequestedDay < CurrentDay
            && (s.Data.PlayerId == 0 || s.Data.PlayerId == Game1.player.UniqueMultiplayerID))
            s.WakeOwnsSleep = true;
        if (!s.WakeOwnsSleep || s.WakeControlReleased || Game1.timeOfDay >= 2600 || WakeBlocker() != null) return;
        var who = Game1.player;
        // Native ApplyWakeUpPosition may put the farmer BESIDE the bed. Requiring the
        // current tile to still be a Bed made that valid wake position a permanent lock.
        // A positive native timer or foreign controller is not ours to erase.
        if (who.currentLocation != Utility.getHomeOfFarmer(who) || who.freezePause > 0 || who.noMovementPause > 0) return;
        who.CanMove = true;
        who.FarmerSprite.PauseForSingleAnimation = false;
        s.WakeControlReleased = true; s.Data.SleepRequestedDay = -1;
    }
    private bool LeaveBed()
    {
        var s = Current; var who = Game1.player; var map = who.currentLocation;
        if (!BedFurniture.IsBedHere(map, who.TilePoint.X, who.TilePoint.Y))
        { StopWalking(); s.BedExit = null; return true; }
        s.Data.Phase = AfkPhase.LeavingBed;
        if (!s.BedExit.HasValue || !AfkWorld.CanStand(map, new(s.BedExit.Value.X, s.BedExit.Value.Y), who))
        {
            StopWalking(); s.BedExit = null;
            if (Now < s.RetryAt) return false;
            s.RetryAt = Now + .5;
            // Enumerate the actual bed's sides, including custom-sized/shifted beds.
            // Every candidate and the complete swept path must satisfy native collision.
            var bed = BedFurniture.GetBedAtTile(map, who.TilePoint.X, who.TilePoint.Y);
            if (bed == null) { WakeWait("bed geometry unavailable"); return false; }
            var box = bed.GetBoundingBox();
            var candidates = new List<TileNode>();
            for (int y = box.Top / 64; y <= (box.Bottom - 1) / 64; y++)
                foreach (int x in new[] { box.Left / 64 - 1, (box.Right - 1) / 64 + 1 })
                    candidates.Add(new(map.NameOrUniqueName, x, y));
            s.BedExit = candidates.Where(t => !BedFurniture.IsBedHere(map,t.X,t.Y)
                && AfkWorld.CanStand(map,new(t.X,t.Y),who)
                && AfkWorld.SegmentClear(map,who.StandingPixel,new(t.X*64+32,t.Y*64+32),who))
                .OrderBy(t => Vector2.DistanceSquared(new(t.X*64+32,t.Y*64+32),who.StandingPixel.ToVector2()))
                .Select(t => (TileNode?)t).FirstOrDefault();
            if (!s.BedExit.HasValue) { WakeWait("no legal bedside departure"); return false; }
            Persist();
        }
        RequestWalk(s.BedExit.Value, false);
        return false;
    }
    private bool ResumeAfterWake()
    {
        var s = Current;
        if (!s.Data.Enabled || !s.Data.WakePending || s.WakePrepared) return true;
        string? blocker = WakeBlocker();
        if (blocker != null) { WakeWait(blocker); return false; }
        var who = Game1.player;
        ReleaseOwnedWakeControl();
        if (!who.CanMove || who.freezePause > 0 || who.noMovementPause > 0)
        { WakeWait("native movement timer or unowned control lock"); return false; }
        s.Data.ReturnToSpot = Config.AfkFishing.ReturnToFishingSpotNextDay;
        if (!s.Data.ReturnToSpot) { Stop(); Notice("afk.return-disabled"); return false; }
        if (!LeaveBed()) return false;
        s.WakeWaitReason = ""; s.WakeWaitWarned = false;
        var previous = s.Data.Phase;
        s.WakePrepared = true; s.HomeIntent = false; s.Goal = null; s.Rod = null;
        s.RouteFailures = 0; s.RecoveryCount = 0; s.RecoveryOrigin = null; s.ActorWaitTile = null; s.ActorWaitBox = null;
        ClearChest(); s.Area = null; s.Nearby.Clear(); s.Standby.Clear();
        s.Navigation.Clear(); s.Data.Phase = AfkPhase.ReturningToSpot; InvalidatePath();
        Persist(); TraceState(previous, "left bed: fresh route; recovery intent retained until native cast starts"); Notice("afk.returning-spot");
        return true;
    }
}

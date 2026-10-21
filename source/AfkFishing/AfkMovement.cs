using Microsoft.Xna.Framework;
using StardewValley;

namespace BetterStardewValley.AfkFishing;

internal sealed partial class AfkFishingService
{
    private bool CanWalk(Farmer who) => Current.Data.Enabled && !Current.WorldAssetsDirty && Traveling(Current.Data.Phase)
        && who == Game1.player && who.IsLocalPlayer && SafeWorld && Game1.activeClickableMenu == null
        && !Game1.freezeControls && Game1.locationRequest == null && who.CanMove && !who.UsingTool
        && !who.isEating && who.freezePause <= 0 && who.controller == null;
    private void StopWalking()
    {
        var s = Current; bool owned = s.WalkTarget.HasValue || s.WalkActive;
        s.WalkTarget = null; s.WalkActive = false; s.MoveLimit = 0; s.WalkVector = Vector2.Zero;
        // A native/event controller taking ownership must not have its directions
        // cleared by our old travel task while we relinquish control.
        if (Game1.player is Farmer who && who.IsLocalPlayer && who.controller == null)
        {
            if (owned) who.Halt();
            if (s.RunOwned) who.setRunning(s.PreviousRunning, false);
        }
        s.RunOwned = false;
    }
    private void RequestWalk(TileNode tile, bool allowExit)
    {
        if (!Current.Data.Enabled || !Traveling(Current.Data.Phase)) return;
        Current.WalkTarget = tile; Current.WalkExit = allowExit;
        Current.WalkGeneration = Current.Generation;
        Current.WalkSetAt = Now; Current.WalkRefreshed = true;
    }
    // Native keyboard polling calls Halt even with no physical key held. Preserve the
    // current native animation ONLY during our short-lived travel intent, not menus/tools.
    private static bool PreserveWalkingAnimation(Farmer __instance)
    {
        var service = Instance;
        if (service == null || !service.Current.WalkActive || !service.CanWalk(__instance)
            || !service.Current.WalkTarget.HasValue || service.Current.WalkGeneration != service.Current.Generation
            || Now - service.Current.WalkSetAt > 0.5) return true;
        // Input polling may call Halt every update when no key is physically held.
        // Keep our current direction set as well as its native animation phase.
        return false;
    }
    private static void BeforeNativeMove(Farmer __instance, out bool __state)
    {
        __state = false;
        var service = Instance;
        if (service == null || __instance != Game1.player || !service.Current.WalkTarget.HasValue) return;
        var s = service.Current; var target = s.WalkTarget.Value;
        if (!service.CanWalk(__instance) || s.WalkGeneration != s.Generation || Now - s.WalkSetAt > 0.5
            || __instance.currentLocation.NameOrUniqueName != target.Map)
        { service.ClearObstacleIntent(); service.StopWalking(); return; }
        var map = __instance.currentLocation; var point = new Point(target.X, target.Y);
        if (!service.StepActorsClear(point))
        { service.StopWalking(); service.ActorBlocked(target); return; }
        if (!s.WalkExit && !AfkWorld.StaticClear(map, point, __instance))
        { service.StopWalking(); service.RecoverRoute(0.2); service.WalkReason("waypoint static collision"); return; }
        if (!s.WalkExit && AfkWorld.ClosedGate(map, point) != null)
        { service.StopWalking(); AfkWorld.TryOpenGate(map, point, __instance); service.RecoverRoute(0.2); return; }
        Point pos = __instance.StandingPixel;
        float dx = target.X * 64 + 32 - pos.X, dy = target.Y * 64 + 32 - pos.Y;
        if (Math.Abs(dx) <= 3 && Math.Abs(dy) <= 3) { service.StopWalking(); return; }
        // Native animation/direction flags, with bounded analogue steering applied to
        // native axis steps below. Never write Farmer.Position or bypass MovePositionImpl.
        var direction = new Vector2(Math.Abs(dx) > 3 ? dx : 0, Math.Abs(dy) > 3 ? dy : 0);
        s.WalkVector = Vector2.Normalize(direction);
        s.MoveLimit = direction.Length();
        // Update only axes which changed. setMoving doesn't itself reset animation,
        // but clearing directions/Halt at every waypoint does produce idle frames.
        int horizontal = direction.X == 0 ? -1 : direction.X > 0 ? 1 : 3;
        int vertical = direction.Y == 0 ? -1 : direction.Y > 0 ? 2 : 0;
        for (int i = __instance.movementDirections.Count - 1; i >= 0; i--)
        {
            int old = __instance.movementDirections[i];
            if (old != horizontal && old != vertical) __instance.setMoving((byte)((1 << old) + 32));
        }
        if (horizontal >= 0 && !__instance.movementDirections.Contains(horizontal)) __instance.setMoving((byte)(1 << horizontal));
        if (vertical >= 0 && !__instance.movementDirections.Contains(vertical)) __instance.setMoving((byte)(1 << vertical));
        if (!s.RunOwned) { s.PreviousRunning = __instance.running; s.RunOwned = true; }
        // No force flag: exhausted, bathing, bridge/event and single-animation rules remain native.
        __instance.setRunning(true, false);
        float speed = __instance.getMovementSpeed();
        var ahead = new Point((int)Math.Round(pos.X + s.WalkVector.X * speed), (int)Math.Round(pos.Y + s.WalkVector.Y * speed));
        if (!s.WalkExit && !AfkWorld.SegmentClear(map, pos, ahead, __instance,s.NativePetPermission&&s.NativePet!=null&&Now-s.NativePetSince<=NativePetAttemptSeconds?s.NativePet:null))
        {
            var sweep = Rectangle.Union(__instance.GetBoundingBox(), new Rectangle(ahead.X - 24, ahead.Y - 16, 48, 32));
            bool actor = AfkWorld.DynamicBlocked(map, sweep, __instance);
            service.StopWalking();
            if (actor) { service.ActorBlocked(target); s.ActorWaitBox = sweep; }
            else { service.RecoverRoute(0.2); service.WalkReason("forward swept footprint blocked"); }
            return;
        }
        s.WalkBefore = __instance.Position; s.WalkMap = map.NameOrUniqueName; s.WalkActive = true;
        __state = true;
        // The ORIGINAL Farmer.MovePosition executes once, in Farmer's own update.
        // Its following updateMovementAnimation consumes these direction flags and speed.
    }
    private static void SteerNativeAxis(Farmer __instance, ref float movementSpeedX, ref float movementSpeedY)
    {
        var service = Instance;
        if (service == null || __instance != Game1.player || !service.Current.WalkActive || !service.CanWalk(__instance)) return;
        var aim = service.Current.WalkVector;
        // The speed hook provides the original total movement budget. Normalize once,
        // not native diagonal reduction followed by a second ratio reduction.
        // All original collision/warp/animation code still runs once.
        movementSpeedX *= Math.Abs(aim.X); movementSpeedY *= Math.Abs(aim.Y);
    }
    private static Exception? AfterNativeMove(Farmer __instance, bool __state, Exception? __exception)
    {
        if (!__state || Instance == null) return __exception;
        var s = Instance.Current; s.MoveLimit = 0;
        if (__exception != null || __instance.currentLocation.NameOrUniqueName != s.WalkMap)
        { Instance.ClearObstacleIntent(); Instance.StopWalking(); }
        else if (__instance.Position == s.WalkBefore && !(s.NativePetPermission&&s.NativePet!=null&&Now-s.NativePetSince<=NativePetAttemptSeconds))
        {
            // Collision stopped the real movement: do not animate running in place.
            Instance.StopWalking();
        }
        return __exception;
    }
}

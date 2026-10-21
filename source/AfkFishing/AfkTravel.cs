using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Tools;

namespace BetterStardewValley.AfkFishing;

internal sealed partial class AfkFishingService
{
    private static bool Traveling(AfkPhase phase) => phase is AfkPhase.ReturningHome or AfkPhase.GoingToBed
        or AfkPhase.LeavingBed or AfkPhase.ReturningToSpot or AfkPhase.PlanningRoute or AfkPhase.WaitingForRoute or AfkPhase.PausedNoRoute;
    private void InvalidatePath(double delay = 0)
    {
        var s = Current;
        if (s.Trace != null)
        {
            s.Trace.Invalidations++;
            s.Trace.Reason = new System.Diagnostics.StackTrace(1, false).GetFrame(0)?.GetMethod()?.Name ?? "invalidate";
        }
        StopWalking();
        s.Route = null; s.Search = null; s.RouteIndex = 0; s.Exiting = false; s.SmoothedIndex = -1;
        s.Navigation.ResetWaypoint(); s.RetryAt = Now + delay;
        if (s.Data.Enabled && Traveling(s.Data.Phase)) s.Data.Phase = s.HomeIntent ? AfkPhase.ReturningHome : AfkPhase.ReturningToSpot;
    }
    private void RecoverRoute(double delay)
    {
        var s = Current; var at = AfkWorld.At(Game1.player);
        if (!s.RecoveryOrigin.HasValue || s.RecoveryOrigin.Value.Map != at.Map
            || Math.Abs(s.RecoveryOrigin.Value.X - at.X) + Math.Abs(s.RecoveryOrigin.Value.Y - at.Y) >= 4)
        { s.RecoveryOrigin = at; s.RecoveryCount = 0; }
        if (++s.RecoveryCount >= 8)
        {
            InvalidatePath(); s.RouteFailures = 5; s.Data.Phase = AfkPhase.WaitingForRoute;
            Notice("afk.waiting-route"); return;
        }
        InvalidatePath(delay);
    }
    private void TopologyChanged(GameLocation location)
    {
        if (!Current.Data.Enabled) return;
        Current.HomeCheckAt = 0; Current.HomeSearch = null; Current.AreaAt = 0; Current.RouteFailures = 0; Current.RecoveryCount = 0;
        if (!Traveling(Current.Data.Phase)) return;
        // Also invalidate searches of not-yet-entered maps. No permanent topology cache.
        InvalidatePath(0.15);
    }
    private void StartHome()
    {
        if (Current.HomeIntent && Traveling(Current.Data.Phase)) return;
        var previous = Current.Data.Phase;
        if (Current.Data.RestReason == AfkRestReason.None)
        { Current.Data.RestReason = AfkRestReason.Bedtime; Current.Data.RestDay = CurrentDay; }
        Current.Data.SessionDay = CurrentDay;
        Current.HomeIntent = true; Current.Goal = null; Current.Data.ReturnToSpot = Config.AfkFishing.ReturnToFishingSpotNextDay;
        Current.Data.WakePending = false;
        Current.RouteFailures = 0; Current.RecoveryCount = 0; Current.RecoveryOrigin = null;
        Current.ActorWaitTile = null; Current.ActorWaitBox = null;
        ClearChest();
        Current.Data.Phase = AfkPhase.ReturningHome; InvalidatePath();
        Persist(); TraceState(previous, Current.Data.RestReason.ToString()); Notice("afk.home");
    }
    private void WaitForRoute()
    {
        var s = Current;
        var previous = s.Data.Phase;
        InvalidatePath(Math.Min(60, AfkNavigationMemory.RetrySeconds * Math.Pow(2, Math.Min(5, s.RouteFailures++))));
        s.Data.Phase = AfkPhase.WaitingForRoute;
        Game1.player.Halt(); Persist(); TraceState(previous, "route unavailable; bounded retry " + s.RouteFailures); Notice("afk.waiting-route");
        // Bounded search and exponential retry backoff. No walking while unreachable; a
        // topology change resets the wait immediately. Overnight policy remains unchanged.
    }
    private void Travel()
    {
        var s = Current; var who = Game1.player;
        if (!s.Data.Enabled || !SafeWorld || Game1.activeClickableMenu != null || !who.CanMove || who.UsingTool || who.isEating || who.freezePause > 0) return;
        if (s.RouteFailures >= 5)
        {
            // Five bounded attempts per unchanged obstruction. A moved actor or topology
            // change re-arms the search; otherwise remain stationary, not in an endless loop.
            if (!s.ActorWaitTile.HasValue) {Stop();Notice("afk.recovery-stopped");return;}
            var at = s.ActorWaitTile.Value; var map = new AfkWorld(who).Map(at.Map);
            if (map == null || AfkWorld.DynamicBlocked(map, s.ActorWaitBox ?? AfkWorld.FootBox(new(at.X, at.Y)), who)) {Stop();Notice("afk.recovery-stopped");return;}
            s.RouteFailures = 0; s.RecoveryCount = 0; s.RetryAt = 0; s.ActorWaitTile = null;
        }
        if (Now < s.RetryAt) return;
        if (s.Navigation.Visit(AfkWorld.At(who), Now)) { RecoverRoute(0.2); return; }
        if (s.Route == null)
        {
            if (s.Search == null)
            {
                var world = new AfkWorld(who);
                var goals = s.HomeIntent ? HomeGoals(world) : ReturnSpots(world);
                if (!s.Data.Enabled || s.Data.Phase == AfkPhase.PausedMissingRod) return;
                // Exact straight segment takes priority on the same map if its entire
                // swept footprint is legal. Otherwise retain the ordinary shortest grid search.
                var direct = goals.Where(p => p.Key.Map == who.currentLocation.NameOrUniqueName)
                    .OrderBy(p => p.Value).ThenBy(p => Vector2.DistanceSquared(new(p.Key.X, p.Key.Y), who.Tile)).FirstOrDefault();
                if (goals.Count > 0 && direct.Key.Map != null && direct.Value == goals.Values.Min()
                    && AfkWorld.SegmentClear(who.currentLocation, who.StandingPixel, new(direct.Key.X * 64 + 32, direct.Key.Y * 64 + 32), who))
                {
                    s.Route = new() { new(AfkWorld.At(who), direct.Key) }; s.Goal = direct.Key;
                    s.RouteIndex = 0; s.Navigation.ResetWaypoint(); s.RouteFailures = 0;
                    s.Data.Phase = s.HomeIntent ? AfkPhase.ReturningHome : AfkPhase.ReturningToSpot;
                }
                else
                {
                    s.Search = world.BeginRoute(AfkWorld.At(who), goals, s.Navigation, Now); s.Data.Phase = AfkPhase.PlanningRoute;
                    if (s.Trace != null) s.Trace.Searches++;
                }
            }
            // Incremental search never reports NoPath just because this frame ran out of time.
            if (s.Route == null)
            {
                var status = s.Search!.Advance();
                if (status == RouteSearchStatus.Searching) return;
                if (status is RouteSearchStatus.NoPath or RouteSearchStatus.BudgetExceeded)
                { WaitForRoute(); if (status == RouteSearchStatus.BudgetExceeded) Notice("afk.route-budget"); return; }
                s.Route = s.Search.Route; s.Goal = s.Search.Goal; s.Search = null; s.RouteIndex = 0; s.Exiting = false; s.SmoothedIndex = -1;
                s.Navigation.ResetWaypoint(); s.RouteFailures = 0;
                s.Data.Phase = s.HomeIntent ? AfkPhase.ReturningHome : AfkPhase.ReturningToSpot;
            }
        }
        // Consume reached intermediate nodes in this update and immediately command
        // the next segment. Do not return a frame with WalkRefreshed=false at each
        // corner: Tick would Halt and reset the native walking animation there.
        int advanced = 0;
        while (s.RouteIndex < s.Route!.Count && !s.Route[s.RouteIndex].Transition
            && NearTile(who, s.Route[s.RouteIndex].To) && advanced++ < 16)
        { s.RouteIndex++; s.Exiting = false; s.Navigation.ResetWaypoint(); }
        if (s.RouteIndex >= s.Route.Count)
        {
            if (s.HomeIntent) Sleep();
            else
            {
                var spot = s.Data.Spot!;
                // A target or fishable water can change while we walk. Search afresh if so.
                if (!AfkWorld.CanStand(who.currentLocation, who.TilePoint, who)
                    || !CastAreaValid(who.currentLocation, who.StandingPixel, spot, who))
                { InvalidatePath(0.2); return; }
                // Original requested stand is persistent; this reachable stand is temporary.
                ClearObstacleIntent();
                s.Data.Phase = AfkPhase.ResumingFishing; s.Route = null;
                who.faceDirection(spot.Facing);
                if (Reconnect()) { s.Data.Phase = AfkPhase.Fishing; Persist(); TraceState(AfkPhase.ResumingFishing, "arrived: fresh ground/chest checks; normal cast gates next"); }
            }
            return;
        }
        if (s.SmoothedIndex != s.RouteIndex)
        {
            int end = s.RouteIndex;
            while (end + 1 < s.Route.Count && end - s.RouteIndex < 16 && !s.Route[end].Transition && !s.Route[end].Gate
                && !s.Route[end + 1].Transition && !s.Route[end + 1].Gate) end++;
            // At most five bounded ray sweeps for a new segment, not a search each frame.
            for (int candidate = end; candidate > s.RouteIndex; candidate = s.RouteIndex + (candidate - s.RouteIndex) / 2)
                if (AfkWorld.SegmentClear(who.currentLocation, who.StandingPixel,
                    new(s.Route[candidate].To.X * 64 + 32, s.Route[candidate].To.Y * 64 + 32), who))
                { s.RouteIndex = candidate; s.Route[candidate] = s.Route[candidate] with { From = AfkWorld.At(who) }; break; }
            s.SmoothedIndex = s.RouteIndex;
        }
        var step = s.Route[s.RouteIndex];
        if (who.currentLocation.NameOrUniqueName != step.From.Map) { InvalidatePath(); return; }
        TileNode waypoint = step.Transition
            ? (!s.Exiting && !NearTile(who, step.From) ? step.From : new(step.From.Map, step.DoorX, step.DoorY)) : step.To;
        double distance = Math.Abs(who.StandingPixel.X - (waypoint.X * 64 + 32)) + Math.Abs(who.StandingPixel.Y - (waypoint.Y * 64 + 32));
        s.Navigation.Watch(waypoint, distance, Now);
        if (step.Transition)
        {
            if (!s.Exiting && !NearTile(who, step.From)) MoveToward(step.From);
            else
            {
                // Refresh the live graph immediately before interacting, including moved buildings.
                var links = new AfkWorld(who).Links(step.From.Map);
                var link = links.FirstOrDefault(l => l.Door.X == step.DoorX && l.Door.Y == step.DoorY && l.Target.Map == step.To.Map);
                if (link == null) { InvalidatePath(0.1); return; }
                if (link.Warp)
                { s.Exiting = true; MoveToward(new(step.From.Map, step.DoorX, step.DoorY), allowExit: true); }
                else
                {
                    if (Math.Abs(who.TilePoint.X - step.DoorX) + Math.Abs(who.TilePoint.Y - step.DoorY) > 1) { InvalidatePath(); return; }
                    if (AfkWorld.DynamicBlocked(who.currentLocation, AfkWorld.FootBox(new(step.DoorX, step.DoorY)), who))
                    { ActorBlocked(waypoint); return; }
                    who.faceGeneralDirection(new Vector2(step.DoorX * 64 + 32, step.DoorY * 64 + 32));
                    who.currentLocation.checkAction(new xTile.Dimensions.Location(step.DoorX, step.DoorY), Game1.viewport, who);
                    s.RetryAt = Now + 0.25;
                }
            }
        }
        else MoveToward(step.To);
        if (s.Navigation.Stuck(Now) && !(s.NativePetPermission&&s.NativePet!=null&&Now-s.NativePetSince<=NativePetAttemptSeconds))
        {
            // Effective waypoint distance must improve, not merely the actor's raw position.
            s.Navigation.Penalize(step.To, Now); who.Halt(); RecoverRoute(0.2);
        }
    }
    private static Dictionary<TileNode, int> HomeGoals(AfkWorld world)
    {
        var spot=world.Bed();var result=new Dictionary<TileNode,int>();if(!spot.HasValue)return result;
        var who=Game1.player;var map=world.Map(spot.Value.Map);var bed=Utility.getHomeOfFarmer(who)?.GetPlayerBed();if(map==null||bed==null)return result;
        var box=bed.GetBoundingBox();
        for(int y=box.Top/64;y<=(box.Bottom-1)/64;y++)for(int x=box.Left/64;x<=(box.Right-1)/64;x++)
        {var tile=new TileNode(map.NameOrUniqueName,x,y);if(BedFurniture.IsBedHere(map,x,y)&&AfkWorld.StaticClear(map,new(x,y),who,false))result[tile]=AfkWorld.DynamicBlocked(map,AfkWorld.FootBox(new(x,y)),who)?8:0;}
        if(result.Count==0)result[spot.Value]=0;return result;
    }
    private void ActorBlocked(TileNode tile)
    {
        WalkReason("temporary character obstruction");
        Current.ActorWaitTile = tile; Current.ActorWaitBox = AfkWorld.FootBox(new(tile.X, tile.Y));
        Game1.player.Halt();
        if (!Current.Navigation.WaitForActor(Now))
        { Current.Navigation.Penalize(tile, Now); RecoverRoute(0.2); }
    }
    private static bool NearTile(Farmer who, TileNode target) => who.currentLocation.NameOrUniqueName == target.Map
        && Math.Abs(who.StandingPixel.X - (target.X * 64 + 32)) <= 3
        && Math.Abs(who.StandingPixel.Y - (target.Y * 64 + 32)) <= 3;
    private void MoveToward(TileNode tile, bool allowExit = false)
    {
        var who = Game1.player; var map = who.currentLocation; var point = new Point(tile.X, tile.Y);
        if (!StepActorsClear(point)) { ActorBlocked(tile); return; }
        Current.Navigation.ClearActor();
        if (!allowExit && !AfkWorld.StaticClear(map, point, who)) { who.Halt(); RecoverRoute(0.2); return; }
        if (!allowExit && AfkWorld.ClosedGate(map, point) != null)
        {
            if (!AfkWorld.TryOpenGate(map, point, who))
            { Current.Navigation.Penalize(tile, Now); RecoverRoute(0.25); }
            return; // recheck actual gate collision on the next update before moving.
        }
        RequestWalk(tile, allowExit);
    }
    private Dictionary<TileNode, int> ReturnSpots(AfkWorld world)
    {
        var result = new Dictionary<TileNode, int>(); var spot = Current.Data.Spot; var who = Game1.player;
        if (spot == null) return result;
        if (!RestoreSavedRod()) { Pause(AfkPhase.PausedMissingRod, "afk.rod-missing"); return result; }
        var map = world.Map(spot.Location); if (!AfkWorld.Supported(map)) return result;
        // Rank by radius, then distance to yesterday's stand. Within equal ranks the
        // weighted shortest path chooses the nearest reachable approach, stable edge order.
        foreach (var pair in AfkNavigationCandidates.Around(spot.Tile))
            if (world.Walkable(pair.Key) && CastAreaValid(map!, new Point(pair.Key.X * 64 + 32, pair.Key.Y * 64 + 32), spot, who)) result[pair.Key] = pair.Value;
        if (result.Count == 0) Notice("afk.no-valid-spot");
        return result;
    }
    private void Sleep()
    {
        if (Current.Data.Phase == AfkPhase.Sleeping) return;
        var who = Game1.player; var home = Utility.getHomeOfFarmer(who); var bed = home?.GetPlayerBed();
        if (bed == null || who.currentLocation != home || !BedFurniture.IsBedHere(home, who.TilePoint.X, who.TilePoint.Y))
        { RecoverRoute(0.25); return; }
        ClearObstacleIntent(); StopWalking();
        Current.Data.ReturnToSpot = Config.AfkFishing.ReturnToFishingSpotNextDay; Current.Data.WakePending = true;
        Current.Data.Phase = AfkPhase.GoingToBed; who.Halt(); Persist();
        Current.Data.SleepRequestedDay = CurrentDay; Current.WakeControlReleased = false;
        Current.Data.Phase = AfkPhase.Sleeping; Persist(); TraceState(AfkPhase.GoingToBed, "native Sleep_Yes: local player only");
        if(!home.answerDialogueAction("Sleep_Yes", Array.Empty<string>()))
        {Current.Data.SleepRequestedDay=-1;Current.Data.WakePending=false;Current.Data.Phase=AfkPhase.GoingToBed;RecoverRoute(.5);Persist();WalkReason("native sleep request declined; no fake sleep");}
    }
    private void HandleNightMenus()
    {
        if (Game1.activeClickableMenu is ShippingMenu shipping)
            shipping.receiveLeftClick(shipping.okButton.bounds.Center.X, shipping.okButton.bounds.Center.Y, false);
        else if (Game1.activeClickableMenu is LevelUpMenu level && level.isActive && level.informationUp && !level.isProfessionChooser)
            level.okButtonClicked();
        else if (Game1.activeClickableMenu is LevelUpMenu choice && choice.isProfessionChooser) Notice("afk.profession");
    }
}

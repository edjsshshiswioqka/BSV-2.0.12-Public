using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace BetterStardewValley.AfkFishing;

internal sealed partial class AfkFishingService
{
    private double TimeRemaining() => AfkReturnRules.RemainingSeconds(Game1.timeOfDay, Game1.gameTimeInterval,
        Game1.realMilliSecondsPerGameTenMinutes); // base clock is conservative for slower-map clocks.
    private double EstimateRoute(IEnumerable<RouteStep>? route)
    {
        if (route == null) return double.PositiveInfinity;
        var steps = route as IList<RouteStep> ?? route.ToArray();
        double secondsPerTick = Math.Max(0.001, Game1.currentGameTime.ElapsedGameTime.TotalSeconds);
        double speed = Game1.player.getMovementSpeed() / secondsPerTick;
        return AfkReturnRules.TravelSeconds((int)Math.Ceiling(steps.Sum(p => p.Transition ? 1d : Math.Sqrt(Math.Pow(p.To.X - p.From.X, 2) + Math.Pow(p.To.Y - p.From.Y, 2)))), steps.Count(p => p.Transition), steps.Count(p => p.Gate), speed);
    }
    private void UpdateReturnBudget()
    {
        var s = Current; var who = Game1.player;
        if (s.HomeIntent && s.Route != null)
        { s.HomeEstimate = EstimateRoute(s.Route.Skip(s.RouteIndex)); s.HomeEstimateReady = true; return; }
        if (s.HomeIntent && s.RouteFailures >= 5) { s.HomeEstimate = double.PositiveInfinity; return; }
        var origin = AfkWorld.At(who);
        if (s.HomeOrigin == origin && s.HomeEstimateReady && s.HomeCheckAt > 0 && s.HomeEstimateRoute != null)
        { s.HomeEstimate = EstimateRoute(s.HomeEstimateRoute); return; }
        if (Now < s.HomeCheckAt && s.HomeSearch == null) return;
        if (s.HomeSearch == null)
        {
            var world = new AfkWorld(who);
            s.HomeSearch = world.BeginRoute(AfkWorld.At(who), HomeGoals(world), new(), Now);
        }
        var result = s.HomeSearch.Advance();
        if (result == RouteSearchStatus.Searching) return;
        s.HomeOrigin = origin; s.HomeEstimateRoute = s.HomeSearch.Route;
        s.HomeEstimate = EstimateRoute(s.HomeEstimateRoute);
        s.HomeEstimateReady = true;
        s.HomeSearch = null; s.HomeCheckAt = Now + 3;
    }
    private bool ReturnDue() => Game1.timeOfDay >= AfkRules.HomeTime || (Current.HomeEstimateReady && double.IsFinite(Current.HomeEstimate)
        && AfkReturnRules.Depart(Game1.timeOfDay, TimeRemaining(), Current.HomeEstimate));
    private bool TryEmergencyReturn()
    {
        var s = Current; var who = Game1.player;
        if (s.Data.RestReason == AfkRestReason.LowEnergy || !AfkReturnRules.Emergency(s.Data.NightPolicy, Game1.timeOfDay, TimeRemaining(), s.HomeEstimate,
                s.HomeIntent, s.Data.EmergencyUsedDay == CurrentDay)
            || !SafeWorld || Game1.activeClickableMenu != null || Game1.farmEvent != null || Game1.freezeControls
            || Game1.locationRequest != null || who.UsingTool || who.isEating || who.freezePause > 0
            || CommittedCatch(s.Rod)) return false;
        var home = Utility.getHomeOfFarmer(who); var bed = home?.GetPlayerBed();
        if (home == null || bed == null) { Notice("afk.emergency-unavailable"); return false; }
        Point tile = bed.GetBedSpot();
        if (who.currentLocation == home && BedFurniture.IsBedHere(home, who.TilePoint.X, who.TilePoint.Y))
        { Sleep(); return true; }
        // This is the sole explicit emergency exception: own verified passable bed only,
        // never a navigation edge, tile nudge, obstacle bypass or wake-up workaround.
        if (!BedFurniture.IsBedHere(home, tile.X, tile.Y) || !AfkWorld.CanStand(home, tile, who))
        { Notice("afk.emergency-unavailable"); return false; }
        StopWalking(); s.Data.EmergencyUsedDay = CurrentDay; s.Data.ReturnToSpot = Config.AfkFishing.ReturnToFishingSpotNextDay;
        InvalidatePath(); Persist(); Notice("afk.emergency");
        Game1.warpFarmer(home.NameOrUniqueName, tile.X, tile.Y, 2);
        // Native warp/fade finishes before ordinary Travel -> Sleep_Yes is allowed.
        return true;
    }
}

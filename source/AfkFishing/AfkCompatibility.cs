using StardewModdingAPI;
using StardewValley;

namespace BetterStardewValley.AfkFishing;

internal sealed partial class AfkFishingService
{
    internal static string CompatibilityStatus => Instance == null ? "AFK service unavailable"
        : Instance.RuntimeState() + "; externalControl=" + (Instance.ExternalControlReason() ?? "none");
    // Observe native ownership/pause signals; never clear them or force the clock.
    // Do not use shouldTimePass here: BSV's own native fishing menu may pause it too.
    private string? ExternalControlReason()
    {
        if (!Context.IsWorldReady) return "world unavailable";
        if (MultiplayerRescue.RescueService.SuspendsAfk(Game1.player)) return "multiplayer rescue";
        if (Game1.paused || Game1.isTimePaused || Game1.HostPaused) return "external/native time pause";
        if (Game1.freezeControls || Game1.pauseTime > 0) return "external/native control lock";
        if (Game1.overlayMenu != null) return "overlay menu";
        if (Game1.farmEvent != null || Game1.player.currentLocation?.currentEvent != null) return "location/farm event";
        if (Game1.player.controller != null) return "external movement controller";
        return null;
    }

    // Invalidate only AFK-owned work. Preserve the saved spot, catch queue and session.
    // No global pause, CanMove, native event or another mod's controller is cleared.
    internal static void YieldForRescue()
    {
        var service = Instance;
        if (service == null || !Context.IsWorldReady) return;
        var s = service.Current;
        // Finish ONLY an existing, owned native result. Never start another cast,
        // eat food or enter a chest transaction while rescue owns control.
        if (Game1.player.CurrentTool is StardewValley.Tools.FishingRod rod && service.Owns(rod)
            && (Game1.player.UsingTool || rod.isFishing || CommittedCatch(rod)))
        {
            if (rod.fishCaught) rod.doneHoldingFish(Game1.player);
            else if (Game1.activeClickableMenu == null && !CommittedCatch(rod))
            {
                rod.doneFishing(Game1.player, false);
                Game1.player.completelyStopAnimatingOrDoingAction();
            }
        }
        if (s.ExternalControlWait == "multiplayer rescue") return;
        service.StopWalking();
        s.Generation++; s.LockToken++; s.LockPending = false;
        s.Route = null; s.Search = null; s.HomeSearch = null;
        s.Nearby.Clear(); s.Standby.Clear(); s.Area = null; s.AreaAt = 0;
        service.ClearChest(); s.ExternalControlWait = "multiplayer rescue";
    }

    internal static bool OwnsRescueCatch(StardewValley.Tools.FishingRod rod) => Instance?.Owns(rod) == true;

    private void InvalidateWorldAssets()
    {
        // Invalidation can be raised outside a local screen's UpdateTicked. Mark every
        // screen now, then cancel its movement/search in that screen's next update.
        foreach (var pair in Sessions.GetActiveValues()) pair.Value.WorldAssetsDirty = true;
    }

    private void RefreshWorldAssets()
    {
        var s = Current;
        if (!s.WorldAssetsDirty) return;
        s.WorldAssetsDirty = false;
        s.Generation++; s.LockToken++; s.LockPending = false;
        ClearChest(); s.Area = null; s.Nearby.Clear(); s.Standby.Clear(); s.AreaAt = 0;
        s.HomeSearch = null; s.HomeCheckAt = 0; s.HomeEstimateReady = false;
        s.RouteFailures = 0; s.RecoveryCount = 0; s.Navigation.Clear();
        InvalidatePath(0.15);
    }
}

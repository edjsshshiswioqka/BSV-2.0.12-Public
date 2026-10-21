using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace BetterStardewValley.AfkFishing;

internal sealed partial class AfkFishingService
{
    internal const float MaximumCastPower = 1f;
    private static readonly MethodInfo NativeStartCast = Target(typeof(FishingRod), "startCasting", typeof(void));
    private static readonly MethodInfo NativeAddedDistance = Target(typeof(FishingRod), "getAddedDistance", typeof(int), typeof(Farmer));
    private const string PickupFlagKey = "Eddie.BetterStardewValley/AfkPickupFlag";
    internal static MethodInfo Target(Type type, string method, Type result, params Type[] args)
    {
        var matches = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == method && m.ReturnType == result && !m.IsGenericMethod
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(args)).ToArray();
        if (matches.Length != 1) throw new MissingMethodException($"BSV AFK: {type.FullName}.{method}: expected one exact target; found {matches.Length}. AFK disabled.");
        return matches[0];
    }
    private void InstallPatches(Harmony harmony)
    {
        HarmonyMethod Hook(string name) => new(typeof(AfkFishingService), name);
        harmony.Patch(Target(typeof(GameLocation), "performTouchAction", typeof(void), typeof(string[]), typeof(Vector2)),
            prefix: Hook(nameof(BeforeBedTouch)));
        harmony.Patch(Target(typeof(FishingRod), "startMinigameEndFunction", typeof(void), typeof(Item)), postfix: Hook(nameof(AfterMinigameCreation)));
        harmony.Patch(Target(typeof(FishingRod), "doneHoldingFish", typeof(void), typeof(Farmer), typeof(bool)),
            prefix: Hook(nameof(BeginOutput)), finalizer: Hook(nameof(EndOutput)));
        harmony.Patch(Target(typeof(FishingRod), "openTreasureMenuEndFunction", typeof(void), typeof(int)),
            prefix: Hook(nameof(BeginOutput)), finalizer: Hook(nameof(EndOutput)));
        harmony.Patch(Target(typeof(FishingRod), "justGotDerbyTagEndFunction", typeof(void), typeof(int)),
            prefix: Hook(nameof(BeginOutput)), finalizer: Hook(nameof(EndOutput)));
        harmony.Patch(Target(typeof(Farmer), "addItemToInventoryBool", typeof(bool), typeof(Item), typeof(bool)), prefix: Hook(nameof(RouteNewOutput)));
        harmony.Patch(Target(typeof(Farmer), "OnItemReceived", typeof(void), typeof(Item), typeof(int), typeof(Item), typeof(bool)),
            prefix: Hook(nameof(RestorePickupFlag)), postfix: Hook(nameof(ClearPickupFlag)));
        harmony.Patch(Target(typeof(Farmer), "MovePosition", typeof(void), typeof(GameTime), typeof(xTile.Dimensions.Rectangle), typeof(GameLocation)),
            prefix: Hook(nameof(BeforeNativeMove)), finalizer: Hook(nameof(AfterNativeMove)));
        harmony.Patch(Target(typeof(Farmer), "MovePositionImpl", typeof(bool), typeof(int), typeof(float), typeof(float),
            typeof(GameTime), typeof(xTile.Dimensions.Rectangle)), prefix: Hook(nameof(SteerNativeAxis)));
        harmony.Patch(Target(typeof(Farmer), "Halt", typeof(void)), prefix: Hook(nameof(PreserveWalkingAnimation)));
        harmony.Patch(Target(typeof(Farmer), "getMovementSpeed", typeof(float)), postfix: Hook(nameof(LimitFinalWalkStep)));
    }
    private bool Owns(FishingRod rod) => Context.IsWorldReady && Current.Data.Enabled
        && ReferenceEquals(Game1.player.CurrentTool, rod) && ReferenceEquals(Current.Rod, rod)
        && rod.getLastFarmerToUse() == Game1.player && Game1.player.IsLocalPlayer;
    private bool IsOwnedFishingMenu() => Current.Rod != null && Owns(Current.Rod)
        && (Game1.activeClickableMenu is BobberBar || Game1.activeClickableMenu is ItemGrabMenu grab && ReferenceEquals(grab.context, Current.Rod));
    private static void AfterMinigameCreation(FishingRod __instance)
    {
        var service = Instance;
        if (service == null || !service.Owns(__instance) || Game1.activeClickableMenu is not BobberBar bar) return;
        service.Guard(() =>
        {
            // Let native fish selection, quality/tackle calculation and treasure roll finish.
            // Resolve its native success branch before any minigame frame is presented.
            // Wild/Challenge Bait counts, XP, legendary records and animations stay native.
            service.Current.Data.Phase = AfkPhase.ResolvingCatch;
            CompleteMinigame(bar, Game1.currentGameTime);
        });
    }
    internal static void CompleteMinigame(BobberBar bar, GameTime time)
    {
        if (bar.handledFishResult) return;
        bar.handledFishResult = true;
        bar.perfect = true; // native landing applies Perfect quality and XP exactly once.
        bar.treasureCaught = bar.treasure;
        bar.distanceFromCatching = 1f;
        bar.fadeIn = false; bar.fadeOut = true; bar.scale = 0f;
        bar.everythingShakeTimer = 0;
        bar.update(time);
    }
    internal static void ReleaseMaximumCast(FishingRod rod)
    {
        if (!rod.isTimingCast) return;
        rod.castingPower = MaximumCastPower;
        // Exact native release event: native countdown, animation, landing, stamina and
        // network flow remain intact. No charge-meter compensation or bobber writes.
        NativeStartCast.Invoke(rod, null);
    }
    private static void BeginOutput(FishingRod __instance, out bool __state)
    {
        __state = Instance?.Owns(__instance) == true;
        if (__state) Instance!.Current.CaptureDepth++;
    }
    private static Exception? EndOutput(FishingRod __instance, bool __state, Exception? __exception)
    {
        if (!__state || Instance == null) return __exception;
        try
        {
            if (__exception == null && Game1.activeClickableMenu is ItemGrabMenu grab && ReferenceEquals(grab.context, __instance))
            {
                var source = grab.ItemsToGrabMenu.actualInventory;
                // This menu contains only this catch's native treasure/derby rewards.
                foreach (var item in source.ToArray()) if (item != null) Instance.Enqueue(item);
                source.Clear(); // no reward remains for essential-menu cleanup to drop.
                Game1.exitActiveMenu();
            }
        }
        finally { Instance.Current.CaptureDepth = Math.Max(0, Instance.Current.CaptureDepth - 1); }
        return __exception;
    }
    private static bool RouteNewOutput(Farmer __instance, Item item, ref bool __result)
    {
        var service = Instance;
        if (service == null || service.Current.CaptureDepth == 0 || __instance != Game1.player || item == null) return true;
        var before = AfkCatchLedger.Snapshot(__instance.Items);
        var remainder = __instance.addItemToInventory(item); // real native backpack-first API.
        service.Current.Catches.Received(__instance.Items, before);
        if (remainder != null) service.Enqueue(remainder);
        __result = true; // accepted into backpack OR durable pending inventory; native must not re-create it.
        return false;
    }
    private void Enqueue(Item item)
    {
        if (!string.IsNullOrEmpty(item.SetFlagOnPickup)) item.modData[PickupFlagKey] = item.SetFlagOnPickup;
        Pending(true)!.Add(item);
    }
    private static void RestorePickupFlag(Item item)
    {
        if (item != null && item.modData.TryGetValue(PickupFlagKey, out string? flag)) item.SetFlagOnPickup = flag;
    }
    private static void ClearPickupFlag(Item item, int countAdded)
    {
        if (countAdded > 0 && item != null) item.modData.Remove(PickupFlagKey);
    }
    private static void LimitFinalWalkStep(Farmer __instance, ref float __result)
    {
        if (Instance != null && __instance == Game1.player && Instance.Current.MoveLimit > 0)
        {
            // Native 1.6.15 applies 0.707 when two digital axes are held. Our unit
            // vector already apportions that same total budget between both axes.
            // Restore the native cardinal budget before apportioning, never a speed buff.
            if (__instance.movementDirections.Count > 1) __result /= 0.707f;
            __result = Math.Min(__result, Instance.Current.MoveLimit);
        }
    }
    private void Cast()
    {
        var who = Game1.player; var s = Current; var spot = s.Data.Spot;
        if (!s.Data.Enabled || spot == null || s.HomeIntent || Game1.timeOfDay >= AfkRules.HomeTime
            || who.UsingTool || who.isEating || !SafeWorld || Game1.activeClickableMenu != null) return;
        if (!RestoreSavedRod()) { Pause(AfkPhase.PausedMissingRod, "afk.rod-missing"); return; }
        var rod = (FishingRod)who.CurrentTool;
        if (UnsafeNextCast()) { RequestRest(AfkRestReason.LowEnergy); StartHome(); return; }
        if (!CastAreaValid(who.currentLocation, who.StandingPixel, spot, who))
        { Pause(AfkPhase.WaitingForCast, "afk.invalid-cast"); return; }
        StopWalking();
        s.Rod = rod; who.faceDirection(spot.Facing); s.Data.Phase = AfkPhase.Fishing; s.AutoCasting = true;
        RememberBait();
        TryTopUpBaitFrom(who.Items);
        rod.beginUsing(who.currentLocation, who.StandingPixel.X, who.StandingPixel.Y, who);
        spot.CastPower = MaximumCastPower;
        ReleaseMaximumCast(rod);
        if (rod.isTimingCast || rod.isCasting || rod.castedButBobberStillInAir || rod.isFishing)
        {
            s.Data.WakePending = false; s.Data.ReturnToSpot = false;
            s.WakeOwnsSleep = false; s.RecoveryStartedAt = -1; Persist();
        }
    }
    internal static bool TryMaximumLanding(GameLocation map, Point standing, int facing, FishingRod rod, Farmer who,
        out Point water, out string? area)
    {
        water = default; area = null;
        if (facing is < 0 or > 3 || !map.canFishHere()) return false;
        int extra = (int)NativeAddedDistance.Invoke(rod, new object[] { who })!;
        // Projection of the current 1.6.15 native release at power=1, not a bobber mutation.
        bool horizontal = facing is 1 or 3;
        float distance = Math.Max(128, MaximumCastPower * (extra + (horizontal ? 4 : 3)) * 64) - (horizontal ? 8 : 0);
        float px = standing.X + (facing == 1 ? distance : facing == 3 ? -distance : 0);
        float py = standing.Y + (facing == 2 ? distance : facing == 0 ? -distance : 0);
        if (px < 0 || py < 0) return false;
        water = new((int)(px / 64), (int)(py / 64));
        if (!map.isTileFishable(water.X, water.Y)) return false;
        map.TryGetFishAreaForTile(new Vector2(water.X, water.Y), out area, out _);
        return true;
    }
    private static bool CastAreaValid(GameLocation map, Point standing, FishingSpot spot, Farmer who)
    {
        if (map.NameOrUniqueName != spot.Location || who.CurrentTool is not FishingRod rod
            || !TryMaximumLanding(map, standing, spot.Facing, rod, who, out var water, out var area) || area != spot.FishArea) return false;
        // Old saves may contain a weaker cast. Confirm connected water without reusing that
        // strength. This rectangle also covers level/buff changes and the 1–4 tile fallback.
        var from = new TileNode(spot.Location, water.X, water.Y); var target = new TileNode(spot.Location, spot.WaterX, spot.WaterY);
        int left = Math.Min(from.X, target.X) - 4, right = Math.Max(from.X, target.X) + 4;
        int top = Math.Min(from.Y, target.Y) - 4, bottom = Math.Max(from.Y, target.Y) + 4;
        if ((long)(right - left + 1) * (bottom - top + 1) > 4096) return false;
        return AfkPath.Find(from, target, n => AfkPath.Neighbors(n)
            .Where(p => p.X >= left && p.X <= right && p.Y >= top && p.Y <= bottom && map.isTileFishable(p.X, p.Y))
            .Select(p => new RouteStep(n, p)), 4096) != null;
    }
}

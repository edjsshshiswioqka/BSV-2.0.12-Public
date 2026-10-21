using StardewModdingAPI;
using StardewValley;
using StardewValley.Enchantments;
using StardewValley.Tools;

namespace BetterStardewValley.AfkFishing;

internal sealed partial class AfkFishingService
{
    // Predict the cost observed in the referenced 1.6.15 FishingRod.DoFunction.
    // The native rod still performs the sole stamina deduction, including Efficient.
    internal static float NextCastCost(Farmer who, FishingRod rod) => rod.hasEnchantmentOfType<EfficientToolEnchantment>()
        ? 0 : Math.Max(0, 8f - who.FishingLevel * 0.1f);
    private bool UnsafeNextCast() => AfkRules.LowEnergy(Game1.player.Stamina, Game1.player.MaxStamina, Config.AfkFishing.ReturnStaminaThreshold)
        || (Game1.player.CurrentTool is FishingRod rod
            && AfkRules.LowEnergy(Game1.player.Stamina - NextCastCost(Game1.player, rod), Game1.player.MaxStamina, Config.AfkFishing.ReturnStaminaThreshold));
    private void TraceState(AfkPhase previous, string reason)
    {
        if (previous == Current.Data.Phase) return;
        Monitor.Log($"BSV AFK player={Game1.player.UniqueMultiplayerID} day={CurrentDay} {previous} -> {Current.Data.Phase}; {reason}; {RuntimeState()}", LogLevel.Debug);
    }
    private void RequestRest(AfkRestReason reason)
    {
        var s = Current;
        if (!s.Data.Enabled || s.HomeIntent) return;
        var previous = s.Data.Phase;
        s.HomeIntent = true; s.Data.RestReason = reason; s.Data.RestDay = CurrentDay;
        s.Data.SessionDay = CurrentDay; s.Data.ReturnToSpot = Config.AfkFishing.ReturnToFishingSpotNextDay;
        // A pending container callback must not refill/feed/export after rest is latched.
        s.Generation++; s.LockPending = false; ClearChest();
        if (Traveling(previous)) { s.Data.Phase = AfkPhase.WaitingForSafeEnd; StartHome(); return; }
        s.Data.Phase = AfkPhase.WaitingForSafeEnd;
        Persist(); TraceState(previous, reason.ToString());
        Notice(reason == AfkRestReason.LowEnergy ? "afk.low-energy" : "afk.home");
    }
    private static bool SavedHomeIntent(AfkSave data) => data.RestReason != AfkRestReason.None
        || data.Phase is AfkPhase.ReturningHome or AfkPhase.GoingToBed or AfkPhase.Sleeping or AfkPhase.WaitingForSafeEnd;
    private bool RestoreSavedRod()
    {
        var who = Game1.player; var spot = Current.Data.Spot;
        if (spot == null || who.UsingTool || who.isEating) return false;
        bool Match(int slot) => slot >= 0 && slot < who.Items.Count
            && who.Items[slot] is FishingRod rod && rod.QualifiedItemId == spot.RodId;
        int slot = Match(who.CurrentToolIndex) ? who.CurrentToolIndex : Match(spot.RodSlot) ? spot.RodSlot
            : Enumerable.Range(0, who.Items.Count).FirstOrDefault(Match, -1);
        if (slot < 0) return false;
        who.CurrentToolIndex = slot; spot.RodSlot = slot; Current.Rod = (FishingRod)who.CurrentTool;
        return true;
    }
}

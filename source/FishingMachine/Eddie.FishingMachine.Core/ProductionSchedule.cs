namespace Eddie.FishingMachine.Core;

/// <summary>Read-only preflight; inventory and state must still be checked under the chest mutex.</summary>
public static class ProductionSchedule
{
    public static bool NeedsWork(ProductionState state, long now, long baitCount, int requiredBait, bool hasOtherItems)
        => state.Phase switch
        {
            "working" => now >= state.DueAt,
            "ready" => true, // normalize the completed task independently of accumulated inventory
            "idle" => baitCount >= requiredBait,
            _ => false
        };
}

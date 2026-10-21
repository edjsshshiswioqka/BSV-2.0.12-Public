using BetterStardewValley;
using BetterStardewValley.Transactions;
using BetterStardewValley.Totems;
using StardewValley.Objects;

namespace Eddie.FishingMachine;

// A — adds no production phase or save field. A protected time-only transaction on the current batch.
internal sealed partial class ProductionService
{
    internal AtomicStep? TotemTiming(Chest chest)
    {
        if (!IntegrationHost.CanMutate || Mod.Saving || !chest.GetMutex().IsLockHeld() || !CompleteFootprint(chest))
            throw new InvalidOperationException("Totem machine lease is unavailable.");
        var old = Read(chest);
        if (old.Phase != "working" || old.StartedAt > Now || old.DueAt <= Now) return null;
        var next = old.Snapshot();
        next.DueAt = TotemRules.DueAt(Now, old.DueAt, old.LegendaryPlans.Count > 0 ? old.LegendaryYear : 0);
        if (next.DueAt == old.DueAt) return null;
        Eddie.FishingMachine.Core.MachineTimeline.Shorten(next,Now,old.DueAt);
        string before = chest.modData[StateKey];
        return new AtomicStep(() =>
        {
            if (!IntegrationHost.CanMutate || Mod.Saving || !chest.GetMutex().IsLockHeld() || chest.modData[StateKey] != before)
                throw new InvalidOperationException("Machine changed before the totem transaction.");
            Commit(chest, null, next); // no inventory, bait, pending, counts, seed or reservation changes
        }, () => { chest.modData[StateKey] = before; Cache.Remove(chest); });
    }
}

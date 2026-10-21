using StardewValley.GameData.Machines;

namespace BetterStardewValley.Totems;

// Processing Totem eligibility, independent of chest networks and external automation.
internal static class ProcessingPolicy
{
    internal static bool IsSafe(MachineData? data, string? machineId = null) => BlockReason(data, machineId) == null;

    // Preserve the existing Processing Totem scope, including the BSV Keg.
    private static readonly IReadOnlySet<string> ProcessingTotemIds = new HashSet<string>(StringComparer.Ordinal)
    {
        WineRules.BsvKeg, "(BC)12", "(BC)13", "(BC)15", "(BC)16", "(BC)17", "(BC)19", "(BC)20", "(BC)24",
        "(BC)114", "(BC)Dehydrator", "(BC)FishSmoker", "(BC)HeavyFurnace"
    };
    internal static bool SupportsProcessingTotem(string machineId, MachineData? data)
        => ProcessingTotemIds.Contains(machineId) && IsSafe(data)
            // BSV Keg explicitly supports the totem even when its cloned native data tracks
            // collection stats. Changing only remaining time does not collect or update stats.
            && (machineId == WineRules.BsvKeg || !(data!.StatsToIncrementWhenHarvested?.Count > 0));

    internal static string? BlockReason(MachineData? data, string? machineId = null)
    {
        if (data == null) return "missing-machine-data";
        bool repeating = false; // Processing Totem never includes Crystalarium.
        if (data.AllowLoadWhenFull && !repeating) return "load-when-full";
        if (data.IsIncubator) return "incubator";
        if (!string.IsNullOrWhiteSpace(data.InteractMethod)) return "interact-method:" + data.InteractMethod;
        if (!string.IsNullOrEmpty(data.ExperienceGainOnHarvest)) return "harvest-experience:" + data.ExperienceGainOnHarvest;
        foreach (var rule in data.OutputRules ?? new())
        {
            if (rule.RecalculateOnCollect) return "recalculate-on-collect";
            var allowed = MachineOutputTrigger.ItemPlacedInMachine;
            if (repeating) allowed |= MachineOutputTrigger.OutputCollected;
            if (rule.Triggers?.Any(t => t.Trigger == 0 || (t.Trigger & ~allowed) != 0) == true) return "non-input-trigger";
            foreach (var output in rule.OutputItem ?? new())
                if (!SafeOutputMethod(output.OutputMethod)) return "output-method:" + output.OutputMethod;
        }
        return null;
    }

    private static bool SafeOutputMethod(string? method)
        => string.IsNullOrWhiteSpace(method)
            || method == "BetterStardewValley.GrapeWineFeature, BetterStardewValley: OutputGrapeWine"
            || (method.StartsWith("StardewValley.Object, Stardew Valley: ", StringComparison.Ordinal)
                && !method.Contains(';'));
}

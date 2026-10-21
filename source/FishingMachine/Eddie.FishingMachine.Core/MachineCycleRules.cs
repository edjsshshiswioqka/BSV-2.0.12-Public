namespace Eddie.FishingMachine.Core;

/// <summary>Calendar duration and existing upgrade stages, shared by production and UI.</summary>
public static class MachineCycleRules
{
    public const int Version = 1;
    public const int DefaultMinutes = 2880;
    public static int RequiredCycles(string kind, int level) => (kind, level) switch
    {
        (MachineKinds.Fishing, 1) => 15,
        (MachineKinds.Crab, 1) or (MachineKinds.Fishing, 2) => 20,
        (MachineKinds.Fishing, 3) or (MachineKinds.Crab, 2) => 0,
        _ => throw new ArgumentException("Unsupported machine upgrade stage.")
    };
    public static int RequiredCycles(ProductionState state) => RequiredCycles(state.Kind, state.Level);
}

/// <summary>Pure preparation only. The host commits the returned copy under the existing mutex.</summary>
public static class MachineStateMigration
{
    public static bool Needed(ProductionState state) => state.CycleRulesVersion < MachineCycleRules.Version;
    public static ProductionState Prepare(ProductionState state)
    {
        // Do not repair invalid records by discarding paid output or quota identity.
        ProductionState.Load(state.Save());
        var next = state.Snapshot();
        if (!Needed(next)) return next;
        if (next.Phase == "working")
        {
            long duration = checked(next.DueAt - next.StartedAt);
            if (duration is < 1 or > 40320)
                throw new ArgumentException("Unrecognized legacy duration; original cycle preserved.");
            next.LegacyPrecomputed = true;
            next.OriginalCycleMinutes = (int)duration;
            // R11 preserves the already-paid deadline; historical defaults are not a new purchase.
        }
        next.CycleRulesVersion = MachineCycleRules.Version;
        next.Schema = 7;
        ProductionState.Load(next.Save());
        return next;
    }
    public static bool MetadataValid(ProductionState state) => state.CycleRulesVersion is 0 or MachineCycleRules.Version
        && (state.Schema == 7 || state.CycleRulesVersion == 0 && !state.LegacyPrecomputed && state.OriginalCycleMinutes == null)
        && (state.LegacyPrecomputed
            ? state.Schema == 7 && state.CycleRulesVersion == MachineCycleRules.Version && state.Phase != "idle"
                && state.OriginalCycleMinutes is >= 1 and <= 40320
                // Processing Totem may legally shorten this paid cycle after migration.
                && state.DueAt > state.StartedAt && state.DueAt - state.StartedAt <= Math.Max(MachineCycleRules.DefaultMinutes,state.OriginalCycleMinutes.Value)
            : state.OriginalCycleMinutes == null);
}

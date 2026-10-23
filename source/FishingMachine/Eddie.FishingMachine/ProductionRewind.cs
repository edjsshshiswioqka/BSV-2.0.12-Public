using System.Text.Json;
using Eddie.FishingMachine.Core;
using StardewModdingAPI;
using StardewValley.Objects;
namespace Eddie.FishingMachine;

internal sealed partial class ProductionService
{
    /// <summary>The date was moved backwards (for example with a cheat menu): move this machine's saved times back
    /// by whole days so it keeps working from today instead of waiting for the old date.</summary>
    private void HandleRewind(Chest c)
    {
        ProductionState s;
        try { s = Read(c); } catch { return; }
        long delta = MachineRewind.Delta(s, Now);
        if (delta == 0) return;
        var shifted = s.Snapshot();
        MachineRewind.Shift(shifted, delta);
        if (shifted.Schema == 8 && shifted.Phase != "idle" && !MachineTimeline.Valid(shifted))
        {
            if (Errors.Add(c.modData.GetValueOrDefault(ModEntry.Tag + "MachineId") + ":rewind"))
                Mod.Monitor.Log($"[BSV machine] the date moved back {-delta / 1440} days at {c.Location?.NameOrUniqueName}:{c.TileLocation}, but the running cycle cannot be moved that far; it resumes when the date catches up.", LogLevel.Warn);
            return;
        }
        // Environment observations of the old dates no longer match the calendar; record them again from today.
        TimelineEnvironment.Forget(s.CycleToken, s.Continuity?.EnvironmentToken);
        if (c.modData.TryGetValue(InputKey, out string raw) && JsonSerializer.Deserialize<InputJournal>(raw) is { } journal && journal.Events.Any(e => e.At > Now))
        {
            journal.Events = journal.Events.Select(e => e.At > Now ? e with { At = Math.Max(360, e.At + delta) } : e).ToList();
            c.modData[InputKey] = JsonSerializer.Serialize(journal);
        }
        Commit(c, null, shifted);
        Mod.Monitor.Log($"[BSV machine] the date moved back {-delta / 1440} days at {c.Location?.NameOrUniqueName}:{c.TileLocation}; machine times moved back with it ({shifted.Phase}, Lv{shifted.Level}).", LogLevel.Info);
    }
}

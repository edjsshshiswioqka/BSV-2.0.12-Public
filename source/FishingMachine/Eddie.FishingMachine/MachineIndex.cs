using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;

namespace Eddie.FishingMachine;

/// <summary>One discovery pass per load/day; SMAPI object and location events maintain the index.</summary>
internal sealed class MachineIndex
{
    private readonly Dictionary<Chest, GameLocation> Entries = new();
    private KeyValuePair<Chest, GameLocation>[]? Snapshot;
    internal IReadOnlyList<KeyValuePair<Chest, GameLocation>> All => Snapshot ??= Entries.ToArray();
    internal bool Contains(Chest chest, GameLocation location)=>Entries.TryGetValue(chest,out var found) && ReferenceEquals(found,location);
    internal void Reset() { Entries.Clear(); Snapshot = null; }
    internal void Add(GameLocation location, StardewValley.Object item)
    {
        if (!Context.IsMainPlayer || item is not Chest chest || !ModEntry.IsAnchor(chest)) return;
        if (Entries.TryGetValue(chest, out var old) && ReferenceEquals(old, location)) return;
        Entries[chest] = location; Snapshot = null;
    }
    internal void Remove(StardewValley.Object item)
    {
        if (item is Chest chest && Entries.Remove(chest)) Snapshot = null;
    }
    internal void Rebuild()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer) return;
        Reset();
        Utility.ForEachLocation(location =>
        {
            foreach (var item in location.objects.Values) Add(location, item);
            return true;
        }, includeInteriors: true, includeGenerated: true);
    }
    internal void Changed(object? sender, ObjectListChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer) return;
        foreach (var pair in e.Removed)
            if (pair.Value is Chest chest && Contains(chest,e.Location)) Remove(chest);
        foreach (var pair in e.Added) Add(e.Location, pair.Value);
    }
}

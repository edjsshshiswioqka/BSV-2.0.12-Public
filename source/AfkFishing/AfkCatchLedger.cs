using StardewValley;

namespace BetterStardewValley.AfkFishing;

// References/counts only, never a second inventory. Conservatively protect pre-existing
// stack contents. Uncertain items after reload/rearrangement are not transport sources.
internal sealed class AfkCatchLedger
{
    private sealed class Entry { internal int Protected, Credit; }
    private readonly Dictionary<Item, Entry> Entries = new(ReferenceEqualityComparer.Instance);
    internal static Dictionary<Item, int> Snapshot(IEnumerable<Item> items)
    {
        var result = new Dictionary<Item, int>(ReferenceEqualityComparer.Instance);
        foreach (var item in items) if (item != null) result[item] = item.Stack;
        return result;
    }
    internal void Received(IList<Item> inventory, IReadOnlyDictionary<Item, int> before)
    {
        foreach (var item in inventory.Where(i => i != null))
        {
            int old = before.TryGetValue(item, out int n) ? n : 0, added = item.Stack - old;
            if (added <= 0) continue;
            if (!Entries.TryGetValue(item, out var entry)) Entries[item] = entry = new() { Protected = old };
            entry.Credit = Math.Min(entry.Credit, Math.Max(0, old - entry.Protected)) + added;
            entry.Protected = item.Stack - entry.Credit;
        }
        Prune(inventory);
    }
    private void Prune(IList<Item> inventory)
    {
        foreach (var item in Entries.Keys.ToArray())
            if (!inventory.Any(i => ReferenceEquals(i, item))) Entries.Remove(item);
    }
    internal int Allowed(Item item) => item != null && Entries.TryGetValue(item, out var e)
        ? Math.Max(0, Math.Min(e.Credit, item.Stack - e.Protected)) : 0;
    internal bool HasOutput(IList<Item> inventory) { Prune(inventory); return inventory.Any(i => Allowed(i) > 0); }
    internal int Store(IList<Item> source, IList<Item> destination, int capacity)
    {
        var before = Snapshot(source);
        int moved = AfkTransfer.MoveSelected(source, destination, capacity, i => i.Stack, (i, n) => i.Stack = n,
            (a, b) => a.canStackWith(b), i => i.maximumStackSize(), i => i.getOne(), Allowed, keepSlots: true);
        foreach (var pair in before)
            if (Entries.TryGetValue(pair.Key, out var e))
                e.Credit = Math.Max(0, e.Credit - (pair.Value - pair.Key.Stack));
        Prune(source); return moved;
    }
    internal void Clear() => Entries.Clear();
}

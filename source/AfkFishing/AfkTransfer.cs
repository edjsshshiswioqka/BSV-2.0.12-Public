namespace BetterStardewValley.AfkFishing;

// Transfer only newly caught output; existing backpack items are never sources.
// Two inventory snapshots allow fault-injection and rollback without rebuilding items.
internal static class AfkTransfer
{
    internal static bool HasRoom<T>(IList<T> destination, int capacity, T item, Func<T, int> count,
        Func<T, T, bool> stacks, Func<T, int> maximum) where T : class =>
        destination.Count < capacity || destination.Take(capacity).Any(i => i == null)
        || destination.Any(i => i != null && stacks(i, item) && count(i) < maximum(i));

    internal static int Move<T>(IList<T> source, IList<T> destination, int capacity,
        Func<T, int> count, Action<T, int> setCount, Func<T, T, bool> stacks,
        Func<T, int> maximum, Func<T, T> clone, Action? afterMutation = null) where T : class
        => MoveSelected(source, destination, capacity, count, setCount, stacks, maximum, clone, count, false, afterMutation);

    internal static int MoveSelected<T>(IList<T> source, IList<T> destination, int capacity,
        Func<T, int> count, Action<T, int> setCount, Func<T, T, bool> stacks,
        Func<T, int> maximum, Func<T, T> clone, Func<T, int> allowed, bool keepSlots,
        Action? afterMutation = null) where T : class
    {
        if (ReferenceEquals(source, destination)) throw new ArgumentException("Same inventory");
        var beforeSource = source.ToArray(); var beforeDest = destination.ToArray();
        var counts = new Dictionary<T, int>(ReferenceEqualityComparer.Instance);
        foreach (var item in beforeSource.Concat(beforeDest)) if (item != null) counts[item] = count(item);
        int moved = 0;
        try
        {
            for (int s = source.Count - 1; s >= 0; s--)
            {
                T item = source[s]; if (item == null || count(item) <= 0) continue;
                int remaining = Math.Min(count(item), Math.Max(0, allowed(item)));
                for (int d = 0; d < destination.Count && remaining > 0; d++)
                {
                    T existing = destination[d];
                    if (existing == null || !stacks(existing, item)) continue;
                    int n = Math.Min(remaining, Math.Max(0, maximum(existing) - count(existing)));
                    if (n == 0) continue;
                    setCount(existing, count(existing) + n); afterMutation?.Invoke();
                    remaining -= n; setCount(item, count(item) - n); moved += n; afterMutation?.Invoke();
                }
                while (remaining > 0)
                {
                    int empty = -1;
                    for (int d = 0; d < Math.Min(destination.Count, capacity); d++)
                        if (destination[d] == null) { empty = d; break; }
                    if (empty < 0 && destination.Count >= capacity) break;
                    int n = Math.Min(remaining, Math.Max(1, maximum(item)));
                    T copy = clone(item); setCount(copy, n);
                    if (empty >= 0) destination[empty] = copy; else destination.Add(copy);
                    afterMutation?.Invoke(); remaining -= n; setCount(item, count(item) - n); moved += n; afterMutation?.Invoke();
                }
                if (count(item) == 0) { if (keepSlots) source[s] = null!; else source.RemoveAt(s); afterMutation?.Invoke(); }
            }
            return moved;
        }
        catch
        {
            source.Clear(); foreach (var i in beforeSource) source.Add(i);
            destination.Clear(); foreach (var i in beforeDest) destination.Add(i);
            foreach (var pair in counts) setCount(pair.Key, pair.Value);
            throw;
        }
    }
}

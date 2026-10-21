namespace Eddie.FishingMachine.Core;

public sealed record BufferSlot(string Key, int Count, int Maximum);
public sealed record DepositResult(IReadOnlyList<BufferSlot> Slots, IReadOnlyList<StoredCatch> Pending);

/// <summary>Pure, non-destructive packing; the game adapter commits slots and pending data together.</summary>
public static class DepositPlanner
{
    public static DepositResult Plan(IReadOnlyList<BufferSlot> inventory, IReadOnlyList<StoredCatch> pending,
        int capacity, Func<string, int> maximum)
    {
        var slots = inventory.ToList();
        var remaining = new List<StoredCatch>();
        foreach (var item in pending)
        {
            int left = item.Count, max = maximum(item.Key);
            if (left <= 0 || max <= 0) throw new ArgumentException("Invalid output stack.");
            for (int i = 0; i < slots.Count && left > 0; i++)
            {
                var s = slots[i];
                if (s.Key != item.Key) continue;
                int n = Math.Min(left, Math.Max(0, s.Maximum - s.Count));
                slots[i] = s with { Count = s.Count + n }; left -= n;
            }
            while (left > 0 && slots.Count < capacity)
            {
                int n = Math.Min(left, max);
                slots.Add(new(item.Key, n, max)); left -= n;
            }
            if (left > 0) remaining.Add(item with { Count = left });
        }
        return new(slots, remaining);
    }
}

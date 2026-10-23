using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.FishPonds;
using Newtonsoft.Json;

namespace BetterStardewValley.SpiritFish;

/// <summary>Data-only rewards. Native selection, roe bonuses, cracker and output ownership remain native.</summary>
internal static class SpiritPondRewards
{
    internal static readonly string[] Items = { "(O)279", "(O)74", "(O)852", "(O)848" };
    private const string Prefix = SpiritRules.Id + "/PondReward/";
    private static bool IsRare(string? id) => id != null && Items.Any(q => q == id || q[3..] == id);

    internal static void Apply(FishPondData pond)
    {
        if (pond.Id != SpiritRules.Id) throw new ArgumentException("Only the dedicated Spirit pond may be edited.");
        pond.ProducedItems ??= new();
        // Work only on SpiritData's deep clone, never the loaded legendary template.
        // Also remove a previous installation of our entries: asset reload is idempotent.
        pond.ProducedItems.RemoveAll(p => p.Id?.StartsWith(Prefix, StringComparison.Ordinal) == true
            || (p.RandomItemId?.Count > 0 ? false : IsRare(p.ItemId)));
        foreach (var row in pond.ProducedItems.ToArray())
            if (row.RandomItemId?.Count > 0)
            {
                row.RandomItemId.RemoveAll(IsRare);
                if (row.RandomItemId.Count == 0) pond.ProducedItems.Remove(row);
            }
        var rewards = Items.Select((id, i) => new FishPondReward {
            Id = Prefix + id[3..], ItemId = id, Chance = 1f / (10 - i),
            RequiredPopulation = 0, Precedence = int.MinValue, MinStack = 1, MaxStack = 1
        });
        // 1.6.15 keeps the FIRST successful equal-precedence entry. int.MinValue
        // prevents any later ordinary row from replacing a selected rare reward.
        pond.ProducedItems.InsertRange(0, rewards);
    }

    internal static void ValidateLoaded(IMonitor monitor, LogLevel successLevel = LogLevel.Debug)
    {
        try
        {
            var data = FishPond.GetRawData(SpiritRules.Id);
            if (data?.Id != SpiritRules.Id || data.ProducedItems is not { Count: >= 4 } produced)
                throw new InvalidOperationException("The loaded Spirit pond entry is missing or overridden.");
            for (int i = 0; i < Items.Length; i++)
            {
                var row = produced[i];
                if (row.Id != Prefix + Items[i][3..] || row.ItemId != Items[i]
                    || row.Chance != 1f / (10 - i) || row.Precedence != int.MinValue
                    || row.RequiredPopulation > 1 || !string.IsNullOrEmpty(row.Condition)
                    || !string.IsNullOrEmpty(row.PerItemCondition) || row.RandomItemId?.Count > 0
                    || row.MinStack != 1 || row.MaxStack != 1 || row.StackModifiers?.Count > 0)
                    throw new InvalidOperationException("Loaded Spirit reward order, eligibility or quantity was modified: " + Items[i]);
                var item = ItemRegistry.Create(Items[i], allowNull: true);
                if (item is not StardewValley.Object obj || obj.bigCraftable.Value || item.QualifiedItemId != Items[i])
                    throw new InvalidOperationException("Required vanilla object is not registered: " + Items[i]);
            }
            if (produced.Skip(4).Any(p => p.RandomItemId?.Count > 0 ? p.RandomItemId.Any(IsRare) : IsRare(p.ItemId)))
                throw new InvalidOperationException("Duplicate Spirit rare reward in the loaded ordinary pool.");
            monitor.Log("Spirit pond loaded rewards verified: four exclusive ~10% outcomes, ~60% ordinary; vanilla registry IDs valid. "
                + JsonConvert.SerializeObject(data), successLevel);
        }
        catch (Exception ex) { monitor.Log("Spirit pond reward validation failed: " + ex.Message, LogLevel.Warn); }
    }
}

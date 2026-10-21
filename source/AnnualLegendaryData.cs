using System.Collections.Generic;
using StardewValley.GameData.Locations;

namespace BetterStardewValley;

internal static class AnnualLegendaryData
{
    internal static int RemoveLifetimeLimits(IDictionary<string, LocationData> locations)
    {
        int count = 0;
        foreach (LocationData location in locations.Values)
        {
            if (location.Fish is null) continue;
            foreach (SpawnFishData spawn in location.Fish)
            {
                if (AnnualLegendaryRules.NormalizeId(spawn.ItemId) is null) continue;
                if (spawn.CatchLimit != -1) { spawn.CatchLimit = -1; count++; }
                // Keep season, weather/quest conditions, fishing level, water depth,
                // player/bobber area, probabilities and training-rod restrictions intact.
            }
        }
        return count;
    }
}

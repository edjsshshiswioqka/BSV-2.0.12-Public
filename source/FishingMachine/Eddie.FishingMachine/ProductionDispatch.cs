using Eddie.FishingMachine.Core;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;

namespace Eddie.FishingMachine;

internal sealed partial class ProductionService
{
    private void ProcessAutomatic(Chest c)
    {
        // Only internal inventory and paid production; connected chests are never scanned.
        Process(c);
        if (ModEntry.IsCrab(c) && !c.modData.ContainsKey(CrabCommandKey)
            && Read(c).Phase != "working" && Read(c).Pending.Count == 0)
        {
            var owner = CrabOperator(c);
            if (owner != null) StartCrab(c, owner);
        }
    }
}

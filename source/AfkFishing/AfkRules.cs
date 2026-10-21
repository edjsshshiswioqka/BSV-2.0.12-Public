namespace BetterStardewValley.AfkFishing;

internal enum AfkPhase
{
    Disabled, Fishing, WaitingForBite, ResolvingCatch, StoringOutput, Eating,
    ReturningHome, GoingToBed, Sleeping, ReturningToSpot, ResumingFishing,
    PausedStorageFull, PausedNoRoute, PausedNoChest, PausedEvent, ResumeCountdown,
    WaitingForRoute, PlanningRoute, WaitingForCast, WakingUp,
    WaitingForSafeEnd, PausedMissingRod, LeavingBed // append only: preserve old numeric save values
}
internal enum AfkRestReason { None, LowEnergy, Bedtime }

internal readonly record struct TileNode(string Map, int X, int Y);
internal sealed record RouteStep(TileNode From, TileNode To, int DoorX = int.MinValue, int DoorY = int.MinValue, bool Gate = false)
{
    public bool Transition => From.Map != To.Map;
    public bool Door => DoorX != int.MinValue;
}

// Original BSV bounded BFS. Every edge costs one legal tile step / doorway interaction.
// No heuristic based on map names, no third-party pathfinding implementation.
internal static class AfkPath
{
    internal static List<RouteStep>? Find(TileNode start, TileNode goal,
        Func<TileNode, IEnumerable<RouteStep>> edges, int budget = 120000)
    {
        var seen = new HashSet<TileNode> { start };
        var parents = new Dictionary<TileNode, RouteStep>();
        var queue = new Queue<TileNode>(); queue.Enqueue(start);
        while (queue.Count > 0 && budget-- > 0)
        {
            var node = queue.Dequeue();
            if (node == goal)
            {
                var result = new List<RouteStep>();
                while (node != start) { var step = parents[node]; result.Add(step); node = step.From; }
                result.Reverse(); return result;
            }
            foreach (var step in edges(node))
                if (step.From == node && seen.Add(step.To)) { parents[step.To] = step; queue.Enqueue(step.To); }
        }
        return null;
    }

    internal static IEnumerable<TileNode> Neighbors(TileNode n)
    {
        yield return n with { Y = n.Y - 1 }; yield return n with { X = n.X - 1 };
        yield return n with { X = n.X + 1 }; yield return n with { Y = n.Y + 1 };
    }

    // Exact player-centered 3x3. Ground validation includes all nine cells;
    // eligible chest occupancy is exempted by the caller, not by trimming the region.
    internal static TileNode? NearestChest(TileNode start, IEnumerable<TileNode> chests,
        Func<TileNode, bool> validGround)
    {
        for (int y = start.Y - 1; y <= start.Y + 1; y++)
            for (int x = start.X - 1; x <= start.X + 1; x++)
                if (!validGround(new(start.Map, x, y))) return null;
        return chests.Where(c => c.Map == start.Map && Math.Abs(c.X - start.X) <= 1 && Math.Abs(c.Y - start.Y) <= 1)
            .OrderBy(c => (c.X - start.X) * (c.X - start.X) + (c.Y - start.Y) * (c.Y - start.Y))
            .ThenBy(c => c.Y).ThenBy(c => c.X).Select(c => (TileNode?)c).FirstOrDefault();
    }

}

internal static class AfkRules
{
    internal const int HomeTime = 2430; // 00:30, NOT 1230 (midday).
    internal static bool Hungry(float energy, int maximum) => maximum > 0 && energy <= maximum * 0.3f;
    internal static bool LowEnergy(float energy, int maximum, double threshold=.05) => maximum > 0 && energy <= maximum * Math.Clamp(double.IsFinite(threshold)?threshold:.05,.01,.50);
    internal static bool GoHome(int time, float energy, int maximum, double threshold=.05) => time >= HomeTime || LowEnergy(energy, maximum, threshold);
    internal static bool AcceptFood(bool edible, int energy, bool quest, bool unsafeEffect) =>
        edible && energy > 0 && !quest && !unsafeEffect;
}

internal sealed class FishingSpot
{
    public string Location { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Facing { get; set; }
    public int WaterX { get; set; }
    public int WaterY { get; set; }
    public string? FishArea { get; set; }
    public float CastPower { get; set; }
    public float StandingOffsetX { get; set; }
    public float StandingOffsetY { get; set; }
    public string RodId { get; set; } = "";
    public int RodSlot { get; set; }
    public TileNode Tile => new(Location, X, Y);
}

internal sealed class AfkSave
{
    public int Schema { get; set; } = 1;
    public long PlayerId { get; set; } // 0: legacy, adopt local owner once.
    public int SleepRequestedDay { get; set; } = -1;
    public bool Enabled { get; set; }
    public AfkPhase Phase { get; set; }
    public FishingSpot? Spot { get; set; }
    public string ChestMap { get; set; } = "";
    public int ChestX { get; set; }
    public int ChestY { get; set; }
    public string ChestId { get; set; } = "";
    // Output lives in a native, save-serialized Inventory, not JSON item approximations.
    public string OutputInventory { get; set; } = "";
    // Stacking identity only, never an item quantity/inventory. Optional for older schema-1 saves.
    public string BaitPreference { get; set; } = "";
    // Additive fields; absent fields in v1.1.8 saves migrate to protected, never late-manual.
    public AfkNightPolicy NightPolicy { get; set; }
    public int SafetyDay { get; set; } = -1;
    public int ActivationDay { get; set; } = -1;
    public int ActivationTime { get; set; }
    public int EmergencyUsedDay { get; set; } = -1;
    public bool ReturnToSpot { get; set; }
    public bool WakePending { get; set; }
    // Additive scalar state only. No route, chest or location object is serialized.
    public AfkRestReason RestReason { get; set; }
    public int RestDay { get; set; } = -1;
    public int SessionDay { get; set; } = -1;
}

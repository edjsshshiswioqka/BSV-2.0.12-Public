using System.Diagnostics;

namespace BetterStardewValley.AfkFishing;

internal enum RouteSearchStatus { Searching, Found, NoPath, BudgetExceeded }

// Finite cost: briefly occupied routes may beat enormous detours. Occupied tiles are
// still NEVER executable until clear; the movement controller waits and replans.
internal readonly record struct RouteCost(long Occupied, long Steps) : IComparable<RouteCost>
{
    internal long Total => Steps + Occupied * 8;
    public int CompareTo(RouteCost other) => Total.CompareTo(other.Total);
    public static RouteCost operator +(RouteCost a, RouteCost b) => new(a.Occupied + b.Occupied, a.Steps + b.Steps);
}

// Incremental Dijkstra (A* with a zero admissible heuristic). Keeps its frontier between
// frames: a frame budget means SEARCHING; total work limits report BudgetExceeded separately.
// Location transitions and local tile steps share this search, so a blocked preferred exit
// can be replaced with another exit/location route, not only a detour to the same bad door.
internal sealed class AfkRouteSearch
{
    private readonly TileNode Start;
    private readonly IReadOnlyDictionary<TileNode, int> Goals;
    private readonly Func<TileNode, IEnumerable<RouteStep>> Edges;
    private readonly Func<RouteStep, RouteCost> Weight;
    private readonly Dictionary<TileNode, RouteCost> Costs = new();
    private readonly Dictionary<TileNode, RouteStep> Parents = new();
    private readonly PriorityQueue<(TileNode Tile, RouteCost Cost), (long Actors, long Cost, long Sequence)> Queue = new();
    private readonly int MinimumRank;
    internal const int MaximumNodes = 120000;
    internal const double MaximumMilliseconds = 1500;
    private double WorkMilliseconds;
    private long Sequence;
    private int BestRank = int.MaxValue;
    private TileNode? Best;
    internal RouteSearchStatus Status { get; private set; }
    internal List<RouteStep>? Route { get; private set; }
    internal TileNode? Goal { get; private set; }
    internal int Expanded { get; private set; }
    internal AfkRouteSearch(TileNode start, IReadOnlyDictionary<TileNode, int> goals,
        Func<TileNode, IEnumerable<RouteStep>> edges, Func<RouteStep, RouteCost>? weight = null)
    {
        Start = start; Goals = goals; Edges = edges; Weight = weight ?? (_ => new(0, 1));
        MinimumRank = goals.Count == 0 ? int.MaxValue : goals.Values.Min();
        Costs[start] = new(0, 0); Queue.Enqueue((start, new(0, 0)), (0, 0, Sequence++));
        if (goals.Count == 0) Status = RouteSearchStatus.NoPath;
    }
    internal RouteSearchStatus Advance(int nodes = 256, double milliseconds = 2.5)
    {
        if (Status != RouteSearchStatus.Searching) return Status;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < nodes && (i == 0 || watch.Elapsed.TotalMilliseconds < milliseconds); i++)
        {
            if (!Queue.TryDequeue(out var entry, out _))
            { if (Best.HasValue) Finish(Best.Value); else Status = RouteSearchStatus.NoPath; return Status; }
            if (Costs[entry.Tile] != entry.Cost) continue;
            if (++Expanded > MaximumNodes || WorkMilliseconds + watch.Elapsed.TotalMilliseconds > MaximumMilliseconds)
            { Status = RouteSearchStatus.BudgetExceeded; return Status; }
            if (Goals.TryGetValue(entry.Tile, out int rank) && rank < BestRank)
            {
                Best = entry.Tile; BestRank = rank;
                if (rank == MinimumRank) { Finish(entry.Tile); return Status; }
            }
            foreach (var step in Edges(entry.Tile))
            {
                if (step.From != entry.Tile) continue;
                var weight = Weight(step);
                if (weight.Occupied < 0 || weight.Steps < 1) throw new InvalidOperationException("AFK route costs must be positive.");
                var cost = entry.Cost + weight;
                if (Costs.TryGetValue(step.To, out var previous) && previous.CompareTo(cost) <= 0) continue;
                Costs[step.To] = cost; Parents[step.To] = step;
                Queue.Enqueue((step.To, cost), (cost.Total, cost.Steps, Sequence++));
            }
        }
        WorkMilliseconds += watch.Elapsed.TotalMilliseconds;
        return Status;
    }
    private void Finish(TileNode node)
    {
        Goal = node; Route = new();
        while (node != Start) { var edge = Parents[node]; Route.Add(edge); node = edge.From; }
        Route.Reverse(); Status = RouteSearchStatus.Found;
    }
}

// Bounded per-player recent history, not permanent save data. An expired penalty cannot
// permanently blacklist a formerly blocked route or gate.
internal sealed class AfkNavigationMemory
{
    internal const double DynamicWait = 0.65, StuckSeconds = 1.6, RetrySeconds = 3;
    private readonly Queue<TileNode> Recent = new();
    private readonly Dictionary<TileNode, double> Penalized = new();
    private TileNode? LastTile, Waypoint;
    private double ImprovedAt, BestDistance = double.MaxValue, BlockedAt = -1;
    internal int Penalty(TileNode n, double now)
    {
        if (!Penalized.TryGetValue(n, out double until)) return 0;
        if (until <= now) { Penalized.Remove(n); return 0; }
        return 8;
    }
    internal void Penalize(TileNode n, double now)
    {
        foreach (var expired in Penalized.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) Penalized.Remove(expired);
        if (Penalized.Count >= 64) Penalized.Remove(Penalized.OrderBy(p => p.Value).First().Key);
        Penalized[n] = now + 12;
    }
    internal bool Visit(TileNode tile, double now)
    {
        if (LastTile == tile) return false;
        LastTile = tile; Recent.Enqueue(tile); while (Recent.Count > 16) Recent.Dequeue();
        if (Recent.Count(t => t == tile) < 3) return false;
        foreach (var n in Recent.TakeLast(8)) Penalize(n, now);
        Recent.Clear(); Recent.Enqueue(tile); ResetWaypoint(); return true;
    }
    internal void Watch(TileNode waypoint, double distance, double now)
    {
        if (Waypoint != waypoint) { Waypoint = waypoint; BestDistance = distance; ImprovedAt = now; BlockedAt = -1; }
        else if (distance < BestDistance - 1) { BestDistance = distance; ImprovedAt = now; }
    }
    internal bool Stuck(double now) => Waypoint.HasValue && now - ImprovedAt >= StuckSeconds;
    internal bool WaitForActor(double now)
    {
        if (BlockedAt < 0) BlockedAt = now;
        return now - BlockedAt < DynamicWait;
    }
    internal void ClearActor() => BlockedAt = -1;
    internal void ResetWaypoint() { Waypoint = null; BestDistance = double.MaxValue; BlockedAt = -1; }
    internal void Clear() { Recent.Clear(); Penalized.Clear(); LastTile = null; ResetWaypoint(); }
}

internal static class AfkNavigationCandidates
{
    internal static IEnumerable<KeyValuePair<TileNode, int>> Around(TileNode center)
    {
        for (int radius = 0; radius <= 4; radius++)
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
                for (int x = center.X - radius; x <= center.X + radius; x++)
                    if (Math.Max(Math.Abs(x - center.X), Math.Abs(y - center.Y)) == radius)
                        yield return new(new(center.Map, x, y), radius * 16 + Math.Abs(x - center.X) + Math.Abs(y - center.Y));
    }
}

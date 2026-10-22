using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterStardewValley;

/// <summary>Game-independent rules used by the actual host adapter (and its checks).</summary>
internal static class HorseRules
{
    internal readonly record struct Tile(int X, int Y);
    internal readonly record struct Bounds(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;
        public bool Intersects(Bounds b) => X < b.Right && Right > b.X && Y < b.Bottom && Bottom > b.Y;
        public Bounds Inflate(int amount) => new(X - amount, Y - amount, Width + 2 * amount, Height + 2 * amount);
    }
    internal static long RecoverOwner(long horseOwner, long stableOwner) => horseOwner != 0 ? horseOwner : stableOwner;
    internal static bool Owns(long actor, long stableOwner, long horseOwner) => actor != 0 && actor == stableOwner && actor == horseOwner;
    internal static bool CanClaim(long stableOwner, long horseOwner) => stableOwner == 0 && horseOwner == 0;
    internal static bool ValidEnvelope(long sender, long actor, string? request, string? horse, string? location) =>
        sender != 0 && sender == actor && Guid.TryParse(request, out _) && Guid.TryParse(horse, out var id)
        && id != Guid.Empty && !string.IsNullOrWhiteSpace(location) && location.Length <= 256;
    internal static bool Allowed(bool outdoors, bool temporary, bool hasEvent, string name, IEnumerable<string> types, IEnumerable<string> blacklist) =>
        outdoors && !temporary && !hasEvent && !blacklist.Contains(name, StringComparer.OrdinalIgnoreCase)
        && !types.Append(name).Any(s => s.Contains("Dungeon", StringComparison.OrdinalIgnoreCase)
            || s.Contains("MineShaft", StringComparison.OrdinalIgnoreCase) || s.StartsWith("UndergroundMine", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("Volcano", StringComparison.OrdinalIgnoreCase) || s.Equals("SkullCave", StringComparison.OrdinalIgnoreCase)
            || s.Equals("SkullCavern", StringComparison.OrdinalIgnoreCase) || s.Equals("BugLand", StringComparison.OrdinalIgnoreCase));
    internal static int[] StopRadii(int preferred) => new[] { preferred, 3, 2, 4, 5 }.Where(n => n >= 1 && n <= 8).Distinct().ToArray();
    internal static IEnumerable<Tile> Stops(Tile player, int radius)
    {
        // Cardinal points first, then nearby points on the same approximate distance ring.
        yield return new(player.X - radius, player.Y);
        yield return new(player.X + radius, player.Y);
        yield return new(player.X, player.Y - radius);
        yield return new(player.X, player.Y + radius);
        for (int x = -radius; x <= radius; x++)
        for (int y = -radius; y <= radius; y++)
            if (x != 0 && y != 0 && Math.Abs(Math.Sqrt(x * x + y * y) - radius) < .45)
                yield return new(player.X + x, player.Y + y);
    }
    internal static bool OutsideBand(Bounds sprite, Bounds camera, int minTiles, int maxTiles)
    {
        if (sprite.Intersects(camera)) return false;
        int gapX = Math.Max(0, Math.Max(camera.X - sprite.Right, sprite.X - camera.Right));
        int gapY = Math.Max(0, Math.Max(camera.Y - sprite.Bottom, sprite.Y - camera.Bottom));
        int gap = Math.Max(gapX, gapY);
        return gap >= minTiles * 64 && gap <= maxTiles * 64;
    }
    internal static bool ValidViewport(Bounds b, int playerX, int playerY) => b.Width >= 128 && b.Height >= 128
        && b.Width <= 16384 && b.Height <= 16384 && b.X > -16384 && b.Y > -16384
        && b.X < 2000000 && b.Y < 2000000 && b.Inflate(64).Intersects(new(playerX, playerY, 64, 64));
    internal static string CleanName(string? value)
    {
        string text = new string((value ?? "").Where(c => !char.IsControl(c) && c != '[' && c != ']' && c != '^').ToArray()).Trim();
        // Avoid cutting a surrogate pair when enforcing the vanilla-sized input limit.
        if (text.Length > 24) text = text[..24];
        if (text.Length > 0 && char.IsHighSurrogate(text[^1])) text = text[..^1];
        return text;
    }
    /// <summary>Breadth-first search from the parking tiles; returns the path from the reachable tile farthest
    /// from them (in steps) back to a parking tile. Used when no off-screen tile exists on a small map.</summary>
    internal static List<Tile>? RouteFromFarthest(IEnumerable<Tile> stops, Func<Tile, bool> safe, Func<Tile, Tile, bool> safeEdge,
        int width, int height, int limit = 12000)
    {
        var queue = new Queue<Tile>();
        var parent = new Dictionary<Tile, Tile>();
        foreach (var t in stops)
            if (t.X >= 0 && t.Y >= 0 && t.X < width && t.Y < height && !parent.ContainsKey(t) && safe(t)) { parent[t] = t; queue.Enqueue(t); }
        Tile? last = null; int visited = 0;
        var dirs = new[] { new Tile(-1, 0), new Tile(1, 0), new Tile(0, -1), new Tile(0, 1) };
        while (queue.Count > 0 && visited++ < limit)
        {
            Tile t = queue.Dequeue(); last = t;
            foreach (var d in dirs)
            {
                Tile n = new(t.X + d.X, t.Y + d.Y);
                if (n.X >= 0 && n.Y >= 0 && n.X < width && n.Y < height && !parent.ContainsKey(n) && safe(n) && safeEdge(t, n))
                { parent[n] = t; queue.Enqueue(n); }
            }
        }
        if (last is not { } end) return null;
        var result = new List<Tile> { end };
        while (parent[end] != end) { end = parent[end]; result.Add(end); }
        return result;
    }
    internal static int RunFrame(int facing, int phase) => (facing == 0 ? 15 : facing == 2 ? 1 : 8) + Math.Abs(phase % 6);
    internal static int IdleFrame(int facing) => facing == 0 ? 14 : facing == 2 ? 0 : 7;

    /// <summary>Reverse, multi-source breadth-first search using the horse's full footprint.
    /// Returns actual entry/current tile -> parking tile; never teleports to the goal.</summary>
    internal static List<Tile>? Route(IEnumerable<Tile> stops, Func<Tile, bool> isEntry,
        Func<Tile, bool> safe, Func<Tile, Tile, bool> safeEdge, int width, int height, int rotation = 0, int limit = 12000)
    {
        var queue = new Queue<Tile>();
        var parent = new Dictionary<Tile, Tile>();
        foreach (var t in stops)
            if (InBounds(t) && !parent.ContainsKey(t) && safe(t)) { parent[t] = t; queue.Enqueue(t); }
        var dirs = new[] { new Tile(-1, 0), new Tile(1, 0), new Tile(0, -1), new Tile(0, 1) };
        int visited = 0;
        while (queue.Count > 0 && visited++ < limit)
        {
            Tile t = queue.Dequeue();
            if (isEntry(t))
            {
                var result = new List<Tile> { t };
                while (parent[t] != t) { t = parent[t]; result.Add(t); }
                return result;
            }
            for (int i = 0; i < 4; i++)
            {
                Tile d = dirs[(i + (rotation & 3)) % 4], n = new(t.X + d.X, t.Y + d.Y);
                if (InBounds(n) && !parent.ContainsKey(n) && safe(n) && safeEdge(t, n))
                { parent[n] = t; queue.Enqueue(n); }
            }
        }
        return null;
        bool InBounds(Tile p) => p.X >= 0 && p.Y >= 0 && p.X < width && p.Y < height;
    }
}

/// <summary>Authoritative anti-replay/cooldown gate, separately keyed by player.</summary>
internal sealed class HorseRequestGate
{
    private readonly Dictionary<long, (string Id, double Time)> Seen = new();
    private readonly HashSet<(long Actor, string Id)> RequestIds = new();
    internal bool Accept(long actor, string id, double now, double cooldown)
    {
        if (RequestIds.Contains((actor, id)) || Seen.TryGetValue(actor, out var old) && now - old.Time < cooldown) return false;
        RequestIds.Add((actor, id));
        Seen[actor] = (id, now);
        return true;
    }
    internal void Clear() { Seen.Clear(); RequestIds.Clear(); }
}

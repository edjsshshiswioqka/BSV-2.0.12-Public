using StardewModdingAPI;

namespace BetterStardewValley;

/// <summary>
/// Harmony re-runs every transpiler on a method each time any mod patches that method again.
/// The first time a BSV transpiler runs, a missing anchor must fail loudly so the feature rolls
/// back. On a later re-run (another mod patching the same method after BSV), throwing would make
/// the other mod's patch fail, so the original code is kept instead and a warning is logged.
/// </summary>
internal static class TranspilerGuard
{
    private static readonly HashSet<string> Applied = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);
    internal static IMonitor? Monitor;

    internal static void Succeeded(string id) { lock (Applied) Applied.Add(id); }

    /// <summary>Throws on BSV's own first patch; afterwards returns normally so the caller can keep the original code.</summary>
    internal static void Failed(string id, string message)
    {
        lock (Applied)
        {
            if (!Applied.Contains(id)) throw new InvalidOperationException(message);
            if (Warned.Add(id))
                Monitor?.Log($"BSV: another mod changed the code BSV patches ({id}); that BSV detail is paused so the other mod keeps working. {message}", LogLevel.Warn);
        }
    }
}

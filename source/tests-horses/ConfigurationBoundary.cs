namespace BetterStardewValley.Configuration;
// This isolated horse fixture has no multiplayer host; native checks exercise host snapshots.
internal static class SharedPreferences
{
    internal static ModConfig Effective(ModConfig config)=>config;
}

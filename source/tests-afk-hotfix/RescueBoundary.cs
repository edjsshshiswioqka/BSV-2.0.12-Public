// Boundary for the existing AFK-only fixture. This does not test the rescue module.
// Rescue's production rules are exercised separately by tests-rescue; live input
// ownership between the two modules still requires the multiplayer acceptance run.
namespace BetterStardewValley.MultiplayerRescue;
internal static class RescueService
{
    internal static bool SuspendsAfk(StardewValley.Farmer? player) => false;
}

using StardewModdingAPI;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CompatibilityNativeChecks")]
namespace BetterStardewValley;
// Shared save/host boundary for orders, totems and config. No external adapter registration.
internal static class IntegrationHost
{
    internal static bool Saving { get; private set; }
    internal static bool CanMutate => Context.IsWorldReady && Context.IsMainPlayer && !Saving;
    internal static void Register(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        helper.Events.GameLoop.Saving += (_, _) => Saving = true;
        helper.Events.GameLoop.Saved += (_, _) => Saving = false;
        helper.Events.GameLoop.DayStarted += (_, _) => Saving = false;
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => Saving = false;
    }
}

namespace BetterStardewValley.AfkFishing;

internal enum AfkNightPolicy { Unclassified, Protected, LateManual }

// BSV policy; timings are estimates, never changes to the native clock or movement speed.
internal static class AfkReturnRules
{
    internal const int PassOutTime = 2600;
    internal const int EmergencyWindow = 2500;
    internal const double CatchReserveSeconds = 12;
    internal static int Minutes(int clock) => clock / 100 * 60 + clock % 100;
    internal static double RemainingSeconds(int clock, int partialMilliseconds, int millisecondsPerTenMinutes)
        => Math.Max(0, (Minutes(PassOutTime) - Minutes(clock)) / 10d * Math.Max(1, millisecondsPerTenMinutes)
            / 1000d - Math.Max(0, partialMilliseconds) / 1000d);
    internal static double TravelSeconds(int steps, int transitions, int gates, double pixelsPerSecond)
        => pixelsPerSecond > 0 && double.IsFinite(pixelsPerSecond)
            ? Math.Max(0, steps) * 64d / pixelsPerSecond + transitions * 2.5 + gates * 0.5 + 1
            : double.PositiveInfinity;
    internal static double Margin(double estimate) => double.IsFinite(estimate) ? Math.Max(5, estimate * 0.25) : 5;
    internal static AfkNightPolicy ClassifyManual(int clock, double remaining, double estimate)
        => remaining < estimate + Margin(estimate) && (double.IsFinite(estimate) || clock >= AfkRules.HomeTime)
            ? AfkNightPolicy.LateManual : AfkNightPolicy.Protected;
    internal static bool Depart(int clock, double remaining, double estimate)
        => clock >= AfkRules.HomeTime || remaining <= estimate + Margin(estimate) + CatchReserveSeconds;
    internal static bool Emergency(AfkNightPolicy policy, int clock, double remaining, double estimate,
        bool goingHome, bool usedToday)
        => policy == AfkNightPolicy.Protected && goingHome && !usedToday && clock >= EmergencyWindow
            && clock < PassOutTime && remaining <= estimate + Margin(estimate);
}

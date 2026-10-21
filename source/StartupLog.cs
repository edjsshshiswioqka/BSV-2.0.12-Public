namespace BetterStardewValley;

/// <summary>Startup-only, monotonic log gate. Never resets on save or title events.</summary>
internal sealed class StartupLog
{
    private bool Failed;
    private bool Reported;

    internal void Check(bool initialized) => Failed |= !initialized;

    internal void Complete(System.Action<string> info)
    {
        if (Failed || Reported) return;
        Reported = true;
        info("BSV running successfully.");
    }
}

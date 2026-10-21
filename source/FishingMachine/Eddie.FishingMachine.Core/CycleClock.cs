namespace Eddie.FishingMachine.Core;

/// <summary>Calendar clock, not vanilla machine processing minutes or real time.</summary>
public static class CycleClock
{
    public static long ToAbsoluteMinutes(int elapsedDays, int hhmm)
    {
        int hours = hhmm / 100;
        int minutes = hhmm % 100;
        if (elapsedDays < 0 || hhmm < 0 || hours > 26 || minutes >= 60 || (hours == 26 && minutes != 0))
            throw new ArgumentOutOfRangeException(nameof(hhmm), "Expected a valid game clock, up to 2600.");
        return (long)elapsedDays * 1440 + hours * 60 + minutes;
    }

    public static long DueAt(long startedAt, int durationMinutes)
    {
        if (startedAt < 0 || durationMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(startedAt));
        return checked(startedAt + durationMinutes);
    }

    public static bool IsComplete(long now, long dueAt) => now >= dueAt;
}

using System.Collections.Generic;

namespace BetterStardewValley.MultiplayerRescue;

// Commands never contain health, rewards or elapsed progress. revive-applied is
// an owner receipt for an existing host authorization, not authority to grant one.
internal sealed class RescueCommand
{
    public int Protocol { get; set; } = 2;
    public string Epoch { get; set; } = "";
    public string Kind { get; set; } = "";
    public long Sequence { get; set; }
    public long Target { get; set; }
    public string EventId { get; set; } = "";
    public string AttemptId { get; set; } = "";
    public bool Busy { get; set; }
    public bool Alive { get; set; }
    public long DamageSerial { get; set; }
    public double SentAt { get; set; }
}
internal sealed class RescueSnapshot
{
    public int Protocol { get; set; } = 2;
    public string Epoch { get; set; } = "";
    public long Sequence { get; set; }
    public double HostNow { get; set; }
    public double Echo { get; set; }
    public List<RescueRecord> Records { get; set; } = new();
}
internal sealed class RescueClock
{
    private bool initialized;
    private double offset, last, bestRtt = double.PositiveInfinity;
    public void Observe(double hostNow, double sent, double received)
    {
        double rtt = received - sent;
        if (rtt < 0 || rtt > 5) return;
        if (!initialized || rtt < bestRtt) { offset = hostNow + rtt/2 - received; bestRtt = rtt; initialized = true; }
    }
    public double Estimate(double localNow, double lastHostNow)
    {
        double value = initialized ? localNow + offset : lastHostNow;
        // Never move a progress bar backwards as later time samples arrive.
        return last = System.Math.Max(last, value);
    }
}

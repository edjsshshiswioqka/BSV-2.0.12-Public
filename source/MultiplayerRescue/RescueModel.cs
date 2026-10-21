using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterStardewValley.MultiplayerRescue;

// Pure authority model. No game clock, rendering, inventory, health writes or network I/O.
internal enum RescueOutcome { Downed, Revived, VanillaDeath, Night, ReviveAuthorized }
internal sealed class RescueActor
{
    public long Id { get; set; }
    public string Location { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public bool Available { get; set; }
    public bool Alive { get; set; } = true;
    public long DamageSerial { get; set; }
}
internal sealed class RescueRecord
{
    public string EventId { get; set; } = "";
    public long Target { get; set; }
    public string Location { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double DownAt { get; set; }
    public double Deadline { get; set; }
    public long Rescuer { get; set; }
    public string AttemptId { get; set; } = "";
    public double StartedAt { get; set; }
    public double RescuerX { get; set; }
    public double RescuerY { get; set; }
    public long DamageSerial { get; set; }
    public RescueOutcome Outcome { get; set; }
    public double ResolvedAt { get; set; }
    public double AuthorizedAt { get; set; }
    public bool Acknowledged { get; set; }
    public string Reason { get; set; } = "";
    public RescueRecord Copy() => (RescueRecord)MemberwiseClone();
}
internal sealed class RescueModel
{
    public const double DownSeconds = 60, RescueSeconds = 5, Reach = 64, MovementTolerance = 1;
    private readonly Dictionary<long, RescueRecord> records = new();
    // IDs live for this host epoch. A replay cannot create a second lethal event/attempt.
    private readonly HashSet<string> seenEvents = new(StringComparer.Ordinal);
    private readonly HashSet<string> seenAttempts = new(StringComparer.Ordinal);
    public long Revision { get; private set; }
    public IEnumerable<RescueRecord> Records => records.Values;
    public RescueRecord? Find(long player) => records.TryGetValue(player, out var value) ? value : null;
    public bool IsDown(long player) => Find(player)?.Outcome is RescueOutcome.Downed or RescueOutcome.ReviveAuthorized;
    public bool IsRescuing(long player) => records.Values.Any(r => r.Outcome == RescueOutcome.Downed && r.Rescuer == player);
    public static bool ValidId(string id) => Guid.TryParseExact(id, "N", out _);
    private static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((ax-bx)*(ax-bx)+(ay-by)*(ay-by));
    public bool Down(RescueActor actor, string eventId, double now)
    {
        if (!ValidId(eventId) || !actor.Available || IsDown(actor.Id) || !seenEvents.Add(eventId)) return false;
        CancelByRescuer(actor.Id, "rescuer-downed");
        records[actor.Id] = new RescueRecord { EventId = eventId, Target = actor.Id, Location = actor.Location,
            X = actor.X, Y = actor.Y, DownAt = now, Deadline = now + DownSeconds };
        Revision++; return true;
    }
    public bool Begin(RescueActor actor, long target, string eventId, string attemptId, double now)
    {
        var r = Find(target);
        if (r == null || r.Outcome != RescueOutcome.Downed || r.EventId != eventId || now >= r.Deadline
            || r.Rescuer != 0 || actor.Id == target || IsDown(actor.Id) || IsRescuing(actor.Id)
            || !actor.Available || !actor.Alive || actor.Location != r.Location || Distance(actor.X, actor.Y, r.X, r.Y) > Reach
            || !ValidId(attemptId) || !seenAttempts.Add(attemptId)) return false;
        r.Rescuer = actor.Id; r.AttemptId = attemptId; r.StartedAt = now;
        r.RescuerX = actor.X; r.RescuerY = actor.Y; r.DamageSerial = actor.DamageSerial;
        r.Reason = ""; Revision++; return true;
    }
    public bool Cancel(long actor, string eventId, string attemptId, string reason)
    {
        var r = records.Values.FirstOrDefault(x => x.Outcome == RescueOutcome.Downed && x.Rescuer == actor
            && x.EventId == eventId && x.AttemptId == attemptId);
        if (r == null) return false;
        ClearAttempt(r, reason); return true;
    }
    public void CancelByRescuer(long actor, string reason)
    {
        foreach (var r in records.Values.Where(x => x.Outcome == RescueOutcome.Downed && x.Rescuer == actor).ToArray()) ClearAttempt(r, reason);
    }
    private void ClearAttempt(RescueRecord r, string reason)
    {
        r.Rescuer = 0; r.AttemptId = ""; r.StartedAt = 0; r.Reason = reason; Revision++;
    }
    public bool GiveUp(long actor, string eventId, double now)
    {
        var r = Find(actor);
        if (r == null || r.Outcome is not (RescueOutcome.Downed or RescueOutcome.ReviveAuthorized) || r.EventId != eventId) return false;
        Resolve(r, RescueOutcome.VanillaDeath, now, "give-up"); return true;
    }
    public bool TerminateUnknown(long actor, string eventId, double now)
    {
        if (!ValidId(eventId) || IsDown(actor) || !seenEvents.Add(eventId)) return false;
        records[actor] = new RescueRecord { Target=actor, EventId=eventId, DownAt=now, Deadline=now,
            Outcome=RescueOutcome.VanillaDeath, ResolvedAt=now, Reason="give-up-before-down" };
        Revision++; return true;
    }
    public bool ConfirmRevived(long actor, string eventId, string attemptId, double now)
    {
        var r=Find(actor);
        // Only the authorized target can acknowledge application. A delayed receipt
        // may outlive a host timeout, but cannot invent an authorization/attempt.
        if(r==null || r.EventId!=eventId || r.AttemptId!=attemptId || !ValidId(attemptId)
            || r.AuthorizedAt<=0 || r.Outcome==RescueOutcome.Revived)return false;
        r.Acknowledged=true;Resolve(r,RescueOutcome.Revived,now,"owner-applied");return true;
    }
    public void AcknowledgeNative(long actor,string id)
    {
        var r=Find(actor);
        if(r?.EventId==id && r.Outcome is RescueOutcome.VanillaDeath or RescueOutcome.Night && !r.Acknowledged)
        {r.Acknowledged=true;Revision++;}
    }
    public void Disconnect(long actor, double now)
    {
        CancelByRescuer(actor, "disconnect");
        var r = Find(actor);
        if (IsDown(actor)) Resolve(r!, RescueOutcome.VanillaDeath, now, "disconnect");
    }
    public void Tick(double now, Func<long, RescueActor?> lookup, bool night)
    {
        foreach (var r in records.Values.Where(x => x.Outcome is RescueOutcome.Downed or RescueOutcome.ReviveAuthorized).ToArray())
        {
            // Native 2 AM transition wins; at an exact deadline tie, timeout wins.
            if (night) { Resolve(r, RescueOutcome.Night, now, "native-night"); continue; }
            if (now >= r.Deadline) { Resolve(r, RescueOutcome.VanillaDeath, now, "timeout"); continue; }
            if (r.Outcome==RescueOutcome.ReviveAuthorized) continue;
            var target = lookup(r.Target);
            if (target == null || target.Location != r.Location)
            { Resolve(r, RescueOutcome.VanillaDeath, now, "target-unavailable"); continue; }
            if (r.Rescuer == 0) continue;
            var actor = lookup(r.Rescuer);
            if (actor == null || !actor.Available || !actor.Alive || !target.Available || actor.Location != r.Location
                || actor.DamageSerial != r.DamageSerial || IsDown(actor.Id)
                || Distance(actor.X, actor.Y, r.RescuerX, r.RescuerY) > MovementTolerance
                || Distance(target.X, target.Y, r.X, r.Y) > MovementTolerance
                || Distance(actor.X, actor.Y, target.X, target.Y) > Reach)
            { ClearAttempt(r, "interrupted"); continue; }
            if (now - r.StartedAt >= RescueSeconds)
            { r.AuthorizedAt=now; Resolve(r, RescueOutcome.ReviveAuthorized, now, "authorized-awaiting-owner"); }
        }
    }
    private void Resolve(RescueRecord r, RescueOutcome outcome, double now, string reason)
    { r.Outcome = outcome; r.ResolvedAt = now; r.Reason = reason; Revision++; }
    public static int RestoredHealth(int maxHealth) => Math.Max(1, (int)Math.Ceiling(Math.Max(1, maxHealth) * .10));
    public static double Progress(double hostNow, double startedAt) => Math.Clamp((hostNow-startedAt)/RescueSeconds, 0, 1);
}

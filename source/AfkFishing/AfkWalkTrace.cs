using StardewModdingAPI;
using StardewValley;

namespace BetterStardewValley.AfkFishing;

internal sealed partial class AfkFishingService
{
    // Opt-in, bounded diagnostics. No trace allocation, reflection or file I/O on
    // ordinary updates when disabled; manual movement can be recorded for comparison.
    private sealed class WalkTrace
    {
        internal double Started, Duration;
        internal long Player;
        internal int Invalidations, Searches;
        internal string Reason = "start", File = "";
        internal readonly List<object> Rows = new();
    }
    private void RegisterWalkTrace()
    {
        Helper.ConsoleCommands.Add("bsv_afk_walk_trace", Tr("afk.trace.help"), (_, args) =>
        {
            if (args.Length == 1 && args[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
            { FinishWalkTrace(Current, "requested stop"); return; }
            if (!Context.IsWorldReady) { Monitor.Log(Tr("diag.load-save"), LogLevel.Info); return; }
            int seconds = 20;
            if (args.Length > 1 || (args.Length == 1 && (!int.TryParse(args[0], out seconds) || seconds < 5 || seconds > 120)))
            { Monitor.Log(Tr("afk.trace.help"), LogLevel.Info); return; }
            FinishWalkTrace(Current, "new recording");
            Current.Trace = new() { Started = Now, Duration = seconds, Player = Game1.player.UniqueMultiplayerID,
                File = $"diagnostics/afk-walk-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Game1.player.UniqueMultiplayerID}-{Context.ScreenId}.json" };
            Monitor.Log(Tr("afk.trace.started", new { seconds }), LogLevel.Info);
        });
    }
    private void CaptureWalkTrace()
    {
        var s = Current; var trace = s.Trace;
        if (trace == null) return;
        if (!Context.IsWorldReady || Game1.player.currentLocation?.Map == null)
        { FinishWalkTrace(s, "world unavailable"); return; }
        var who = Game1.player;
        // Read after the game's previous update; the next BSV Tick only issues intent.
        trace.Rows.Add(new { seconds = Now - trace.Started, player = who.UniqueMultiplayerID,
            location = who.currentLocation.NameOrUniqueName, x = who.Position.X, y = who.Position.Y,
            box=who.GetBoundingBox(),blockers=who.currentLocation.characters.Where(n=>AfkWorld.CharacterBox(n).Intersects(who.GetBoundingBox())).Select(n=>new{name=n.Name,box=AfkWorld.CharacterBox(n)}).ToArray(),nativePet=s.NativePet?.Name,retries=s.RecoveryCount,standingX = who.StandingPixel.X, standingY = who.StandingPixel.Y, facing = who.FacingDirection,
            running = who.running, speed = who.getMovementSpeed(), spriteFrame = who.FarmerSprite.CurrentFrame, directions = who.movementDirections.ToArray(),
            controller = who.controller?.GetType().FullName ?? "none", who.CanMove, who.UsingTool,
            runtime = RuntimeState(),
            enabled = s.Data.Enabled, phase = s.Data.Phase.ToString(), node = s.RouteIndex,
            target = s.WalkTarget?.ToString(), walkActive = s.WalkActive, taskGeneration = s.Generation,
            walkGeneration = s.WalkGeneration, invalidations = trace.Invalidations, searches = trace.Searches,
            reason = trace.Reason });
        if (Now - trace.Started >= trace.Duration || trace.Rows.Count >= 7200) FinishWalkTrace(s, "bounded recording complete");
    }
    private void WalkReason(string reason)
    {
        if (Current.Trace != null) Current.Trace.Reason = reason;
    }
    private void FinishWalkTrace(Session session, string end)
    {
        var trace = session.Trace; if (trace == null) return;
        session.Trace = null;
        try
        {
            Helper.Data.WriteJsonFile(trace.File, new { schema = 1, trace.Player, end,
                game = "Stardew Valley 1.6.15", version = typeof(AfkFishingService).Assembly.GetName().Version?.ToString(), trace.Duration,
                trace.Invalidations, trace.Searches, rows = trace.Rows });
            Monitor.Log(Tr("afk.trace.saved", new { path = trace.File }), LogLevel.Info);
        }
        catch (Exception ex) { Monitor.Log("BSV AFK trace write failed: " + ex.Message, LogLevel.Warn); }
    }
}

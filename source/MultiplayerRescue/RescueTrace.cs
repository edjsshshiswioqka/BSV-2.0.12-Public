using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using StardewModdingAPI;

namespace BetterStardewValley.MultiplayerRescue;

internal sealed partial class RescueService
{
    private readonly List<string> TraceRows = new();
    private double TraceUntil;
    private void RegisterTrace()
    {
        Helper.ConsoleCommands.Add("bsv_rescue_trace", Tr("trace-help"), (_, _) =>
        {
            TraceRows.Clear(); TraceUntil = Now + 15;
            Monitor.Log(Tr("trace-start"), LogLevel.Info);
        });
    }
    private void TraceFrame(RescueRecord r, double p, Rectangle fill)
    {
        if (TraceUntil <= 0 || Now > TraceUntil || TraceRows.Count >= 20000) return;
        TraceRows.Add(FormattableString.Invariant($"{Now:F6},{HostNow:F6},{r.EventId},{r.AttemptId},{r.Target},{r.Rescuer},{r.StartedAt:F6},{p:F6},{fill.X},{fill.Y},{fill.Width},{fill.Height}"));
    }
    private void FinishTrace()
    {
        if (TraceUntil <= 0 || Now < TraceUntil) return;
        TraceUntil = 0;
        string dir = Path.Combine(Helper.DirectoryPath,"diagnostics"); Directory.CreateDirectory(dir);
        string path = Path.Combine(dir,"rescue-render-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+".csv");
        File.WriteAllLines(path,new[]{"localSeconds,estimatedHostSeconds,event,attempt,target,rescuer,startedHostSeconds,progress,fillX,fillY,fillWidth,fillHeight"}.Concat(TraceRows),Encoding.UTF8);
        Monitor.Log(Tr("trace-saved",new{count=TraceRows.Count,path}),LogLevel.Info);
        TraceRows.Clear();
    }
}

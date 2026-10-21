using System.Globalization;
using System.Text.Json;
using BetterStardewValley;
using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;

int checks=0;
void Check(bool condition,string description){checks++;if(!condition)throw new Exception(description);}
float Measure(string text)=>StringInfo.ParseCombiningCharacters(text).Length*14;
var locales=Directory.GetFiles(args[0],"*.json").ToDictionary(Path.GetFileNameWithoutExtension,
    f=>JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(f))!);
var reasons=new Dictionary<string,string>
{
    ["Machine owner unavailable"]="machine.pause.owner",
    ["Machine owner unavailable."]="machine.pause.owner",
    ["Paid machine moved; historical geometry does not match."]="machine.pause.moved",
    ["Seasonal quota busy; opportunity retained."]="machine.pause.quota",
    ["Dated legendary quota changed; opportunity retained."]="machine.pause.quota",
    ["Legendary quota changed before production."]="machine.pause.quota",
    ["Waiting for the owner's in-flight manual catch before settling legendary quota."]="machine.pause.manual-catch",
    ["No supported trap ecology"]="machine.pause.ecology",
    ["Machine water geometry is no longer valid"]="machine.pause.geometry",
    ["No recorded environment for paid cycle."]="machine.pause.history",
    ["Missing historical environment; paid opportunity retained (no guessed weather)."]="machine.pause.history",
    ["Past-day quota observation was not sealed; paid opportunity retained."]="machine.pause.history",
    ["World clock moved backwards; history retained."]="machine.pause.history",
    ["Observed day is already sealed; history retained."]="machine.pause.history",
    ["Missing timeline journal."]="machine.pause.ledger",
    ["Unsupported timeline journal; preserved."]="machine.pause.ledger",
    ["Invalid legendary reservations."]="machine.pause.ledger",
    ["Invalid dated legendary ledger."]="machine.pause.ledger",
    ["third-party error: raw diagnostic must not appear in UI"]="machine.pause.unknown"
};
foreach(var (raw,key) in reasons)
{
    // Exercise a real persisted timeline value; display mapping must not rewrite it.
    var timeline=new MachineTimeline {PausedReason=raw};
    string before=JsonSerializer.Serialize(timeline);
    Check(MachinePauseText.KeyFor(timeline.PausedReason)==key,"known/unknown reason routing: "+raw);
    Check(JsonSerializer.Serialize(timeline)==before,"display mapping preserves persisted timeline");
    foreach(var (locale,texts) in locales)
    {
        string text=texts[MachinePauseText.KeyFor(raw)];
        Check(!string.IsNullOrWhiteSpace(text)&&text!=raw,"localized reason instead of raw error: "+locale);
        string rendered=texts["status.timeline-paused"].Replace("{{reason}}",text);
        Check(!rendered.Contains("{{")&&!rendered.Contains("third-party error"),"reason template resolves: "+locale);
    }
}
Check(MachinePauseText.KeyFor(null)=="machine.pause.unknown"&&MachinePauseText.KeyFor("  ")=="machine.pause.unknown","missing reason has safe localized explanation");
foreach(var (locale,texts) in locales)
foreach(var screen in new[]{(320,180),(640,360),(800,600),(1280,720),(1920,1080)})
{
    string text=texts["rescue.native-handoff"];
    var r=DisplayText.HudNotice(text,screen.Item1,screen.Item2,24,Measure);
    Check(r.X>=0&&r.Y>=0&&r.X+r.Width<=screen.Item1&&r.Y+r.Height<=screen.Item2,"notice bounds: "+locale);
    Check(r.Text.Split('\n').All(line=>Measure(line)<=r.Width),"notice line width: "+locale);
    Check(r.Text.Replace("\n","").Replace(" ","")==text.Replace(" ",""),"full shipped notice preserved at supported sizes: "+locale);
}
foreach(string text in new[]{"无空格中文长段落的恢复提示","a\u0301😀 a\u0301😀 a\u0301😀",new string('X',10000)})
foreach(var screen in new[]{(0,0),(1,1),(40,30),(100,60),(320,180)})
{
    var r=DisplayText.HudNotice(text,screen.Item1,screen.Item2,24,Measure);
    Check(r.X>=0&&r.Y>=0&&r.X+r.Width<=screen.Item1&&r.Y+r.Height<=screen.Item2,"tiny viewport clipping safety");
    Check(r.Text.Split('\n').All(line=>Measure(line)<=r.Width),"corrupt/long/tiny notice bounded");
    Check(!r.Text.Contains('\ufffd'),"Unicode text elements preserved");
}
foreach(float bad in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
    Check(DisplayText.HudNotice("text",320,180,bad,Measure).Text=="","invalid metrics produce no offscreen draw");
foreach(var texts in locales.Values)
{
    Check(texts["cmd.sewer-usage"].Contains("bsv_sewer_status [anchorX anchorY]"),"command syntax unchanged");
    Check(texts["diag.machine.standalone-conflict"].Contains("Eddie.FishingMachine"),"conflicting mod identity unchanged");
}
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,assertions=checks,locales=locales.Keys,
    scope="Actual C# display helpers and serialized timeline data; deterministic font metric delegate, not a real game render."}));

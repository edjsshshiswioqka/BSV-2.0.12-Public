namespace Eddie.FishingMachine.Core;
public sealed record TimedMachineFish(int From,int Until,List<FishChoice> Fish);
public sealed class MachineEnvironmentProfile
{
    public string Location {get;set;}="";
    public string MachineId {get;set;}="";
    public int X {get;set;}
    public int Y {get;set;}
    public int Day {get;set;}
    public bool Rain {get;set;}
    public bool UnknownWeather {get;set;}
    public Dictionary<int,List<TimedMachineFish>> SafeFish {get;set;}=new();
    public string? Error {get;set;}
    public bool Ocean {get;set;}
    public bool Sewer {get;set;}
    public bool KingCrabEnabled {get;set;}
    public List<TimedMachineFish> Fish {get;set;}=new();
    public List<CrabAquatic> Crab {get;set;}=new();
    public List<string> Other {get;set;}=new();
    public string? Jelly {get;set;}
}
public sealed class MachineEnvironmentWindow
{
    public long From {get;set;}
    public long Until {get;set;}
    public MachineEnvironmentProfile Profile {get;set;}=new();
}
public sealed class MachineEnvironmentJournal
{
    public int Version {get;set;}=1;
    public Dictionary<string,List<MachineEnvironmentWindow>> Cycles {get;set;}=new();
    public void Record(string token,long now,MachineEnvironmentProfile profile,bool closeDay)
    {
        if(Version!=1||now<(long)profile.Day*1440+360||now>(long)profile.Day*1440+1560)throw new ArgumentException("Invalid observed day.");
        if(!Cycles.TryGetValue(token,out var frames))Cycles[token]=frames=new();
        var last=frames.LastOrDefault();
        if(last!=null&&now<last.From)return; // rollback never overwrites the saved high-water history
        // Only extend an observed window within its own game day. Never bridge unknown skipped days.
        if(last!=null&&last.Profile.Day==profile.Day)last.Until=Math.Max(last.Until,now);
        long until=closeDay?(long)profile.Day*1440+1560:now;
        bool equal=last!=null&&last.Profile.Day==profile.Day&&System.Text.Json.JsonSerializer.Serialize(last.Profile)==System.Text.Json.JsonSerializer.Serialize(profile);
        if(equal)last!.Until=Math.Max(last.Until,until);
        else
        {
            // Once a day is sealed, it is immutable. A repeated callback may not revise sleep outcomes.
            if(last!=null&&last.Until>now)throw new InvalidOperationException("Observed day is already sealed; history retained.");
            frames.Add(new(){From=now,Until=until,Profile=System.Text.Json.JsonSerializer.Deserialize<MachineEnvironmentProfile>(System.Text.Json.JsonSerializer.Serialize(profile))!});
        }
    }
    public MachineEnvironmentProfile At(string token,long minute)
    {
        if(Version!=1||!Cycles.TryGetValue(token,out var frames))throw new InvalidOperationException("No recorded environment for paid cycle.");
        var found=frames.LastOrDefault(w=>minute>=w.From&&minute<w.Until&&w.Profile.Day==MachineTimeline.Day(minute));
        if(found!=null)return found.Profile;
        var basis=frames.LastOrDefault(w=>w.From<=minute);
        var fallback=System.Text.Json.JsonSerializer.Deserialize<MachineEnvironmentProfile>(System.Text.Json.JsonSerializer.Serialize((basis??frames.First()).Profile))!;
        fallback.Day=MachineTimeline.Day(minute);fallback.UnknownWeather=true;fallback.Rain=false;
        fallback.Fish=basis?.Profile.SafeFish.GetValueOrDefault(MachineTimeline.Season(fallback.Day))??new();
        // Missing evidence cannot license rare catches. The runtime retains a paid opportunity if no legal non-trash fallback exists.
        if(basis==null){fallback.Jelly=null;fallback.Other=new(){"(O)168","(O)169","(O)170","(O)171","(O)172"};}
        return fallback;
    }
}

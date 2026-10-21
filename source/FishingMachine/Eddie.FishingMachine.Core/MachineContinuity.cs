using System.Text.Json;
namespace Eddie.FishingMachine.Core;

public sealed record MachineFuel(string Key,long At,long Count);
public sealed class MachinePolicy
{
    public long At {get;set;}
    public bool Auto {get;set;}
    public BalanceConfig Fish {get;set;}=new();
    public CrabBalanceConfig Crab {get;set;}=new();
    public bool Lure {get;set;}
    public bool Book {get;set;}
    public string Signature()=>JsonSerializer.Serialize(new{Auto,Fish,Crab,Lure,Book});
}
/// <summary>Saved evidence of fuel and policy availability; never a second inventory.</summary>
public sealed class MachineContinuity
{
    public int Version {get;set;}=1;
    public long ObservedAt {get;set;}
    public long NextAt {get;set;}
    public bool Stopped {get;set;}=true;
    public string? StopReason {get;set;}
    public string EnvironmentToken {get;set;}="";
    public long InputCursor {get;set;}
    public List<MachineFuel> Fuel {get;set;}=new();
    public List<MachinePolicy> Policies {get;set;}=new();
    public MachineContinuity Copy(){var c=(MachineContinuity)MemberwiseClone();c.Fuel=Fuel.ToList();c.Policies=Policies.ToList();return c;}
    public void ObserveFuel(long now,IReadOnlyDictionary<string,long> inventory)
    {
        long at=Math.Max(now,ObservedAt);ObservedAt=at;
        foreach(string key in Fuel.Select(p=>p.Key).Union(inventory.Keys).Distinct().ToArray())
        {
            long before=Fuel.Where(p=>p.Key==key).Sum(p=>p.Count),after=inventory.GetValueOrDefault(key);
            if(after>before)Fuel.Add(new(key,at,after-before));
            // A withdrawal destroys the oldest historical entitlement first.
            else if(after<before)Spend(key,before-after,long.MaxValue);
        }
    }
    public void Observe(long now,IReadOnlyDictionary<string,long> inventory,MachinePolicy policy)
    {
        ObserveFuel(now,inventory);long at=ObservedAt;
        if(Policies.Count==0||Policies[^1].Signature()!=policy.Signature())
        {
            policy.At=at;
            if(Policies.Count>0&&Policies[^1].At==at)Policies.RemoveAt(Policies.Count-1);
            Policies.Add(JsonSerializer.Deserialize<MachinePolicy>(JsonSerializer.Serialize(policy))!);
        }
        if(Stopped)NextAt=at;
        int keep=Policies.FindLastIndex(p=>p.At<=NextAt);if(keep>0)Policies.RemoveRange(0,keep);
    }
    public MachinePolicy PolicyAt(long at)=>Policies.LastOrDefault(p=>p.At<=at)??Policies[0];
    public long Available(string key,long at)=>Fuel.Where(p=>p.Key==key&&p.At<=at).Sum(p=>p.Count);
    public void Spend(string key,long count,long at)
    {
        if(count<0||Available(key,at)<count)throw new InvalidOperationException("Historical fuel unavailable; inventory preserved.");
        for(int i=0;i<Fuel.Count&&count>0;i++)
        {
            var lot=Fuel[i];if(lot.Key!=key||lot.At>at)continue;
            long take=Math.Min(count,lot.Count);Fuel[i]=lot with{Count=lot.Count-take};count-=take;
        }
        Fuel.RemoveAll(p=>p.Count==0);
    }
    public void Started(long due){Stopped=false;StopReason=null;NextAt=due;}
    public void Stop(long now,string reason){Stopped=true;StopReason=reason;NextAt=Math.Max(now,ObservedAt);}
    public bool Valid()=>Version==1&&InputCursor>=0&&ObservedAt>=360&&NextAt>=360&&Fuel!=null&&Policies!=null&&Policies.Count>0
        &&Fuel.All(p=>p.Count>0&&p.At>=360&&p.At<=ObservedAt&&!string.IsNullOrEmpty(p.Key))
        &&Policies.All(p=>p.At>=360&&p.At<=ObservedAt&&p.Fish!=null&&p.Crab!=null)
        &&Policies.SequenceEqual(Policies.OrderBy(p=>p.At));
}

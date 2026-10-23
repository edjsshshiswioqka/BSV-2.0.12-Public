using System.Text.Json;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Objects;
namespace Eddie.FishingMachine;

internal sealed partial class ProductionService
{
    private void RememberCommit(Chest c,string raw,ProductionState state)
    {
        string? option=c.modData.TryGetValue(AutoTrashKey,out var text)?text:null;
        Cache.Remove(c);Cache.Add(c,new(raw,option,state.Snapshot(),null));
        if(!Mod.Config.DebugProduction)return;
        string key=state.CycleToken+"/"+state.Phase+"/"+state.CompletedCycles+"/"+state.Level+"/"+state.Timeline?.PausedReason+"/"+state.Continuity?.StopReason;
        if(TraceStates.TryGetValue(c,out string? old)&&old==key)return;TraceStates[c]=key;
        Mod.Monitor.Log($"[BSV production] machine={c.modData.GetValueOrDefault(ModEntry.Tag+"MachineId")} kind={state.Kind} level={state.Level} location={c.Location.NameOrUniqueName}:{c.TileLocation} token={state.CycleToken} start={state.StartedAt} due={state.DueAt} now={Now} phase={state.Phase} settlement={state.Timeline?.Cursor}/{state.Timeline?.Plan.Count} paidBait={state.BaitUsed} storedBait={StoredBait(c)} pending={state.Pending.Sum(p=>(long)p.Count)} completed={state.CompletedCycles} levelCycles={state.LevelCycles} reason={state.Timeline?.PausedReason??(state.Continuity?.StopReason??"running")}",StardewModdingAPI.LogLevel.Debug);
    }
    private readonly Dictionary<Chest,string> TraceStates=new();
    // Per-frame catch-up only for work that can actually run now. A paused timeline or a machine whose
    // menu is open (mutex held) waits for the once-per-second tick instead of retrying every frame.
    internal bool HasBacklog=>Mod.Machines.All.Any(p=>GetCached(p.Key).State is {Phase:"working"} s&&s.Timeline?.PausedReason==null
        &&!p.Key.GetMutex().IsLocked()&&NextEvent(p.Key)<Now);
    private bool Scheduling;
    private int InventoryMutation;
    partial void TrackInputs(Chest c);
    partial void ApplyInputEvents(Chest c,MachineContinuity history);
    partial void PruneInputEvents(Chest c,long cursor);
    private static string FuelKey(Item item)=>JsonSerializer.Serialize(new {Id=item.QualifiedItemId,item.Quality,
        Parent=(item as StardewValley.Object)?.preservedParentSheetIndex.Value,
        Data=item.modData.Keys.OrderBy(k=>k,StringComparer.Ordinal).ToDictionary(k=>k,k=>item.modData[k])});
    private Dictionary<string,long> FuelInventory(Chest c)=>c.Items.Where(i=>i!=null&&i.Stack>0
        &&(ModEntry.IsCrab(c)?CrabBaitItem(i):Bait(i,0,Level(c))!=null))
        .GroupBy(FuelKey).ToDictionary(g=>g.Key,g=>g.Sum(i=>(long)i.Stack));
    private void ObserveContinuity(Chest c)
    {
        TrackInputs(c);var s=Read(c).Snapshot();string before=s.Save();
        // Old saves provide evidence for the paid task only, not ancient unattended purchases.
        s.Continuity??=new(){ObservedAt=Now,NextAt=Math.Max(Now,s.DueAt),Stopped=true};
        ApplyInputEvents(c,s.Continuity);
        var owner=CrabOperator(c);
        s.Continuity.Observe(Now,FuelInventory(c),new(){Auto=BetterStardewValley.Configuration.SharedPreferences.Current.AutoRestartProduction,
            Fish=Mod.Config,Crab=Mod.CrabConfig,Lure=owner?.professions.Contains(11)==true,Book=owner?.stats.Get("Book_Crabbing")!=0&&owner!=null});
        if(before!=s.Save())Commit(c,null,s);
        PruneInputEvents(c,s.Continuity.InputCursor);
    }
    private long StartAt(ProductionState s,bool manual)=>manual?Math.Max(Now,s.Continuity!.ObservedAt):s.Continuity!.NextAt;
    private void StopContinuity(Chest c,string reason="paused")
    {
        var s=Read(c).Snapshot();if(s.Continuity==null)return;
        string before=s.Save();s.Continuity.Stop(Now,reason);if(before!=s.Save())Commit(c,null,s);
    }
    private Dictionary<string,long> FuelBudget(ProductionState s,long at)=>s.Continuity!.Fuel.Select(p=>p.Key).Distinct()
        .ToDictionary(k=>k,k=>s.Continuity.Available(k,at));
    private int FuelLimit(Item item,Dictionary<string,long> budget)
    {
        string key=FuelKey(item);int n=(int)Math.Min(item.Stack,budget.GetValueOrDefault(key));budget[key]=budget.GetValueOrDefault(key)-n;return n;
    }
    private void PayEvidence(Chest c,ProductionState next,IEnumerable<(int Index,int Count)> plan,long at)
    {
        foreach(var spend in plan)next.Continuity!.Spend(FuelKey(c.Items[spend.Index]),spend.Count,at);
        next.Continuity!.EnvironmentToken=next.CycleToken;next.Continuity.Started(next.DueAt);
    }
    private long NextEvent(Chest c)
    {
        try
        {
            var s=Read(c);
            if(s.Phase=="working")return s.Timeline is {} t&&t.Cursor<t.Plan.Count?t.Plan[t.Cursor].At:s.DueAt;
            return s.Phase=="ready"?Math.Min(Now,s.DueAt):s.Continuity?.NextAt??Now;
        }catch{return long.MaxValue;}
    }
    private bool WithLock(Chest c,GameLocation l,Action work)
    {
        c.GetMutex().Update(l);
        if(c.GetMutex().IsLocked()||LockRequests.Contains(c))return false;
        bool ran=false;LockRequests.Add(c);
        c.GetMutex().RequestLock(()=>
        {
            try
            {
                if(StardewModdingAPI.Context.IsWorldReady&&StardewModdingAPI.Context.IsMainPlayer&&!Mod.Saving
                    &&c.GetMutex().IsLockHeld()&&Mod.Machines.Contains(c,l)&&l.objects.TryGetValue(c.TileLocation,out var same)
                    &&ReferenceEquals(c,same)&&CompleteFootprint(c)){ran=true;work();}
            }
            catch(Exception ex)
            {
                ran=false;
                string key=l.NameOrUniqueName+":"+c.TileLocation+":"+ex.Message;
                if(Errors.Add(key))Mod.Monitor.Log("Machine paused; saved inventory/task preserved: "+key+"\n"+ex,StardewModdingAPI.LogLevel.Error);
            }
            finally{c.GetMutex().ReleaseLock();LockRequests.Remove(c);}
        },()=>LockRequests.Remove(c));
        c.GetMutex().Update(l);return ran;
    }
    private void TickContinuity()
    {
        if(Scheduling)return;Scheduling=true;
        try
        {
            var machines=Mod.Machines.All.ToArray();var unavailable=new HashSet<Chest>();
            foreach(var p in machines)
            {
                if(!p.Value.objects.TryGetValue(p.Key.TileLocation,out var current)||!ReferenceEquals(current,p.Key)){Mod.Machines.Remove(p.Key);continue;}
                if(!WithLock(p.Key,p.Value,()=>{MigrateCycle(p.Key);ObserveContinuity(p.Key);}))unavailable.Add(p.Key);
            }
            var blocked=new HashSet<Chest>();
            // One opportunity per visit, globally ordered. Persisted cursors survive budget exhaustion.
            var clock=System.Diagnostics.Stopwatch.StartNew();
            for(int budget=0;budget<128 && clock.ElapsedMilliseconds<12;budget++)
            {
                var candidate=machines.Where(p=>Mod.Machines.Contains(p.Key,p.Value)&&!blocked.Contains(p.Key)&&NeedsWork(p.Key)&&NextEvent(p.Key)<=Now)
                    .OrderBy(p=>NextEvent(p.Key)).ThenBy(p=>p.Key.modData.TryGetValue(ModEntry.Tag+"MachineId",out var id)?id:"",StringComparer.Ordinal).FirstOrDefault();
                var c=candidate.Key;if(c==null||unavailable.Contains(c))break;
                string before=Read(c).Save();long eventAt=NextEvent(c);
                if(!WithLock(c,candidate.Value,()=>ProcessAutomatic(c)))break; // never overtake a locked historical event
                if(Read(c).Save()==before)
                {
                    // A paid event cannot be overtaken while retrying content, quota or transaction failures.
                    if(Read(c).Phase=="working"&&eventAt<Now)break;
                    blocked.Add(c);
                }
            }
        }
        finally{Scheduling=false;}
    }
}

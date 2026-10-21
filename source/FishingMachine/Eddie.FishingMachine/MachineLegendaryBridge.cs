using System.Text.Json;
using BetterStardewValley;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Tools;

namespace Eddie.FishingMachine;

/// <summary>Host-authoritative reservations plus the same annual record used by manual fishing.</summary>
internal static class MachineLegendaryBridge
{
    private const string LedgerKey=ModEntry.Tag+"LegendaryReservations";
    private static string Key(long owner,string id,int year)=>$"{owner}/{year}/{id}";
    internal static bool IsLegendary(string id)=>AnnualLegendaryRules.NormalizeId(id)!=null;
    internal static int CompletionYear(long minute)=>(int)(Math.Max(0,minute-360)/1440/112)+1;
    private static Dictionary<string,string> Read()=>Game1.MasterPlayer.modData.TryGetValue(LedgerKey,out var raw)
        ? JsonSerializer.Deserialize<Dictionary<string,string>>(raw) ?? throw new InvalidOperationException("Invalid legendary reservations.") : new();
    internal static bool Reserved(long owner,string id,int year)=>Read().ContainsKey(Key(owner,"(O)"+AnnualLegendaryRules.NormalizeId(id),year));
    internal static bool Available(Farmer owner,string id,int year)=>AnnualLegendaryFeature.MachineAvailable(owner,id,year)
        && !Reserved(owner.UniqueMultiplayerID,id,year);
    private const string SettledKey=ModEntry.Tag+"TimelineLegendarySettled/v1";
    private static Dictionary<string,string> Settled()=>Game1.MasterPlayer.modData.TryGetValue(SettledKey,out var raw)
        ? JsonSerializer.Deserialize<Dictionary<string,string>>(raw)??throw new InvalidOperationException("Invalid dated legendary ledger."):new();
    internal static bool AvailableTimeline(Farmer owner,string id,int year,bool observed)
        => observed&&year<=Game1.year&&!Reserved(owner.UniqueMultiplayerID,id,year)
            &&!Settled().ContainsKey(Key(owner.UniqueMultiplayerID,id,year))
            &&AnnualLegendaryFeature.MachineAvailable(owner,id,year,historical:true);
    internal static bool ManualCatchPending(long ownerId)
    {
        var owner=Game1.getOnlineFarmers().FirstOrDefault(p=>p.UniqueMultiplayerID==ownerId);
        return owner?.CurrentTool is FishingRod rod&&(rod.isFishing||rod.isReeling||rod.pullingOutOfWater||rod.hit||rod.fishCaught);
    }
    internal static void CommitTimeline(Farmer owner,int day,ProductionState unit,ProductionState next,Action commit)
    {
        if(unit.LegendaryPlans.Count==0){commit();return;}
        if(ManualCatchPending(owner.UniqueMultiplayerID))throw new InvalidOperationException("Waiting for the owner's in-flight manual catch before settling legendary quota.");
        int year=MachineTimeline.Year(day);var ledger=Settled();
        bool had=Game1.MasterPlayer.modData.TryGetValue(SettledKey,out string raw);
        var before=new Dictionary<string,string?>();
        try
        {
            foreach(var plan in unit.LegendaryPlans)
            {
                if(!AvailableTimeline(owner,plan.Id,year,true))throw new InvalidOperationException("Dated legendary quota changed; opportunity retained.");
                string record=AnnualLegendaryRules.KeyPrefix+AnnualLegendaryRules.NormalizeId(plan.Id);
                before[record]=owner.modData.TryGetValue(record,out var old)?old:null;
                string token=next.CycleToken+":"+next.Timeline!.Cursor;
                ledger.Add(Key(owner.UniqueMultiplayerID,plan.Id,year),token);
                AnnualLegendaryFeature.MarkMachineCatch(owner,plan.Id,year);
                next.Timeline.Claims.Add(new(day,plan.Id,token));
            }
            Game1.MasterPlayer.modData[SettledKey]=JsonSerializer.Serialize(ledger);commit();
        }
        catch
        {
            foreach(var pair in before)Restore(owner,pair.Key,pair.Value);
            Restore(Game1.MasterPlayer,SettledKey,had?raw:null);throw;
        }
    }
    internal static bool ReserveManual(Farmer owner,int year,string token)
    {
        var ledger=Read();string key=Key(owner.UniqueMultiplayerID,"(O)Eddie.BetterStardewValley_SpiritFish",year);
        if(ledger.TryGetValue(key,out var old))return old==token;
        if(!AnnualLegendaryFeature.MachineAvailable(owner,"(O)Eddie.BetterStardewValley_SpiritFish",year))return false;
        ledger.Add(key,token);Game1.MasterPlayer.modData[LedgerKey]=JsonSerializer.Serialize(ledger);return true;
    }
    internal static void ReleaseManual(Farmer owner,int year,string token,bool caught)
    {
        var ledger=Read();string key=Key(owner.UniqueMultiplayerID,"(O)Eddie.BetterStardewValley_SpiritFish",year);
        if(!ledger.TryGetValue(key,out var existing)||existing!=token)return;
        if(caught)AnnualLegendaryFeature.MarkMachineCatch(owner,"(O)Eddie.BetterStardewValley_SpiritFish",year);
        ledger.Remove(key);Game1.MasterPlayer.modData[LedgerKey]=JsonSerializer.Serialize(ledger);
    }
    internal static void Start(Farmer owner,ProductionState state,Action commit)
    {
        state.OwnerId=owner.UniqueMultiplayerID;
        if(state.LegendaryPlans.Count==0){commit();return;}
        state.OwnerId=owner.UniqueMultiplayerID;state.LegendaryYear=CompletionYear(state.DueAt);state.CycleToken=Guid.NewGuid().ToString("N");
        var ledger=Read();
        foreach(var plan in state.LegendaryPlans)
        {
            if(!Available(owner,plan.Id,state.LegendaryYear))throw new InvalidOperationException("Legendary quota changed before production.");
            ledger.Add(Key(state.OwnerId,plan.Id,state.LegendaryYear),state.CycleToken);
        }
        bool had=Game1.MasterPlayer.modData.TryGetValue(LedgerKey,out var previous);
        try {Game1.MasterPlayer.modData[LedgerKey]=JsonSerializer.Serialize(ledger);commit();}
        catch {Restore(Game1.MasterPlayer,LedgerKey,had?previous:null);throw;}
    }
    internal static bool CanFinish(ProductionState state)
    {
        if(state.LegendaryPlans.Count==0)return true;
        // Give any pre-hooked manual fish time to settle its annual record first.
        var owner=Game1.getOnlineFarmers().FirstOrDefault(p=>p.UniqueMultiplayerID==state.OwnerId);
        return owner?.CurrentTool is not FishingRod rod || !(rod.isFishing || rod.isReeling || rod.pullingOutOfWater || rod.hit || rod.fishCaught);
    }
    internal static void Finish(ProductionState state,Action commit)
    {
        if(state.LegendaryPlans.Count==0){commit();return;}
        var owner=Game1.GetPlayer(state.OwnerId,onlyOnline:false) ?? throw new InvalidOperationException("Machine owner unavailable.");
        var ledger=Read();
        var records=new Dictionary<string,string?>();
        bool had=Game1.MasterPlayer.modData.TryGetValue(LedgerKey,out var previous);
        try
        {
            foreach(var plan in state.LegendaryPlans)
            {
                string recordKey=AnnualLegendaryRules.KeyPrefix+AnnualLegendaryRules.NormalizeId(plan.Id);
                records[recordKey]=owner.modData.TryGetValue(recordKey,out var old)?old:null;
                string key=Key(state.OwnerId,plan.Id,state.LegendaryYear);
                // A manually hooked fish may finish before the reservation reaches that client.
                // Convert the fixed batch to its pre-rolled ordinary fallback instead of duplicating a king.
                bool owns=ledger.TryGetValue(key,out var token)&&token==state.CycleToken;
                // Extending a paid legacy cycle must not move its existing reservation into a new year.
                int catchYear=state.LegacyPrecomputed?state.LegendaryYear:plan.Id==Production.SpiritId?Game1.year:state.LegendaryYear;
                bool currentFree=catchYear==state.LegendaryYear||!Reserved(state.OwnerId,plan.Id,catchYear);
                // Pre-timeline Spirit plans have no per-consumed-bait/historical-window proof.
                if(plan.Id!=Production.SpiritId && owns && currentFree && AnnualLegendaryFeature.MachineAvailable(owner,plan.Id,catchYear,committed:true))
                    AnnualLegendaryFeature.MarkMachineCatch(owner,plan.Id,catchYear);
                else
                {
                    var existing=state.Pending.Single(p=>p.Id==plan.Id && p.Quality==plan.Quality);
                    state.Pending.Remove(existing);
                    if(existing.Count>plan.Count)state.Pending.Add(existing with {Count=existing.Count-plan.Count});
                    state.Pending.Add(new(plan.FallbackId,plan.Count,plan.Quality));
                }
                if(owns)ledger.Remove(key);
            }
            state.LegendaryPlans.Clear();
            Game1.MasterPlayer.modData[LedgerKey]=JsonSerializer.Serialize(ledger);
            commit();
        }
        catch
        {
            foreach(var record in records)Restore(owner,record.Key,record.Value);
            Restore(Game1.MasterPlayer,LedgerKey,had?previous:null);throw;
        }
    }
    private static void Restore(Farmer player,string key,string? value)
    {if(value==null)player.modData.Remove(key);else player.modData[key]=value;}
}

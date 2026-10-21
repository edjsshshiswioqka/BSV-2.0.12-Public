using System.Text.Json;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewModdingAPI;
namespace BetterStardewValley.MutantLobsterKing;

internal static class MutantWildQuota
{
    internal const string Key="Eddie.BetterStardewValley/MutantLobsterKing/AnnualLedger/v1";
    internal const string ClaimKey="Eddie.BetterStardewValley/MutantLobsterKing/WildClaim";
    private static readonly object Gate=new();
    internal static void Initialize(bool provenNewSave,IMonitor monitor)
    {
        if(!Game1.IsMasterGame||Game1.getLocationFromName("Farm") is not Farm farm)return;
        lock(Gate)
        {
            if(farm.modData.ContainsKey(Key)){Read();return;}
            var value=new MutantAnnualLedger();
            if(!provenNewSave)
            {
                value.MigrationLockedThroughYear=Game1.year;
                value.MigrationReason="existing-save-without-trusted-capture-history";
                monitor.Log("Mutant Lobster King: missing annual history. Existing items are preserved; new wild captures are conservatively locked through year "+Game1.year+". Next year opens normally. This is a migration lock, not a recorded catch.",LogLevel.Warn);
            }
            Save(value);
        }
    }
    internal static void Reconcile()
    {
        if(!Game1.IsMasterGame)return;
        lock(Gate)
        {
            var ledger=Read();bool changed=false;
            Utility.ForEachItem(item=>
            {
                if(item is not StardewValley.Object obj||!obj.modData.TryGetValue(BetterStardewValley.LobsterKing.KingWildQuota.SourceKey,out var id))return true;
                var tokens=new HashSet<string>();
                if(obj.heldObject.Value?.modData.TryGetValue(ClaimKey,out var held)==true)tokens.Add(held);
                if(obj is StardewValley.Objects.Chest c)
                {
                    if(c.modData.TryGetValue(Eddie.FishingMachine.ProductionService.StateKey,out var raw))
                        foreach(var p in ProductionState.Load(raw).Pending.Where(p=>MutantLobsterRules.IsItem(p.Id)&&p.Count==1&&p.WildClaim!=null))tokens.Add(p.WildClaim!);
                    foreach(var stored in c.Items)if(stored!=null&&stored.modData.TryGetValue(ClaimKey,out var t))tokens.Add(t);
                }
                string prefix=(obj is StardewValley.Objects.CrabPot?"pot:":"machine:")+id;
                foreach(var claim in ledger.Claims.Where(c=>!c.Committed&&(c.Source==prefix||c.Source.StartsWith(prefix+":",StringComparison.Ordinal))))
                    if(tokens.Contains(claim.Token)){claim.Committed=true;changed=true;}
                return true;
            });
            if(changed)Save(ledger);
        }
    }
    internal static MutantAnnualLedger Read()
    {
        if(!Game1.getFarm().modData.TryGetValue(Key,out var raw))throw new InvalidOperationException("Mutant annual ledger not initialized.");
        var value=JsonSerializer.Deserialize<MutantAnnualLedger>(raw)??throw new InvalidOperationException("Null Mutant annual ledger.");
        value.Validate();return value;
    }
    private static void Save(MutantAnnualLedger value){value.Validate();Game1.getFarm().modData[Key]=JsonSerializer.Serialize(value);}
    internal static bool AvailableAt(int day)
    {
        if(!MutantService.Ready||!Game1.IsMasterGame||day<0||day>Game1.Date.TotalDays||Game1.getLocationFromName("Farm") is not Farm)return false;
        if(!Game1.getFarm().modData.ContainsKey(Key))return false;
        lock(Gate)return Read().Available(MachineTimeline.Year(day));
    }
    internal static bool CommitAt(int day,string source,Action<string> apply,Action rollback)
    {
        if(!Game1.IsMasterGame||day<0||day>Game1.Date.TotalDays)return false;
        lock(Gate)return Read().Commit(day,source,Save,apply,rollback);
    }
}

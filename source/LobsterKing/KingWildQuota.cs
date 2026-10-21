using System.Text.Json;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewModdingAPI;
namespace BetterStardewValley.LobsterKing;

// All writers run on the authoritative game thread. The gate also serializes reentrant
// machine/pot callbacks. Prepared records fail closed until their source is reconciled.
internal static class KingWildQuota
{
    internal const string Key="Eddie.BetterStardewValley/LobsterKing/WildLedger";
    internal const string SourceKey="Eddie.BetterStardewValley/LobsterKing/Source";
    private static readonly object Gate=new();
    internal static KingLedger Read()
    {
        var farm=Game1.getFarm();
        var value=farm.modData.TryGetValue(Key,out var json)?JsonSerializer.Deserialize<KingLedger>(json)??throw new InvalidOperationException("Null King ledger."):new();
        value.Validate();return value;
    }
    private static void Save(KingLedger value){value.Validate();Game1.getFarm().modData[Key]=JsonSerializer.Serialize(value);}
    internal static bool Available()
        => AvailableAt(Game1.Date.TotalDays);
    internal static bool AvailableAt(int day)
    {
        if(!KingService.Ready||!Game1.IsMasterGame||day<0||day>Game1.Date.TotalDays||MachineTimeline.Season(day)==3)return false;
        // Test/early-load callers without a farm cannot create a farm-scoped claim.
        if(Game1.getLocationFromName("Farm") is not Farm)return false;
        try{lock(Gate)return Read().Available(MachineTimeline.Year(day),MachineTimeline.Season(day));}
        catch(Exception ex){KingService.Error("quota",ex);return false;}
    }
    internal static string Source(StardewValley.Object source,string kind)
    {
        if(!source.modData.TryGetValue(SourceKey,out var id))source.modData[SourceKey]=id=Guid.NewGuid().ToString("N");
        if(!Guid.TryParseExact(id,"N",out _))throw new InvalidOperationException("Invalid King source identity; preserved.");
        return kind+":"+id;
    }
    internal static bool Commit(string source,Action<string> apply,Action rollback)
        => CommitAt(Game1.Date.TotalDays,source,apply,rollback);
    internal static bool CommitAt(int day,string source,Action<string> apply,Action rollback)
    {
        if(!Game1.IsMasterGame||day<0||day>Game1.Date.TotalDays)return false;
        lock(Gate)
        {
            var farm=Game1.getFarm();if(farm==null)return false;
            return KingTransactions.Commit(Read(),MachineTimeline.Year(day),MachineTimeline.Season(day),day,source,Save,apply,rollback);
        }
    }
    internal static bool Acquire(Farmer who,string token)
    {
        if(!Game1.IsMasterGame)return false;
        lock(Gate)
        {
            var item=who.Items.FirstOrDefault(i=>i!=null&&i.Stack==1&&LobsterKingRules.IsFish(i.QualifiedItemId)
                &&i.modData.TryGetValue(LobsterKingRules.ClaimKey,out var t)&&t==token);
            if(item==null)return false; // actual synchronized backpack presence, not a client assertion
            var ledger=Read();var claim=ledger.Claims.SingleOrDefault(c=>c.Token==token);
            if(claim?.Committed!=true)return false;
            bool changed=ledger.Acquire(token,who.UniqueMultiplayerID,Game1.Date.TotalDays);
            if(changed)Save(ledger); // durable recipient first; retry only removes the spent marker
            if(claim.Recipient!=0)item.modData.Remove(LobsterKingRules.ClaimKey);
            return changed;
        }
    }
    internal static void ReconcileSource(StardewValley.Object source,string kind,IEnumerable<string> tokens)
    {
        if(!Game1.IsMasterGame||!source.modData.TryGetValue(SourceKey,out var id))return;
        lock(Gate)
        {
            var ledger=Read();bool changed=false;
            foreach(var claim in ledger.Claims.Where(c=>!c.Committed&&(c.Source==kind+":"+id||c.Source.StartsWith(kind+":"+id+":",StringComparison.Ordinal))))
                if(tokens.Contains(claim.Token)){claim.Committed=true;changed=true;}
            // Missing source evidence is retained, never guessed away or rerolled.
            if(changed)Save(ledger);
        }
    }
}

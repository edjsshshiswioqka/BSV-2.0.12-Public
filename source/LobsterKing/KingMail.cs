using Eddie.FishingMachine.Core;
using StardewValley;
namespace BetterStardewValley.LobsterKing;

internal static class KingMail
{
    internal const string Id="Eddie.BetterStardewValley_LobsterKingWilly";
    internal static string Letter(Func<string,string> tr)=>tr("king.mail.body")
        +" %item cookingRecipe "+LobsterKingRules.SashimiId+" %%[#]"+tr("king.mail.title");
    internal static void Data(IDictionary<string,string> data,Func<string,string> tr,bool enabled)
    {if(enabled)data[Id]=Letter(tr);}
    // The farm ledger is host-authored/synchronized. Only the owning local player writes
    // their native mailbox; host, farmhands and split screens don't write each other's mail.
    internal static bool Deliver(Farmer who,KingLedger ledger,int day,bool enabled)
    {
        if(!enabled||!who.IsLocalPlayer)return false;
        ledger.Validate();
        if(!ledger.FirstAcquired.TryGetValue(who.UniqueMultiplayerID,out int acquired)||day<=acquired
            ||!ledger.Claims.Any(c=>c.Committed&&c.Recipient==who.UniqueMultiplayerID)
            ||who.cookingRecipes.ContainsKey(LobsterKingRules.SashimiId)
            ||who.mailReceived.Contains(Id)||who.mailbox.Contains(Id)||who.mailForTomorrow.Contains(Id))return false;
        who.mailbox.Add(Id); // native read-time cookingRecipe command grants the recipe, not delivery
        return true;
    }
}

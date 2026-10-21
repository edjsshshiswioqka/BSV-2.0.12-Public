// Explicit boundary for inventory-only fake-game suites. KingNativeChecks exercises
// the real farm-backed quota; these older suites have no Farm or world calendar.
namespace BetterStardewValley.LobsterKing {
internal static class KingWildQuota
{
    internal static bool Available()=>false;
    internal static int AvailableReads;
    internal static bool AvailableAt(int day){AvailableReads++;return false;}
    internal static string Source(StardewValley.Object source,string kind)=>kind;
    internal static bool Commit(string source,System.Action<string> apply,System.Action rollback)=>false;
    internal static bool CommitAt(int day,string source,System.Action<string> apply,System.Action rollback)=>false;
}

}
namespace BetterStardewValley.KingCrab { internal static class KingCrabService { internal static bool Ready=>false; } }
// These isolation suites don't supply native maps. Sewer eligibility is tested against
// the actual game types by MutantChecks; don't fake production qualification here.
namespace BetterStardewValley.MutantLobsterKing { internal static class MutantService { internal static bool Eligible(StardewValley.GameLocation location,Microsoft.Xna.Framework.Vector2 tile)=>false; } }

namespace BetterStardewValley.MutantLobsterKing {
internal static class MutantWildQuota {
 internal const string ClaimKey="Eddie.BetterStardewValley/MutantLobsterKing/WildClaim";
 internal static Eddie.FishingMachine.Core.MutantAnnualLedger? FixtureLedger;
 internal static bool AvailableAt(int day)=>FixtureLedger?.Available(Eddie.FishingMachine.Core.MachineTimeline.Year(day))==true;
 internal static bool CommitAt(int day,string source,System.Action<string> apply,System.Action rollback)
  =>FixtureLedger!=null&&FixtureLedger.Commit(day,source,_=>{},apply,rollback);
}}
namespace BetterStardewValley.SpiritFish {internal static class SpiritRules {internal static bool Eligible(int level,int time,string location)=>level>=10&&time>=1800&&time<2600&&location=="Mountain";}}

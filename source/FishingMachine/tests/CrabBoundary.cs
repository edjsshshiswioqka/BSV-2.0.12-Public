// Extra native boundary members shared by the focused production/cave harnesses.
// Full crab behavior is exercised by ProductionChecks using the same production code.
using Eddie.FishingMachine.Core;
namespace StardewValley
{
 public partial class Object {public const int baitCategory=-21;public int Category=>QualifiedItemId is "(O)685" or "(O)DeluxeBait"?-21:0;}
 public partial class Farmer {public Net<int> fishingLevel=new(10);public List<int> professions=new();public Stats stats=new();}
 public sealed class Stats {public uint Get(string key)=>0;}
 public static class DataLoader {public static Dictionary<string,string> Fish(object content)=>new(){["717"]="Synthetic crab/trap/1/0/ocean"};}
 public partial class GameLocation {public IList<string> GetCrabPotFishForTile(Microsoft.Xna.Framework.Vector2 tile)=>new List<string>{"ocean"};}
 public static partial class Game1 {public static object content=new();public static void exitActiveMenu(){activeClickableMenu=null;}}
}
namespace Eddie.FishingMachine
{
 internal sealed class TestRegistry{public bool IsLoaded(string id)=>false;}
 internal sealed class TestHelper{public TestRegistry ModRegistry=new();}
 internal sealed class CrabSettings : Eddie.FishingMachine.Core.CrabBalanceConfig {public bool DebugMode;}
 internal sealed class NetworkActions{public void Control(StardewValley.Objects.Chest chest,ActionKind kind){}}
 internal partial class ModEntry
 {
  public TestHelper Helper=new();
  public bool Ready=true;public CrabSettings CrabConfig=new();public NetworkActions Actions=new();
  public void ReleaseChest(bool startCrab=true){}
  public static bool IsCrab(StardewValley.Item? item)=>item?.QualifiedItemId=="(BC)"+MachineKinds.CrabId;
  public static bool IsMachine(StardewValley.Item? item)=>IsCrab(item)||item?.QualifiedItemId=="(BC)"+MachineKinds.FishingId;
 }
}
namespace BetterStardewValley
{
 internal static class IntegrationHost {public static bool CanMutate=>StardewModdingAPI.Context.IsWorldReady&&StardewModdingAPI.Context.IsMainPlayer;}
}
namespace BetterStardewValley.Configuration
{
 internal static class SharedPreferences {internal static readonly Settings Current=new();internal sealed class Settings{internal bool AutoRestartProduction=true;}}
}

using StardewModdingAPI;
namespace BetterStardewValley
{
 // Only unrelated configuration/integration boundaries are substituted. All AFK source
 // files, native inventory methods and native Perfect quality/XP branches are exercised.
 internal sealed class ModConfig
 {
  public SButton CallHorseKey {get;set;}=SButton.H;
  public SButton Ui2HorseCallKey {get;set;}=SButton.J;
  public UiOptions UI {get;set;}=new();
  public BetterStardewValley.AfkFishing.AfkOptions AfkFishing {get;set;}=new();
 }
 internal sealed class UiOptions {public SButton CalendarKey {get;set;}=SButton.None;}
}
namespace Eddie.FishingMachine
{
 internal static class ModEntry { internal static bool IsAnchor(StardewValley.Object obj)=>false; }
}

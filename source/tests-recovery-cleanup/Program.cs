using BetterStardewValley;
using Eddie.FishingMachine.Core;
using Newtonsoft.Json;
using StardewModdingAPI;
using System.Reflection;
using System.Text.Json;
int checks=0;var labels=new List<string>();
void Check(bool b,string label){if(!b)throw new Exception(label);checks++;labels.Add(label);}
var a=typeof(ModConfig).Assembly;var names=a.GetTypes().Select(t=>t.FullName!).ToArray();
foreach(string retired in new[]{"BetterStardewValley.NativeAutomation.","BetterStardewValley.InformationUI.","BetterStardewValley.Ui2","BetterStardewValley.Automation."})
 Check(!names.Any(n=>n.StartsWith(retired,StringComparison.Ordinal)),"no retired production type prefix: "+retired);
foreach(string live in new[]{"BetterStardewValley.Transactions.AtomicChange","BetterStardewValley.Totems.ProcessingPolicy","BetterStardewValley.AfkFishing.AfkFishingService","BetterStardewValley.Orders.OrderMarket","Eddie.FishingMachine.ProductionService"})
 Check(names.Contains(live),"independent feature/shared infrastructure retained: "+live);
Check(!a.GetReferencedAssemblies().Any(n=>n.Name!.Contains("Automate")||n.Name!.Contains("UiInfo")),"main assembly requires no external Automate/UI2 assembly");
foreach(string field in new[]{"Automation","UI","EnableUIInfoSuite2Compatibility","Ui2HorseCallKey","EnableAutomateCompatibility","AutomateMachineUpgrades"})
 Check(typeof(ModConfig).GetProperty(field)==null,"retired configuration field removed: "+field);
var c=JsonConvert.DeserializeObject<ModConfig>("{\"Automation\":{\"Enabled\":true},\"UI\":{\"Enabled\":true},\"Ui2HorseCallKey\":\"J\",\"EnableAutomateCompatibility\":true,\"CallHorseKey\":\"J\",\"AfkFishing\":{\"ReturnToFishingSpotNextDay\":false}}")!;
a.GetType("BetterStardewValley.Configuration.ConfigPolicy",true)!.GetMethod("Normalize",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{c});
Check(c.CallHorseKey==SButton.J&&!c.AfkFishing.ReturnToFishingSpotNextDay,"actual Newtonsoft old config ignores retired fields and preserves explicit J/AFK option");
Check(new ModConfig().CallHorseKey==SButton.H,"new default is H without external mod probe");
Check(new CrabBalanceConfig().CycleGameMinutes==MachineCycleRules.DefaultMinutes&&MachineCycleRules.DefaultMinutes==2880,"crab new default remains two game days");
var crab=JsonConvert.DeserializeObject<CrabBalanceConfig>("{\"CycleGameMinutes\":4320,\"CycleRulesVersion\":1,\"LevelOneCriticalChance\":0.23}")!;
crab.Validate();Check(!crab.MigrateCycle()&&crab.CycleGameMinutes==4320&&crab.LevelOneCriticalChance==.23,"existing migrated custom crab duration and chance preserved");
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks,labels,layer="actual compiled BSV/Newtonsoft/SMAPI configuration and type-load checks",live_game=false,multiplayer=false},new JsonSerializerOptions{WriteIndented=true}));

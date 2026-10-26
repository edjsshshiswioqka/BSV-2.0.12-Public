using Eddie.FishingMachine.Core;
using Newtonsoft.Json.Linq;

namespace BetterStardewValley.Configuration;

// Pure JSON migration of fishing-machine.json / crab-catcher.json (no game or SMAPI types), shared with tests.
internal static partial class MachineBalanceMigration
{
    internal const int Version = 1;
    internal static JObject Prepare(JObject original, bool crab)
    {
        var next = (JObject)original.DeepClone();
        // BSV 1.0.x shipped a one-day cycle (1440 min) and 350 bait as its untouched defaults. Those players never
        // chose a custom length, so they move to the 2.0 per-level defaults like everyone else; a changed value stays.
        bool untouched10 = !crab && next["FishingBalanceVersion"] == null && next["LevelDurationVersion"] == null
            && next.Value<bool?>("UniformCycleWasCustomized") != true
            && next.Value<int?>("CycleGameMinutes") == 1440 && next.Value<int?>("BaitPerCycle") == 350;
        if(!crab && (next.Value<int?>("FishingBalanceVersion")??0)<1)
        {
            next["FishingBalanceVersion"]=1;
            next["BaitPerCycle"]=200;next["LevelTwoBaitPerCycle"]=300;next["LevelThreeBaitPerCycle"]=400;
            next["FishCriticalChance"]=.45;next["LevelTwoCriticalChance"]=.55;next["LevelTwoTripleChance"]=.10;
            next["LevelThreeCriticalChance"]=.66;next["LevelThreeTripleChance"]=.24;next["RulesVersion"]=1;
        }
        // BSV 2.0.14: new cycles use the bait rules of balance version 2 (wild, magnet, magic bait; per-level challenge/targeted bait).
        if(!crab && next.Value<int?>("FishingBalanceVersion")==1) next["FishingBalanceVersion"]=2;
        if ((next.Value<int?>("LevelDurationVersion") ?? 0) >= Version) return next;
        int original10 = next.Value<int?>("CycleGameMinutes") ?? 2880;
        int uniform = untouched10 ? MachineCycleRules.DefaultMinutes : original10;
        if (uniform is < 10 or > 40320) throw new InvalidDataException("Invalid legacy machine duration; original config preserved.");
        bool custom = next.Value<bool?>("UniformCycleWasCustomized") == true || uniform != 2880;
        next["CycleGameMinutes"] = uniform;
        if (next.GetValue("LevelTwoCycleGameMinutes", StringComparison.OrdinalIgnoreCase) == null)
            next["LevelTwoCycleGameMinutes"] = custom ? uniform : crab ? 5760 : 4320;
        if (!crab && next.GetValue("LevelThreeCycleGameMinutes", StringComparison.OrdinalIgnoreCase) == null)
            next["LevelThreeCycleGameMinutes"] = custom ? uniform : 5760;
        next["LevelDurationVersion"] = Version;
        next["PreviousCycleGameMinutes"] = original10;
        next["CycleRulesVersion"] = MachineCycleRules.Version;
        return next;
    }
}

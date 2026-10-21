using Eddie.FishingMachine.Core;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace BetterStardewValley.Configuration;

// Only named settings are patched. Unknown fields and explicit per-level settings survive.
internal static class MachineBalanceMigration
{
    internal const int Version = 1;
    internal static JObject Prepare(JObject original, bool crab)
    {
        var next = (JObject)original.DeepClone();
        if(!crab && (next.Value<int?>("FishingBalanceVersion")??0)<1)
        {
            next["FishingBalanceVersion"]=1;
            next["BaitPerCycle"]=200;next["LevelTwoBaitPerCycle"]=300;next["LevelThreeBaitPerCycle"]=400;
            next["FishCriticalChance"]=.45;next["LevelTwoCriticalChance"]=.55;next["LevelTwoTripleChance"]=.10;
            next["LevelThreeCriticalChance"]=.66;next["LevelThreeTripleChance"]=.24;next["RulesVersion"]=1;
        }
        if ((next.Value<int?>("LevelDurationVersion") ?? 0) >= Version) return next;
        int uniform = next.Value<int?>("CycleGameMinutes") ?? 2880;
        if (uniform is < 10 or > 40320) throw new InvalidDataException("Invalid legacy machine duration; original config preserved.");
        bool custom = next.Value<bool?>("UniformCycleWasCustomized") == true || uniform != 2880;
        next["CycleGameMinutes"] = uniform;
        if (next.GetValue("LevelTwoCycleGameMinutes", StringComparison.OrdinalIgnoreCase) == null)
            next["LevelTwoCycleGameMinutes"] = custom ? uniform : crab ? 5760 : 4320;
        if (!crab && next.GetValue("LevelThreeCycleGameMinutes", StringComparison.OrdinalIgnoreCase) == null)
            next["LevelThreeCycleGameMinutes"] = custom ? uniform : 5760;
        next["LevelDurationVersion"] = Version;
        next["PreviousCycleGameMinutes"] = uniform;
        next["CycleRulesVersion"] = MachineCycleRules.Version;
        return next;
    }
    internal static T Load<T>(IModHelper helper, IMonitor monitor, string name, bool crab, Action<T> validate) where T : new()
    {
        string path = Path.Combine(helper.DirectoryPath, name);
        bool exists = File.Exists(path);
        var raw = exists ? JObject.Parse(File.ReadAllText(path)) : JObject.FromObject(new T());
        var next = Prepare(raw, crab);
        var value = next.ToObject<T>() ?? throw new InvalidDataException("Empty machine configuration.");
        var beforeValidation=JObject.FromObject(value!);
        validate(value); // Validation precedes backup/write; invalid input remains intact.
        // Preserve pre-existing migrations without replacing unrelated or unknown JSON fields.
        foreach(var property in JObject.FromObject(value!).Properties())
            if(!JToken.DeepEquals(beforeValidation[property.Name],property.Value))next[property.Name]=property.Value;
        if (!JToken.DeepEquals(raw, next) || !exists)
        {
            if (exists)
            {
                string backup = path + ".pre-r15.bak";
                if (!File.Exists(backup)) File.Copy(path, backup, false);
            }
            ConfigFiles.Commit(helper.DirectoryPath, new Dictionary<string, object> { [name] = next });
            monitor.Log("BSV R15 machine configuration migrated once; backup retained. Changed fields: "
                + string.Join(", ", next.Properties().Where(p => !JToken.DeepEquals(raw[p.Name], p.Value)).Select(p => p.Name))
                + ". Review per-level durations; active paid cycles retain their stored deadlines.", LogLevel.Info);
        }
        return value;
    }
}

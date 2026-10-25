using Eddie.FishingMachine.Core;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace BetterStardewValley.Configuration;

// Only named settings are patched. Unknown fields and explicit per-level settings survive.
internal static partial class MachineBalanceMigration
{
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
            if (!exists) monitor.Log($"BSV created {name} with the default settings.", LogLevel.Trace);
            else monitor.Log("BSV R15 machine configuration migrated once; backup retained. Changed fields: "
                + string.Join(", ", next.Properties().Where(p => !JToken.DeepEquals(raw[p.Name], p.Value)).Select(p => p.Name))
                + ". Review per-level durations; active paid cycles retain their stored deadlines.", LogLevel.Info);
        }
        return value;
    }
}

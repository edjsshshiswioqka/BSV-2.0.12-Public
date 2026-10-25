// BSV 1.0.3 -> 2.0 upgrade bridge. Offline check, no game running.
// The real BSV 1.0.3 DLL (the player's own Nexus file, kept outside the repository in Documents\BSV-test\bsv103)
// creates machine data with its own code; BSV 2.0 must read every one of those records without losing anything.
// The 1.0.3 config files shipped in that package are run through 2.0's config migration as well.
using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using BetterStardewValley.Configuration;
using Eddie.FishingMachine.Core;
using Newtonsoft.Json.Linq;

int passed = 0, failed = 0;
void Check(bool ok, string name) { if (ok) { passed++; Console.WriteLine("PASS: " + name); } else { failed++; Console.WriteLine("FAIL: " + name); } }

string dir = Environment.GetEnvironmentVariable("BSV_REAL_MAPS") ?? (args.Length > 0 ? args[0] : "");
string oldDir = Path.Combine(dir, "bsv103");
string dll = Path.Combine(oldDir, "BetterStardewValley.dll");
if (!File.Exists(dll)) { Console.WriteLine("SKIP: BSV 1.0.3 not found in " + oldDir); return 0; }

// ---------------------------------------------------------------- 1. config files
{
    var cfg = JObject.Parse(File.ReadAllText(Path.Combine(oldDir, "fishing-machine.json")));
    var next = MachineBalanceMigration.Prepare(cfg, crab: false);
    var c = next.ToObject<BalanceConfig>()!;
    c.Validate();
    Check(c.MinutesForLevel(1) == 2880 && c.MinutesForLevel(2) == 4320 && c.MinutesForLevel(3) == 5760,
        $"untouched 1.0.3 fishing-machine.json gets the 2.0 cycle lengths (got {c.MinutesForLevel(1)}/{c.MinutesForLevel(2)}/{c.MinutesForLevel(3)})");
    Check(c.BaitPerCycle == 200 && c.LevelTwoBaitPerCycle == 300 && c.LevelThreeBaitPerCycle == 400, "1.0.3 bait cost becomes 200/300/400");
    var custom = (JObject)cfg.DeepClone(); custom["CycleGameMinutes"] = 720;
    var cc = MachineBalanceMigration.Prepare(custom, false).ToObject<BalanceConfig>()!;
    Check(cc.MinutesForLevel(1) == 720 && cc.MinutesForLevel(3) == 720, "a 1.0.3 player's own custom cycle length is kept");
    var crab = MachineBalanceMigration.Prepare(JObject.Parse(File.ReadAllText(Path.Combine(oldDir, "crab-catcher.json"))), crab: true).ToObject<CrabBalanceConfig>()!;
    crab.Validate();
    Check(true, "1.0.3 crab-catcher.json migrates and validates");
}

// ---------------------------------------------------------------- 2. machine records written by 1.0.3 itself
var alc = new AssemblyLoadContext("bsv103", isCollectible: false);
alc.Resolving += (_, name) =>
{
    foreach (string root in new[] { oldDir, Environment.GetEnvironmentVariable("BSV_GAME_PATH") ?? "" })
        foreach (string candidate in new[] { Path.Combine(root, name.Name + ".dll"), Path.Combine(root, "smapi-internal", name.Name + ".dll") })
            if (root.Length > 0 && File.Exists(candidate)) return alc.LoadFromAssemblyPath(candidate);
    return null;
};
var old = alc.LoadFromAssemblyPath(dll);
Type T(string name) => old.GetType("Eddie.FishingMachine.Core." + name, true)!;
Type oldState = T("ProductionState");
MethodInfo oldSave = oldState.GetMethod("Save", BindingFlags.Public | BindingFlags.Instance)!;
MethodInfo oldLoad = oldState.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!;

object? Make(Type t, string name, Dictionary<string, object?> given)
{
    if (given.TryGetValue(name, out var v)) return v;
    if (t == typeof(string)) return "(O)145";
    if (t == typeof(int)) return name.Contains("ount", StringComparison.OrdinalIgnoreCase) ? 1 : name.Contains("uality") ? 0 : 1;
    if (t == typeof(long)) return 0L;
    if (t == typeof(double)) return 1.0;
    if (t == typeof(bool)) return false;
    if (t.IsEnum) return Enum.GetValues(t).GetValue(0);
    if (Nullable.GetUnderlyingType(t) != null || t == typeof(object)) return null;
    if (t.IsGenericType)
    {
        var def = t.GetGenericTypeDefinition(); var arg = t.GetGenericArguments()[0];
        if (def == typeof(ISet<>) || def == typeof(HashSet<>)) { var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(arg))!; set.GetType().GetMethod("Add")!.Invoke(set, new[] { Make(arg, name, given) }); return set; }
        if (def == typeof(IReadOnlyList<>) || def == typeof(IEnumerable<>) || def == typeof(IList<>) || def == typeof(List<>) || def == typeof(IReadOnlyCollection<>))
        { var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(arg))!; list.Add(Make(arg, name, given)); return list; }
    }
    var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
    if (ctor == null) return Activator.CreateInstance(t);
    return ctor.Invoke(ctor.GetParameters().Select(p => Make(p.ParameterType, p.Name!, given)).ToArray());
}
object? Call(MethodInfo m, Dictionary<string, object?> given) => m.Invoke(null, m.GetParameters().Select(p =>
    given.ContainsKey(p.Name!) ? given[p.Name!] : p.IsOptional ? p.DefaultValue is DBNull ? Type.Missing : p.DefaultValue : Make(p.ParameterType, p.Name!, given)).ToArray());
object NewOld(string kind, int level, long cycles)
{
    var s = Activator.CreateInstance(oldState)!;
    oldState.GetProperty("Kind")!.SetValue(s, kind);
    oldState.GetProperty("Level")!.SetValue(s, level);
    oldState.GetProperty("CompletedCycles")!.SetValue(s, cycles);
    oldState.GetProperty("LevelCycles")!.SetValue(s, Math.Min(cycles, 3L));
    oldState.GetProperty("OwnerId")!.SetValue(s, 1L);
    return s;
}
var records = new List<(string Name, string Json)>();
void Add(string name, object state)
{
    string json = (string)oldSave.Invoke(state, null)!;
    try { oldLoad.Invoke(null, new object[] { json }); records.Add((name, json)); }
    catch (TargetInvocationException ex) { Console.WriteLine($"NOTE: 1.0.3 itself rejects its '{name}' sample ({ex.InnerException?.Message}); not a bridge case."); }
}
foreach (int lv in new[] { 1, 2, 3 }) Add($"fishing idle Lv{lv}", NewOld(MachineKinds.Fishing, lv, lv * 4));
foreach (int lv in new[] { 1, 2 }) Add($"crab idle Lv{lv}", NewOld(MachineKinds.Crab, lv, lv * 4));

// Working cycles started by 1.0.3's own production code.
try
{
    var config = Activator.CreateInstance(T("BalanceConfig"))!;
    foreach (int lv in new[] { 1, 2, 3 })
    {
        var previous = NewOld(MachineKinds.Fishing, lv, 5);
        int bait = (int)T("MachineUpgrade").GetMethod("RequiredBait")!.Invoke(null, new[] { previous, config })!;
        var spendType = T("BaitSpend"); var spends = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(spendType))!;
        spends.Add(Make(spendType, "bait", new() { ["Count"] = bait, ["Index"] = 0, ["Target"] = null }));
        var choiceType = T("FishChoice"); var fish = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(choiceType))!;
        foreach (string id in new[] { "(O)145", "(O)142", "(O)131" }) fish.Add(Make(choiceType, "fish", new() { ["Id"] = id, ["Weight"] = 1.0, ["IsLegendary"] = false, ["BasePrice"] = 50 }));
        var begin = T("Production").GetMethod("Begin")!;
        var started = Call(begin, new() { ["config"] = config, ["bait"] = spends, ["fish"] = fish, ["other"] = new List<string> { "(O)168", "(O)153" }, ["start"] = 1000L, ["seed"] = 20261025 + lv, ["previous"] = previous })!;
        Add($"fishing working Lv{lv} (started by 1.0.3)", started);
    }
}
catch (Exception ex) { Check(false, "1.0.3 fishing cycle sample: " + (ex.InnerException ?? ex).Message); }
try
{
    var rules = T("CrabRules");
    foreach (int lv in new[] { 1, 2 })
    {
        var previous = NewOld(MachineKinds.Crab, lv, 5);
        int need = (int)rules.GetMethod("Required")!.Invoke(null, new object[] { lv, false })!;
        var baitType = T("CrabBait"); var bait = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(baitType))!;
        bait.Add(Make(baitType, "bait", new() { ["Index"] = 0, ["Id"] = "(O)685", ["Count"] = need, ["Deluxe"] = false, ["Quality"] = 0, ["Metadata"] = "" }));
        var aquaticType = T("CrabAquatic"); var pool = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(aquaticType))!;
        foreach (string id in new[] { "(O)715", "(O)716", "(O)717", "(O)722" }) pool.Add(Make(aquaticType, "pool", new() { ["Id"] = id, ["Weight"] = 1.0 }));
        var started = Call(rules.GetMethod("Begin")!, new() { ["previous"] = previous, ["bait"] = bait, ["pool"] = pool, ["owner"] = 1L, ["luremaster"] = false, ["book"] = false, ["start"] = 1000L, ["seed"] = 77 + lv })!;
        Add($"crab working Lv{lv} (started by 1.0.3)", started);
    }
}
catch (Exception ex) { Check(false, "1.0.3 crab cycle sample: " + (ex.InnerException ?? ex).Message); }

foreach (var (name, json) in records)
{
    try
    {
        var doc = JObject.Parse(json);
        var s = ProductionState.Load(json);
        bool same = s.Kind == (string?)doc["Kind"] && s.Level == (int?)doc["Level"] && s.CompletedCycles == (long?)doc["CompletedCycles"]
            && s.Phase == (string?)doc["Phase"] && s.DueAt == ((long?)doc["DueAt"] ?? 0) && s.FishCount == ((int?)doc["FishCount"] ?? 0);
        Check(same, $"2.0 reads 1.0.3 '{name}' unchanged (level {s.Level}, phase {s.Phase}, cycles {s.CompletedCycles})");
        var migrated = MachineStateMigration.Prepare(s);
        Check(migrated.Level == s.Level && migrated.Phase == s.Phase && migrated.DueAt == s.DueAt && migrated.CompletedCycles == s.CompletedCycles,
            $"2.0 cycle migration keeps 1.0.3 '{name}' (deadline {migrated.DueAt})");
        ProductionState.Load(migrated.Save());
    }
    catch (Exception ex) { Check(false, $"2.0 reads 1.0.3 '{name}': {ex.Message}\n{json}"); }
}
Check(records.Count >= 8, $"{records.Count} real 1.0.3 machine records checked");
Console.WriteLine($"{{\"suite\":\"upgrade-1.0.3\",\"passed\":{passed},\"failed\":{failed},\"status\":\"{(failed == 0 ? "PASS" : "FAIL")}\"}}");
return failed == 0 ? 0 : 1;

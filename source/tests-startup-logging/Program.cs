using System.Reflection;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Executes the real packaged log gate; inspects compiled event wiring and failure guards.
// Does not impersonate a running SMAPI/game session.
class Program
{
    static readonly List<string> Checks = new();
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        Checks.Add(name);
    }
    static IEnumerable<TypeDefinition> Types(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Types));
    static int? Constant(Instruction i) => i.OpCode.Code switch
    {
        Code.Ldc_I4_0 => 0, Code.Ldc_I4_1 => 1, Code.Ldc_I4_2 => 2, Code.Ldc_I4_3 => 3,
        Code.Ldc_I4_4 => 4, Code.Ldc_I4_5 => 5, Code.Ldc_I4_6 => 6, Code.Ldc_I4_7 => 7,
        Code.Ldc_I4_8 => 8, Code.Ldc_I4 => (int)i.Operand, Code.Ldc_I4_S => (sbyte)i.Operand, _ => null
    };
    static bool Calls(Instruction i, string type, string method) => i.Operand is MethodReference m && m.DeclaringType.Name == type && m.Name == method;
    static int LogCount(MethodDefinition method, int level) => method.Body.Instructions.Count(i => Calls(i, "IMonitor", "Log") && Constant(i.Previous) == level);
    static MethodDefinition Subscription(MethodDefinition method, string eventName, bool last = false)
    {
        var calls = method.Body.Instructions.Where(i => i.Operand is MethodReference m && m.Name == "add_" + eventName).ToArray();
        var call = last ? calls.Last() : calls.First();
        var cursor = call.Previous;
        while (cursor.OpCode.Code != Code.Ldftn) cursor = cursor.Previous;
        return ((MethodReference)cursor.Operand).Resolve();
    }
    static void Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(args[0]);
        var type = assembly.GetType("BetterStardewValley.StartupLog", true)!;
        var check = type.GetMethod("Check", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var complete = type.GetMethod("Complete", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object New() => Activator.CreateInstance(type, true)!;
        var messages = new List<string>();
        void Complete(object state) => complete.Invoke(state, new object[] { (Action<string>)messages.Add });
        var good = New();
        check.Invoke(good, new object[] { true });
        Complete(good);
        for (int i = 0; i < 20; i++) Complete(good);
        Check(messages.SequenceEqual(new[] { "BSV running successfully." }), "success text exact, no prefix, one emission across repeated completion attempts");
        foreach (string failure in new[] { "HD sprite", "horse", "annual fish", "wine", "harvest", "tea/apple", "totem", "orders", "AFK", "fishing machine" })
        {
            messages.Clear(); var state = New();
            check.Invoke(state, new object[] { false });
            for (int i = 0; i < 10; i++) check.Invoke(state, new object[] { true });
            Complete(state); Complete(state);
            Check(messages.Count == 0, "failure latch cannot be overwritten by later module success: " + failure);
        }
        // Real compiled event wiring: no callback is simulated as a live save load.
        using var module = ModuleDefinition.ReadModule(args[0]);
        using var smapi = ModuleDefinition.ReadModule(Path.Combine(Path.GetDirectoryName(args[0])!, "StardewModdingAPI.dll"));
        var levels = smapi.Types.Single(t => t.FullName == "StardewModdingAPI.LogLevel");
        int Level(string name) => Convert.ToInt32(levels.Fields.Single(f => f.Name == name).Constant);
        int info = Level("Info"), debug = Level("Debug"), error = Level("Error"), warn = Level("Warn");
        var entryType = module.Types.Single(t => t.FullName == "BetterStardewValley.ModEntry");
        var entry = entryType.Methods.Single(m => m.Name == "Entry");
        var final = Subscription(entry, "GameLaunched", last: true);
        Check(final.Body.Instructions.Any(i => Calls(i, "StartupLog", "Complete")), "summary registered on final GameLaunched handler after existing startup services");
        Check(final.Body.Instructions.Any(i => i.Operand as string == "diag.machine.loaded") && final.Body.Instructions.Any(i => i.Operand as string == "diag.machine.standalone"), "summary requires successful deferred machine registration or intentional standalone handoff");
        foreach (var handler in entry.Body.ExceptionHandlers.Where(h => h.HandlerType == ExceptionHandlerType.Catch))
        {
            var instructions = entry.Body.Instructions.SkipWhile(i => i != handler.HandlerStart).TakeWhile(i => i != handler.HandlerEnd).ToArray();
            var guarded = entry.Body.Instructions.SkipWhile(i => i != handler.TryStart).TakeWhile(i => i != handler.TryEnd).ToArray();
            if (guarded.Any(i => Calls(i, "DeveloperLetterService", ".ctor")))
            {
                Check(!instructions.Any(i => Calls(i, "StartupLog", "Check")) && instructions.Any(i => Calls(i, "IMonitor", "Log")), "optional appreciation mail isolates failure and retains an error diagnostic");
                continue;
            }
            Check(instructions.Any(i => Calls(i, "StartupLog", "Check") && Constant(i.Previous) == 0), "caught required initializer failure latches before summary, IL offset " + handler.HandlerStart.Offset);
        }
        foreach (string name in new[] { "HdSprites", "HorseFeature", "AnnualLegendaryFeature" })
            Check(entry.Body.Instructions.Any(i => Calls(i, name, "get_Initialized")), "internally caught initialization failure observed: " + name);
        Check(LogCount(entry, info) == 0 && LogCount(entry, error) >= 6, "Entry details no longer Info; all six caught initialization errors retained");
        var machine = Subscription(entry, "GameLaunched");
        Check(LogCount(machine, info) == 0 && LogCount(machine, debug) == 1 && LogCount(machine, error) == 2 && LogCount(machine, warn) == 1, "deferred machine detail Debug; initializer and config-recovery Errors plus standalone Warning preserved");
        var all = module.Types.SelectMany(Types).SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
        Check(all.Count(m => m.Body.Instructions.Any(i => Calls(i, "StartupLog", "Complete"))) == 1, "only startup callback can emit summary; no save/title/day callback calls it");
        Check(type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).All(m => m.Name != "Reset"), "no per-save summary reset");
        var integration = module.Types.Single(t => t.Name == "IntegrationHost").Methods.Single(m => m.Name == "Register");
        var adapter = Subscription(integration, "GameLaunched");
        Check(LogCount(adapter, info) == 0 && LogCount(adapter, debug) == 3 && LogCount(adapter, error) == 1, "Automate absence/disabled/success Debug; adapter exception still Error");
        var ui2 = module.Types.Single(t => t.Name == "Ui2Integration").Methods.Single(m => m.Name == "Launch");
        Check(LogCount(ui2, info) == 0 && LogCount(ui2, debug) == 4, "UI2 normal startup and horse key console details Debug");
        foreach (string name in new[] { "AnnualLegendaryFeature", "AfkFishingService" })
            Check(LogCount(all.Single(m => m.DeclaringType.Name == name && m.IsConstructor && !m.IsStatic), info) == 0, name + " startup has no Info");
        var version = JsonDocument.Parse(File.ReadAllText(args[2])).RootElement.GetProperty("Version").GetString();
        foreach (string path in args.Take(2))
            Check(AssemblyName.GetAssemblyName(path).Version!.ToString() == version + ".0" && System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion == version + ".0", "packaged main/bridge versions match manifest: " + Path.GetFileName(path));
        Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks = Checks.Count, scenarios = Checks, real_compiled_assembly = true, live_game = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

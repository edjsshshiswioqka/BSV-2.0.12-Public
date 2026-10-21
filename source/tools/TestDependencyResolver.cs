// Test process only. Not compiled into either BSV distributable assembly.
// SMAPI 4.5.2 ships Harmony referencing MonoMod.Common 22.6.3.1 alongside
// MonoMod.Common 22.3.5.1. A standalone test doesn't have SMAPI's loader.
// Explicitly bind this one supplied dependency without modifying its bytes.
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class TestDependencyResolver
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var requested = new AssemblyName(args.Name);
            if (requested.Name != "MonoMod.Common") return null;
            string path = Path.Combine(AppContext.BaseDirectory, "MonoMod.Common.dll");
            if (!File.Exists(path)) return null;
            Console.Error.WriteLine($"TEST-ONLY dependency binding: {args.Name} -> {AssemblyName.GetAssemblyName(path).FullName}");
            return Assembly.LoadFrom(path);
        };
    }
}

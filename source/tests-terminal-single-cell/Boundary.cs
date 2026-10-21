using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace BetterStardewValley
{
    internal static class IntegrationHost { internal static bool Saving; }
}
namespace BetterStardewValley.Orders
{
    internal sealed class OrdersService { internal static readonly OrdersService Instance = new();internal bool Enabled=true; }
    // Existing Orders renderer/settlement have their own unchanged suites. Record routing only.
    internal sealed class OrdersMenu : IClickableMenu
    {
        internal readonly string Location; internal readonly int X, Y;
        internal OrdersMenu(OrdersService service, string location, int x, int y) { Location = location; X = x; Y = y; }
    }
}
public class Recorder : DispatchProxy
{
    public static readonly List<(string Name, Delegate Handler)> Events = new();
    public static readonly List<string> Logs = new();
    public static readonly Dictionary<string, Texture2D> Textures = new();
    private readonly Dictionary<Type, object> Values = new();
    public static object Make(Type t) => typeof(DispatchProxy).GetMethods().Single(m => m.Name == "Create" && m.IsGenericMethodDefinition).MakeGenericMethod(t, typeof(Recorder)).Invoke(null, null)!;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var m = method!;
        if (m.Name.StartsWith("add_") && args?[0] is Delegate d) { Events.Add((m.Name, d)); return null; }
        if (m.Name == "Log") { Logs.Add(args![0]?.ToString() ?? ""); return null; }
        if (m.Name == "get_Locale") return NativeTranslationFixture.Locale;
        if (m.Name == "Get" && m.DeclaringType == typeof(ITranslationHelper)) return NativeTranslationFixture.Get((string)args![0]!,args.Length>1?args[1]:null);
        if (m.Name == "GetInAllLocales") return NativeTranslationFixture.All((string)args![0]!);
        if (m.Name == "Load" && m.ReturnType == typeof(Texture2D)) return Textures[(string)args![0]!];
        if (m.Name == "get_UniqueID") return "Eddie.BetterStardewValley";
        if (m.Name == "get_Version") return new SemanticVersion("1.1.10");
        if (m.Name is "get_Tile" or "get_GrabTile") return new Microsoft.Xna.Framework.Vector2(4, 4);
        var t = m.ReturnType; if (t == typeof(void)) return null;
        if (t.IsInterface) { if (!Values.TryGetValue(t, out var result)) Values[t] = result = Make(t); return result; }
        return t.IsValueType ? Activator.CreateInstance(t) : null;
    }
}

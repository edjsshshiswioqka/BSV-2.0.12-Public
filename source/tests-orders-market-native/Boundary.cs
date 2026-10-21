using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
public class Recorder : DispatchProxy
{
    public static readonly List<(string Name, Delegate Handler)> Events = new();
    public static readonly List<string> Logs = new();
    public static readonly Dictionary<string, Texture2D> Textures = new();
    public static readonly HashSet<string> InstalledMods = new();
    public static readonly Dictionary<string, Action<string,string[]>> Commands = new();
    public static readonly Dictionary<string,string> Versions = new();
    public static string? ModuleDirectory;
    public static int InputCalls, ConfigWrites, ApiCalls, SuppressCalls;
    public static Microsoft.Xna.Framework.Vector2 CursorTile = new(4,4);
    public static bool NullApi;
    public static object? ApiOverride;
    public static string? LastConfigJson,FailJsonFile;
    public static readonly Dictionary<string,string> JsonWrites=new();
    private string ReportedVersion = "2.0.4";
    public static string? PeerVersion="2.0.4";
    private string ReportedId = "Eddie.BetterStardewValley";
    private readonly Dictionary<Type, object> Values = new();
    public static object Make(Type t) => typeof(DispatchProxy).GetMethods().Single(m => m.Name == "Create" && m.IsGenericMethodDefinition).MakeGenericMethod(t, typeof(Recorder)).Invoke(null, null)!;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var m = method!;
        if (m.Name.StartsWith("add_") && args?[0] is Delegate d) { Events.Add((m.Name, d)); return null; }
        if (m.Name == "Add" && args?.Length == 3 && args[2] is Action<string,string[]> command) { Commands[(string)args[0]!] = command; return null; }
        if (m.Name == "GetMod" && m.DeclaringType == typeof(IMultiplayerPeer))
        {
            if(PeerVersion==null)return null;
            var peerMod=(IMultiplayerPeerMod)Make(typeof(IMultiplayerPeerMod));
            ((Recorder)(object)peerMod).ReportedVersion=PeerVersion;return peerMod;
        }
        if (m.Name == "GetApi") { ApiCalls++; if (NullApi) return null; if (ApiOverride!=null && m.ReturnType.IsInstanceOfType(ApiOverride))return ApiOverride; }
        if (m.Name == "WriteConfig") { ConfigWrites++;LastConfigJson=Newtonsoft.Json.JsonConvert.SerializeObject(args![0]); return null; }
        if (m.Name == "WriteJsonFile")
        {
            string path=(string)args![0]!;
            if(FailJsonFile==path){FailJsonFile=null;throw new IOException("Injected settings write failure");}
            JsonWrites[path]=Newtonsoft.Json.JsonConvert.SerializeObject(args[1]);return null;
        }
        if (m.DeclaringType == typeof(IInputHelper)) InputCalls++;
        if (m.Name == "Suppress") SuppressCalls++;
        if (m.Name is "get_Tile" or "get_GrabTile") return CursorTile;
        if (m.Name == "get_DirectoryPath") return ModuleDirectory;
        if (m.Name == "GetAll" && m.DeclaringType == typeof(IModRegistry))
            return InstalledMods.Select(id =>
            {
                var info = (IModInfo)Make(typeof(IModInfo));
                ((Recorder)(object)info).ReportedId = id;
                ((Recorder)(object)info).ReportedVersion = Versions.GetValueOrDefault(id, "9.0.0");
                return info;
            }).ToArray();
        if (m.Name == "Get" && m.DeclaringType == typeof(IModRegistry))
        {
            string id=(string)args![0]!;
            if (!InstalledMods.Contains(id)) return null;
            var info=(IModInfo)Make(typeof(IModInfo));
            ((Recorder)(object)info).ReportedId=id;
            ((Recorder)(object)info).ReportedVersion=Versions.GetValueOrDefault(id,"9.0.0");
            return info;
        }
        if(m.Name == "IsLoaded") return InstalledMods.Contains((string)args![0]!);
        if (m.Name == "Log") { Logs.Add(args![0]?.ToString() ?? ""); return null; }
        if (m.Name == "get_Locale") return NativeTranslationFixture.Locale;
        if (m.Name == "Get" && m.DeclaringType == typeof(ITranslationHelper)) return NativeTranslationFixture.Get((string)args![0]!,args.Length>1?args[1]:null);
        if (m.Name == "GetInAllLocales") return NativeTranslationFixture.All((string)args![0]!);
        if (m.Name == "Load" && m.ReturnType == typeof(Texture2D)) return Textures[(string)args![0]!];
        if (m.Name == "get_UniqueID") return ReportedId;
        if (m.Name == "get_Version") return new SemanticVersion(ReportedVersion);
        var t = m.ReturnType; if (t == typeof(void)) return null;
        if (t.IsInterface) { if (!Values.TryGetValue(t, out var result)) Values[t] = result = Make(t); ((Recorder)result).ReportedVersion=ReportedVersion; ((Recorder)result).ReportedId=ReportedId; return result; }
        return t.IsValueType ? Activator.CreateInstance(t) : null;
    }
}

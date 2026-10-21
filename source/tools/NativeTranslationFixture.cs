using System.Reflection;
using System.Text.Json;
using StardewModdingAPI;

// Test boundary only: actual SMAPI Translation objects, real shipped JSON.
// Normal dispatch is simulated here; production uses SMAPI's own helper.
internal static class NativeTranslationFixture
{
    internal static string Locale="en";
    private static readonly Dictionary<string,Dictionary<string,string>> Files=new();
    private static Dictionary<string,string> File(string locale)
    {
        if(Files.TryGetValue(locale,out var file))return file;
        string root=Environment.GetEnvironmentVariable("BSV_TEST_I18N_PATH")??throw new InvalidOperationException("Set BSV_TEST_I18N_PATH for native tests.");
        string path=Path.Combine(root,locale+".json");
        return Files[locale]=System.IO.File.Exists(path)?JsonSerializer.Deserialize<Dictionary<string,string>>(System.IO.File.ReadAllText(path))!:new();
    }
    private static Translation Value(string locale,string key,string? text)=>(Translation)Activator.CreateInstance(typeof(Translation),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object?[]{locale,key,text},null)!;
    internal static Translation Get(string key,object? tokens=null)=>Value(Locale,key,File(Locale).GetValueOrDefault(key)??File("default").GetValueOrDefault(key)).Tokens(tokens);
    internal static IDictionary<string,Translation> All(string key)
    {
        var result=new Dictionary<string,Translation>();
        foreach(string locale in new[]{"default",Locale}.Distinct())if(File(locale).TryGetValue(key,out var text))result[locale]=Value(locale,key,text);
        return result;
    }
}

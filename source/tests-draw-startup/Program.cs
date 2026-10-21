using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using SObject=StardewValley.Object;
class Program
{
 static void Main(string[] args)
 {
  int checks=0;var names=new List<string>();void Check(bool ok,string name){checks++;Console.Error.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)throw new Exception(name);names.Add(name);}
  var mod=Assembly.LoadFrom(args[0]);var resolver=mod.GetType("BetterStardewValley.DrawPatchTargets",true)!;
  var draw=(MethodInfo)resolver.GetMethod("ObjectMenu",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)!;
  Check(draw.DeclaringType==typeof(SObject)&&draw.ReturnType==typeof(void)&&draw.GetParameters().Length==8&&draw.GetParameters()[7].ParameterType==typeof(bool),"actual 1.6.15 Object draw target is declared eight-parameter override");
  Check(typeof(SObject).GetMethods().Count(m=>m.Name=="drawInMenu")>1,"actual game exposes inherited convenience overloads reproducing name-only ambiguity");
  bool reproduced=false;try{AccessTools.Method(typeof(SObject),"drawInMenu");}catch(AmbiguousMatchException){reproduced=true;}
  Check(reproduced,"old name-only lookup reproduces actual AmbiguousMatchException");
  bool rejected=false;try{resolver.GetMethod("Require",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{typeof(SObject),"drawInMenu",typeof(void),Type.EmptyTypes});}catch(TargetInvocationException e){rejected=e.InnerException is MissingMethodException;}
  Check(rejected,"missing exact signature fails closed before patch installation");
  var scan=JsonSerializer.Deserialize<List<Scan>>(File.ReadAllText(args[1]),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
  var reviewed=new List<object>();
  foreach(var row in scan)
  {
   var type=new[]{"", "StardewValley.", "StardewValley.Objects.", "StardewValley.TerrainFeatures.", "StardewValley.Locations.", "StardewValley.Menus.", "StardewValley.Characters.", "StardewValley.Buildings."}.Select(prefix=>typeof(Game1).Assembly.GetType(prefix+row.Type)).FirstOrDefault(t=>t!=null);
   if(type==null){reviewed.Add(new{row.File,row.Line,row.Type,row.Method,status="not a Stardew game type"});continue;}
   var methods=type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance).Where(m=>m.Name==row.Method).ToArray();
   var lookup=AccessTools.Method(type,row.Method);
   Check(lookup!=null,"remaining name-only game target resolves: "+row.Type+"."+row.Method);
   reviewed.Add(new{row.File,row.Line,row.Type,row.Method,visibleOverloads=methods.Select(m=>m.ToString()).ToArray(),resolved=lookup!.ToString()});
  }
  File.WriteAllText(args[2],JsonSerializer.Serialize(reviewed,new JsonSerializerOptions{WriteIndented=true}));
  var helper=(IModHelper)Recorder.Make(typeof(IModHelper));var monitor=(IMonitor)Recorder.Make(typeof(IMonitor));var manifest=(IManifest)Recorder.Make(typeof(IManifest));
  foreach(string typeName in new[]{"BetterStardewValley.Totems.TotemService","BetterStardewValley.Orders.OrdersService","BetterStardewValley.Totems.TotemUnlockService"})
  {
   Console.Error.WriteLine("START constructor "+typeName);
   object[] constructorArgs=typeName.EndsWith(".OrdersService")?new object[]{helper,monitor,manifest,(Func<IEnumerable<string>>)(()=>Array.Empty<string>())}:new object[]{helper,monitor,manifest};
   Activator.CreateInstance(mod.GetType(typeName,true)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,constructorArgs,null);
   Check(true,"actual production constructor completes with real Harmony and game assembly: "+typeName);
  }
  var afk=mod.GetType("BetterStardewValley.AfkFishing.AfkFishingService",true)!;
  var config=Activator.CreateInstance(mod.GetType("BetterStardewValley.ModConfig",true)!)!;
  Console.Error.WriteLine("START constructor AFK");
  Activator.CreateInstance(afk,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{helper,monitor,manifest,config},null);
  Check(true,"AFK production constructor and exact native Harmony patch targets install successfully");
  foreach(string name in new[]{"startMinigameEndFunction","doneHoldingFish","openTreasureMenuEndFunction","justGotDerbyTagEndFunction"})
  {
   var targets=Harmony.GetAllPatchedMethods().Where(m=>m.DeclaringType==typeof(StardewValley.Tools.FishingRod)&&m.Name==name).ToArray();
   Check(targets.Length==1 && Harmony.GetPatchInfo(targets[0])!.Owners.Contains(manifest.UniqueID+".AfkFishing"),"AFK exact native rod patch: "+name);
  }
  foreach(string name in new[]{"addItemToInventoryBool","OnItemReceived","getMovementSpeed"})
   Check(Harmony.GetAllPatchedMethods().Count(m=>m.DeclaringType==typeof(Farmer)&&m.Name==name&&Harmony.GetPatchInfo(m)!.Owners.Contains(manifest.UniqueID+".AfkFishing"))==1,"AFK exact farmer patch: "+name);
  Check(!Harmony.GetAllPatchedMethods().Any(m=>m.DeclaringType==typeof(StardewValley.Tools.FishingRod)&&m.Name=="tickUpdate"&&Harmony.GetPatchInfo(m)!.Owners.Contains(manifest.UniqueID+".AfkFishing")),"AFK no longer patches the global rod charge-meter update");
  Check(Recorder.Events.Any(e=>e.Name=="add_ButtonPressed"&&e.Handler.Method.DeclaringType==afk),"AFK manual override/toggle registered");
  foreach(string eventName in new[]{"add_ObjectListChanged","add_TerrainFeatureListChanged","add_BuildingListChanged","add_FurnitureListChanged","add_LargeTerrainFeatureListChanged"})
   Check(Recorder.Events.Any(e=>e.Name==eventName&&(e.Handler.Method.DeclaringType==afk||e.Handler.Method.DeclaringType?.DeclaringType==afk)),"AFK topology invalidation registered: "+eventName);
  var info=Harmony.GetPatchInfo(draw);
  Check(info?.Prefixes.Count(p=>p.PatchMethod.DeclaringType?.FullName=="BetterStardewValley.Totems.TotemArt")==1,"Totem menu patch installed exactly once on exact target");
  Check(info?.Prefixes.Count(p=>p.PatchMethod.DeclaringType?.FullName=="BetterStardewValley.Orders.OrderTerminal")==1,"Orders menu patch installed exactly once on exact target");
  var fishing=mod.GetType("Eddie.FishingMachine.MenuIconPatch",true)!;var fishHarmony=new Harmony("BSV.DrawHotfix.Test.Fishing");fishHarmony.CreateClassProcessor(fishing).Patch();
  Check(Harmony.GetPatchInfo(draw)!.Prefixes.Count(p=>p.PatchMethod.DeclaringType==fishing)==1,"third fishing/crab icon patch resolves and installs exactly once");
  foreach(string owner in new[]{"TotemArt","OrderTerminal","TotemUnlockService"})
   Check(Recorder.Events.Any(e=>e.Name=="add_AssetRequested"&&e.Handler.Method.DeclaringType?.Name==owner),"content registration survives constructor: "+owner);
  foreach(string eventName in new[]{"add_SaveLoaded","add_DayStarted","add_OneSecondUpdateTicked"})
   Check(Recorder.Events.Any(e=>e.Name==eventName&&e.Handler.Method.DeclaringType?.Name=="OrderTerminal"),"terminal mail event restored: "+eventName);
  foreach(string name in new[]{"add_UpdateTicked","add_DayStarted","add_DayEnding"}) Check(Recorder.Events.Any(e=>e.Name==name&&(e.Handler.Method.DeclaringType?.Name=="OrdersService"||e.Handler.Method.DeclaringType?.DeclaringType?.Name=="OrdersService")),"Orders 2 durable intent/day lifecycle registered: "+name);
  Check(Recorder.Events.Any(e=>e.Name=="add_ModMessageReceived"&&e.Handler.Method.DeclaringType?.Name=="TotemService"),"totem multiplayer callback fully registered");
  Check(!Recorder.Logs.Any(s=>s.Contains("AmbiguousMatchException")||s.Contains("stopped")),"constructor run has no ambiguity or stopped-action logs");
  // This standalone process owns its detours. Native unpatch teardown can segfault in the
  // headless Linux runtime even after all initialization assertions pass. Preserve an explicit
  // reproduction mode; do not modify production patches or hide the saved failure evidence.
  if(args.Contains("--probe-detour-cleanup"))
   foreach(string id in new[]{manifest.UniqueID+".AfkFishing",manifest.UniqueID+".Totems",manifest.UniqueID+".Orders",manifest.UniqueID+".TotemUnlocks",fishHarmony.Id})
   {Console.Error.WriteLine("START native detour cleanup "+id);new Harmony(id).UnpatchAll(id);}
  Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks,scenarios=names,real_harmony_patch_installation=true,real_game_assembly=true,smapi_event_interfaces="recording proxies",live_game=false,real_multiplayer=false}));
 }
 public sealed record Scan(string File,int Line,string Type,string Method);
}
public class Recorder:DispatchProxy
{
 public static List<(string Name,Delegate Handler)> Events=new();public static List<string> Logs=new();private Dictionary<Type,object> Values=new();
 public static object Make(Type t)=>typeof(DispatchProxy).GetMethods().Single(m=>m.Name=="Create"&&m.IsGenericMethodDefinition).MakeGenericMethod(t,typeof(Recorder)).Invoke(null,null)!;
 protected override object? Invoke(MethodInfo? method,object?[]? args)
 {
  var m=method!;
  if(m.Name.StartsWith("add_")&&args?[0] is Delegate d){Events.Add((m.Name,d));return null;}
  if(m.Name=="Log"){Logs.Add(args![0]?.ToString()??"");return null;}
  if(m.Name=="get_UniqueID")return "BSV.DrawHotfix.Test";
  if(m.Name=="get_Version")return new SemanticVersion("1.1.5");
  if(m.Name=="get_Locale")return NativeTranslationFixture.Locale;
  if(m.Name=="Get"&&m.DeclaringType==typeof(ITranslationHelper))return NativeTranslationFixture.Get((string)args![0]!,args.Length>1?args[1]:null);
  if(m.Name=="GetInAllLocales")return NativeTranslationFixture.All((string)args![0]!);
  var t=m.ReturnType;if(t==typeof(void))return null;
  if(t.IsInterface){if(!Values.TryGetValue(t,out var value))Values[t]=value=Make(t);return value;}
  return t.IsValueType?Activator.CreateInstance(t):null;
 }
}

// Actual game objects and released BSV assembly; no Tractor Mod DLL or GPU fixture.
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
class Program
{
 static readonly BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static readonly Type Feature=typeof(BetterStardewValley.ModConfig).Assembly.GetType("BetterStardewValley.HorseFeature")!;
 static readonly List<string> Checks=new();
 static object? Invoke(string method,params object?[] args)=>Feature.GetMethod(method,Any)!.Invoke(null,args);
 static void Check(bool pass,string label){if(!pass)throw new Exception(label);Checks.Add(label);}
 static bool Skip()=>false;
 static void Main()
 {
  var harmony=new Harmony("BSV.R14.NativeFixture");
  harmony.Patch(typeof(AnimatedSprite).GetMethod("LoadTexture",Any,null,new[]{typeof(string),typeof(bool)},null)!,prefix:new(typeof(Program),nameof(Skip)));
  Game1.buildingData=new Dictionary<string,StardewValley.GameData.Buildings.BuildingData>();
  var horse=new Horse();var garage=new Stable();garage.buildingType.Value="Pathoschild.TractorMod_Stable";
  Check(!(bool)Invoke("IsRealManagedHorse",new object?[]{null})!,"null is not a real horse");
  foreach(string name in new[]{"Tractor","tractor/legacy","Maple","拖拉机"})
  {
   horse.Name=name;Check((bool)Invoke("IsRealManagedHorse",horse)!,"actual native unmarked horse accepted: "+name);
   foreach(string value in new[]{"","0","1","custom"})
   {
    horse.modData["Pathoschild.TractorMod"]=value;
    Check((bool)Invoke("IsTractorHorse",horse)!&&!(bool)Invoke("IsRealManagedHorse",horse)!,"actual NetStringDictionary presence-only marker: "+name+"/"+value);
    object?[] args={horse,null,false};
    Check((bool)Invoke("BeforeAction",args)!&&!(bool)args[2]!,"actual checkAction prefix preserves result/original execution");
    horse.modData.Remove("Pathoschild.TractorMod");
   }
  }
  garage.owner.Value=874;garage.modData["Eddie.BetterStardewValley/HorseName"]="Legacy";
  Check((bool)Invoke("IsTractorStable",garage)!,"official garage Data/Buildings ID recognized without horse");
  Check((bool)Invoke("BeforeOwnership",garage)!&&(bool)Invoke("BeforeGrab",garage)!,"empty garage native lifecycle pass-through");
  Invoke("Recover",garage);Invoke("ApplyName",garage);Invoke("BeforeDemolition",garage);
  Check(garage.owner.Value==874&&garage.modData["Eddie.BetterStardewValley/HorseName"]=="Legacy","native empty garage owner and tags preserved");
  horse.modData["Pathoschild.TractorMod"]="1";
  bool rejected=false;
  try{Activator.CreateInstance(Feature.GetNestedType("Summon",BindingFlags.NonPublic)!,Any,null,new object?[]{null,horse,null,garage,0d},null);}
  catch(TargetInvocationException ex)when(ex.InnerException is InvalidOperationException){rejected=true;}
  Check(rejected&&!horse.modData.ContainsKey("Eddie.BetterStardewValley/HorseRun"),"actual Summon constructor rejects before native mutation");
  Console.WriteLine(JsonSerializer.Serialize(new{passed=true,assertions=Checks.Count,checks=Checks,game=typeof(Horse).Assembly.GetName().Version!.ToString(),live_game=false,tractor_dll_loaded=false,limitations="Native game types and released BSV DLL; no real Tractor Mod runtime, GPU or multiplayer transport."},new JsonSerializerOptions{WriteIndented=true}));
 }
}

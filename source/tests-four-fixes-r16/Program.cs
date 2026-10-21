using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Mono.Cecil;
using StardewValley;
using SObject = StardewValley.Object;
class Program
{
 static readonly BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static readonly Assembly Mod=typeof(BetterStardewValley.ModConfig).Assembly;
 static readonly List<string> Checks=new();
 static void Check(bool pass,string label){if(!pass)throw new Exception(label);Checks.Add(label);}
 static void Main()
 {
  var rules=Mod.GetType("BetterStardewValley.WineRules")!;
  string wine=(string)rules.GetField("GrapeWine",Any)!.GetRawConstantValue()!;
  int price=(int)rules.GetField("GrapeWinePrice",Any)!.GetRawConstantValue()!;
  Check(price==990,"production wine constant 990");
  Check((int)rules.GetField("BloodOfJesusPrice",Any)!.GetRawConstantValue()! ==30000,"rare wine unchanged");
  var normalize=Mod.GetType("BetterStardewValley.GrapeWineFeature")!.GetMethod("NormalizePrice",Any)!;
  foreach(int q in new[]{0,1,2,4})
  {
   var obj=new SObject(); obj.ItemId=wine[3..]; obj.Price=1800; obj.Stack=17; obj.Quality=q;
   obj.modData["fixture/owner"]="kept";
   normalize.Invoke(null,new object[]{obj});normalize.Invoke(null,new object[]{obj});
   Check(obj.Price==990&&obj.Stack==17&&obj.Quality==q&&obj.modData["fixture/owner"]=="kept","native serialized instance idempotent repair quality "+q);
  }
  var other=new SObject();other.ItemId="348";other.Price=987;normalize.Invoke(null,new object[]{other});
  Check(other.Price==987,"vanilla wine instance untouched");
  int duration=(int)Mod.GetType("BetterStardewValley.TeaAppleRules")!.GetField("InvincibleMilliseconds",Any)!.GetRawConstantValue()!;
  Check(duration==2500,"production apple duration 2500");
  using var assembly=AssemblyDefinition.ReadAssembly(Mod.Location);
  var apple=assembly.MainModule.GetType("BetterStardewValley.GoldenAppleFeature");
  foreach(string method in new[]{"AfterDamage","Tick"})
  {
   var writes=apple.Methods.Single(m=>m.Name==method).Body.Instructions.Where(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld).Select(i=>i.Operand.ToString()).ToArray();
   Check(!writes.Any(s=>s.Contains("temporarilyInvincible")||s.Contains("temporaryInvincibilityTimer")||s.Contains("currentTemporaryInvincibilityDuration")),"apple "+method+" never clears or replaces another protection source");
  }
  var horse=assembly.MainModule.GetType("BetterStardewValley.HorseFeature");
  Check(!horse.Methods.Single(m=>m.Name=="StartRoute").Body.Instructions.Any(i=>i.Operand is MethodReference mr&&mr.Name=="warpCharacter"),"summon has no offscreen warp fallback");
  Check(horse.Methods.Single(m=>m.Name=="Mountable").Body.Instructions.Any(i=>i.Operand is MethodReference mr&&mr.Name=="withinRadiusOfPlayer"),"docking uses native interaction radius");
  Console.WriteLine(JsonSerializer.Serialize(new{passed=true,assertions=Checks.Count,checks=Checks,scope="Native Object price repair and production IL contract checks; movement, duration scheduling and rendering need runtime validation",live_game=false},new JsonSerializerOptions{WriteIndented=true}));
 }
}

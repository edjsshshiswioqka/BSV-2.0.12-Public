using HarmonyLib;
using System.Reflection.Emit;
namespace Microsoft.Xna.Framework {public record struct Vector2(float X,float Y);}
namespace StardewValley
{
 public class Item
 {
  public string QualifiedItemId="(O)24",Color="";public int Stack=1,Quality;
  public Dictionary<string,string> modData=new();
  public virtual Item getOne()=>new(){QualifiedItemId=QualifiedItemId,Quality=Quality,Color=Color,modData=new(modData)};
 }
 public class Farmer
 {
  public int Space=99;public List<Item> Inventory=new();
  public Item? addItemToInventory(Item item){int n=Math.Min(Space,item.Stack);if(n>0){var c=item.getOne();c.Stack=n;Inventory.Add(c);Space-=n;item.Stack-=n;}return item.Stack>0?item:null;}
 }
 public class GameLocation{}
 public class Junimo{public List<Item> Hut=new();public void tryToAddItemToHut(Item i)=>Hut.Add(i);}
 public static class LocalizedContentManager {public enum LanguageCode{en,zh,fr,de,ja,pt,es,ru,ko,tr,it,hu,mod};public static LanguageCode CurrentLanguageCode;}
 public static class Game1{public static object content=new();public static List<Item> Debris=new();public static void createItemDebris(Item i,Microsoft.Xna.Framework.Vector2 p,int d,GameLocation l)=>Debris.Add(i);}
 public static class CraftingRecipe{public static Dictionary<string,string> craftingRecipes=new(),cookingRecipes=new();}
 public static class DataLoader{public static Dictionary<string,string> CraftingRecipes(object c)=>new(){{"fresh","crafting"}};public static Dictionary<string,string> CookingRecipes(object c)=>new(){{"fresh","cooking"}};}
}
namespace StardewModdingAPI
{
 public static class Context{public static bool IsWorldReady=true;}
 public class Translation
 {
  public string Text;public Translation(string text){Text=text;}
  public bool HasValue()=>Text!=null;
  public Translation Tokens(object? tokens){string t=Text;if(tokens!=null)foreach(var p in tokens.GetType().GetProperties())t=t.Replace("{{"+p.Name+"}}",Convert.ToString(p.GetValue(tokens),System.Globalization.CultureInfo.InvariantCulture));return new(t);}
  public override string ToString()=>Text;
 }
 public class Translations
 {
  public Dictionary<string,Dictionary<string,string>> Locales=new();public int Reads;
  public string Locale="en";
  public bool Broken;
  public Translation Get(string key,object? tokens=null){Reads++;if(Broken)throw new InvalidOperationException("fixture unavailable");string? value=null;if(Locales.TryGetValue(Locale,out var local))local.TryGetValue(key,out value);if(value==null&&Locales.TryGetValue("default",out var fallback))fallback.TryGetValue(key,out value);return new Translation(value!).Tokens(tokens);}
  public IDictionary<string,Translation> GetInAllLocales(string key){Reads++;return Locales.Where(p=>p.Value.ContainsKey(key)).ToDictionary(p=>p.Key,p=>new Translation(p.Value[key]));}
 }
 public class ContentEvents {public event EventHandler? LocaleChanged;public void Change()=>LocaleChanged?.Invoke(this,EventArgs.Empty);}
 public class EventsHub {public ContentEvents Content=new();}
 public class Content {public HashSet<string> Invalidated=new();public void InvalidateCache(string a)=>Invalidated.Add(a);}
 public class IModHelper {public Translations Translation=new();public EventsHub Events=new();public Content GameContent=new();}
}
namespace BetterStardewValley
{
 public sealed partial class ModEntry
 {
  private sealed class HarvestState
  {public HarvestState? Parent;public int Multiplier;public int MinimumQuality;public string HarvestId="(O)24";public List<StardewValley.Item> Crops=new();public StardewValley.Junimo? Junimo;public StardewValley.Farmer Farmer=new();public bool DropOnGround;public Microsoft.Xna.Framework.Vector2 Position;public StardewValley.GameLocation Location=new();}
  internal static void CheckBoundary(Action<bool,string> check)
  {
   var method=AccessTools.Method(typeof(StardewValley.Item),nameof(StardewValley.Item.getOne));
   var input=new[]{new CodeInstruction(OpCodes.Callvirt,method),new CodeInstruction(OpCodes.Nop)};
   var rewritten=CaptureHarvestOutputs(input).ToArray();check(rewritten[0].opcode==OpCodes.Call&&((System.Reflection.MethodInfo)rewritten[0].operand).Name=="CopyHarvestItem"&&rewritten[1].opcode==OpCodes.Nop,"only clone boundary rewritten");
   bool threw=false;try{CaptureHarvestOutputs(new[]{new CodeInstruction(OpCodes.Nop)}).ToArray();}catch(InvalidOperationException){threw=true;}check(threw,"unknown native boundary fails explicitly");
   var scope=new HarvestState{Multiplier=3};ActiveHarvest=scope;
   var original=new StardewValley.Item{Quality=4,Color="blue",Stack=7};original.modData["producer"]="kept";
   var one=CopyHarvestItem(original);one.Stack=0;
   check(original.Stack==7&&scope.Crops.Count==1&&scope.Crops[0].Stack==1,"capture doesn't alter native output or source quantity");
   check(scope.Crops[0].Quality==4&&scope.Crops[0].Color=="blue"&&scope.Crops[0].modData["producer"]=="kept","color, quality and metadata preserved");
   CopyHarvestItem(new(){QualifiedItemId="(O)431"});check(scope.Crops.Count==1,"sunflower seeds and byproducts are not crop duplicates");
   var nested=new HarvestState{Parent=scope,Multiplier=2};ActiveHarvest=nested;CopyHarvestItem(original);FinishHarvestScope(nested);check(ReferenceEquals(ActiveHarvest,scope)&&nested.Crops.Count==1&&scope.Crops.Count==1,"nested crop context restores parent");
   FinishHarvestScope(scope);CopyHarvestItem(original);check(ActiveHarvest==null&&scope.Crops.Count==1,"scope cleared after harvest or finalizer exception cleanup");
   foreach(int multiplier in new[]{1,2,3})foreach(int floor in new[]{0,2,4})foreach(int quality in new[]{0,1,2,4})
   {
    var qscope=new HarvestState{Multiplier=multiplier,MinimumQuality=floor};ActiveHarvest=qscope;
    var inputItem=new StardewValley.Item{Quality=quality,Stack=1};var promoted=CopyHarvestItem(inputItem);
    check(promoted.Quality==Math.Max(quality,floor)&&inputItem.Quality==quality,"totem quality floor preserves source and higher original quality");
    check(qscope.Crops.Count==1&&qscope.Crops.All(i=>i.Quality==promoted.Quality),"native product is captured once before host multiplier selection with promoted quality");
    var seed=CopyHarvestItem(new(){QualifiedItemId="(O)431",Quality=0});
    check(seed.Quality==0&&qscope.Crops.Count==1,"harvest byproducts and seed refunds remain outside totem quality/multiplier capture");
    FinishHarvestScope(qscope);
   }
   foreach(bool scythe in new[]{false,true})foreach(bool junimo in new[]{false,true}){
    var s=new HarvestState{Farmer=new(){Space=1},DropOnGround=scythe,Junimo=junimo?new():null};StardewValley.Game1.Debris.Clear();var reward=original.getOne();reward.Stack=2;GiveHarvestBonus(s,reward);
    int inventory=s.Farmer.Inventory.Sum(i=>i.Stack),ground=StardewValley.Game1.Debris.Sum(i=>i.Stack),hut=s.Junimo?.Hut.Sum(i=>i.Stack)??0;
    check(inventory+ground+hut==2,"no partial-inventory duplicate/loss");check(junimo?hut==2:scythe?ground==2:inventory==1&&ground==1,"same Junimo/scythe/player destination, full bag remainder drops");
   }
  }
 }
}

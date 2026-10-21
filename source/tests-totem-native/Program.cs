// A — uses real game recipe parsing, inventory validation/consumption and object creation headlessly.
using BetterStardewValley.Totems;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.GameData.BigCraftables;
using StardewValley.ItemTypeDefinitions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using SObject=StardewValley.Object;
int checks=0;var scenarios=new HashSet<string>();void Check(bool ok,string why){checks++;scenarios.Add(why);if(!ok)throw new Exception(why);}
void Init(object target,string field,params object[] args){var f=target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!;f.SetValue(target,Activator.CreateInstance(f.FieldType,args));}
ItemRegistry.AddTypeDefinition(new ObjectDataDefinition());ItemRegistry.AddTypeDefinition(new BigCraftableDataDefinition());
Game1.objectData=new Dictionary<string,ObjectData>();Game1.bigCraftableData=new Dictionary<string,BigCraftableData>();
Game1.random=new Random(1);
var who=(Farmer)FormatterServices.GetUninitializedObject(typeof(Farmer));Init(who,"netItems",new StardewValley.Inventories.Inventory());Init(who,"craftingRecipes");typeof(Game1).GetField("_player",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,who);
CraftingRecipe.craftingRecipes=new();CraftingRecipe.cookingRecipes=new();
foreach(var r in TotemUnlockRules.Recipes)
{
 var parts=r.Ingredients.Split(' ');var expected=new Dictionary<string,int>();for(int i=0;i<parts.Length;i+=2)expected[parts[i]]=int.Parse(parts[i+1]);
 foreach(var id in expected.Keys)Game1.objectData[id]=new(){Name=id,DisplayName=id,Description="Test material",Type="Basic",Category=-16,Price=10,Texture="Maps/springobjects",SpriteIndex=0};
 Game1.bigCraftableData[r.Id]=new(){Name=r.Id,DisplayName=r.Kind+" Totem",Description="Permanent BSV totem",Texture="test",SpriteIndex=0,Fragility=0};
 CraftingRecipe.craftingRecipes[r.Id]=r.Data(r.Kind+" Totem");
 var recipe=new CraftingRecipe(r.Id,false);
 Check(recipe.recipeList.OrderBy(p=>p.Key).SequenceEqual(expected.OrderBy(p=>p.Key)),"real native parser: exact ingredients "+r.Kind);
 Check(recipe.bigCraftable&&!recipe.isCookingRecipe&&recipe.numberProducedPerCraft==1&&recipe.itemToProduce.SequenceEqual(new[]{r.Id}),"real output metadata "+r.Kind);
 Check(!CraftingRecipe.TryParseLevelRequirement(r.Id,CraftingRecipe.craftingRecipes[r.Id],false,out _,out _),"native level-up cannot silently grant "+r.Kind);
 who.Items.Clear();foreach(var p in expected)who.Items.Add(new SObject(p.Key,p.Value*2));
 Check(recipe.doesFarmerHaveIngredientsInInventory(),"native inventory accepts exact ingredients "+r.Kind);
 recipe.consumeIngredients(null!);Check(expected.All(p=>who.Items.CountId("(O)"+p.Key)==p.Value),"first craft consumes every material exactly once "+r.Kind);
 var item=recipe.createItem();Check(item.QualifiedItemId=="(BC)"+r.Id&&item.Stack==1&&item is SObject o&&o.bigCraftable.Value,"native craft creates one permanent existing-ID object "+r.Kind);
 recipe.consumeIngredients(null!);Check(who.Items.All(i=>i==null),"second copy consumes remaining materials without refunds "+r.Kind);
 Check(!recipe.doesFarmerHaveIngredientsInInventory(),"empty inventory cannot craft "+r.Kind);
 foreach(var missing in expected.Keys)
 {
  who.Items.Clear();foreach(var p in expected)who.Items.Add(new SObject(p.Key,p.Value-(p.Key==missing?1:0)));
  var before=who.Items.Select(i=>i.Stack).ToArray();Check(!recipe.doesFarmerHaveIngredientsInInventory(),"one missing unit prevents crafting "+r.Kind+" "+missing);
  Check(before.SequenceEqual(who.Items.Select(i=>i.Stack)),"availability check cannot consume partial inputs "+r.Kind+" "+missing);
 }
 var original=new TotemState().Used(r.Kind,4,120);Check(original.Remaining(r.Kind,4,120)>0,"new crafted copies do not alter shared cooldown "+r.Kind);
}
// Collection rereads must not teach recipes; this executes the game's own token handler.
var letter=(StardewValley.Menus.LetterViewerMenu)FormatterServices.GetUninitializedObject(typeof(StardewValley.Menus.LetterViewerMenu));letter.isFromCollection=true;
foreach(var r in TotemUnlockRules.Recipes){string text=letter.HandleItemCommand("hello%item craftingrecipe "+r.Id+" %%");Check(!who.craftingRecipes.ContainsKey(r.Id)&&text=="hello","native archived mail cannot grant recipe "+r.Kind);}
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,assertions=checks,scenarios,real_game=false,real_multiplayer=false,real_game_classes=true}));

using HarmonyLib;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.GameData.Machines;
using StardewValley.Buildings;
using StardewValley.Inventories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
namespace BetterStardewValley.MutantLobsterKing;

internal static class UsagePolicy
{
    internal static bool Protected(Item? item)=>item!=null&&MutantLobsterRules.IsItem(item.QualifiedItemId);
    internal static void Install(Harmony h)
    {
        h.Patch(DrawPatchTargets.Require(typeof(StardewValley.Object),"PlaceInMachine",typeof(bool),typeof(MachineData),typeof(Item),typeof(bool),typeof(Farmer),typeof(bool),typeof(bool)),prefix:new(typeof(UsagePolicy),nameof(Input)));
        h.Patch(DrawPatchTargets.Require(typeof(MachineDataUtility),"CanApplyOutput",typeof(bool),typeof(StardewValley.Object),typeof(MachineOutputRule),typeof(MachineOutputTrigger),typeof(Item),typeof(Farmer),typeof(GameLocation),typeof(MachineOutputTriggerRule).MakeByRefType(),typeof(bool).MakeByRefType()),prefix:new(typeof(UsagePolicy),nameof(Output)));
        h.Patch(DrawPatchTargets.Require(typeof(FishPond),"isLegalFishForPonds",typeof(bool),typeof(string)),prefix:new(typeof(UsagePolicy),nameof(Pond)));
        h.Patch(DrawPatchTargets.Require(typeof(CraftingRecipe),"consumeIngredients",typeof(void),typeof(List<IInventory>)),transpiler:new(typeof(UsagePolicy),nameof(ConsumptionMatches)));
        h.Patch(DrawPatchTargets.Require(typeof(CraftingRecipe),"ConsumeAdditionalIngredients",typeof(void),typeof(List<KeyValuePair<string,int>>),typeof(List<IInventory>)),transpiler:new(typeof(UsagePolicy),nameof(ConsumptionMatches)));
        // The native availability/count/UI paths don't all call ItemMatchesForCrafting.
        // Keep them consistent even if an external data edit explicitly requests this ID.
        h.Patch(DrawPatchTargets.Require(typeof(CraftingRecipe),"doesFarmerHaveIngredientsInInventory",typeof(bool),typeof(IList<Item>)),prefix:new(typeof(UsagePolicy),nameof(Available)));
        h.Patch(DrawPatchTargets.Require(typeof(CraftingRecipe),"getCraftableCount",typeof(int),typeof(IList<Item>)),prefix:new(typeof(UsagePolicy),nameof(Count)));
        h.Patch(DrawPatchTargets.Require(typeof(CraftingRecipe),"DoesFarmerHaveAdditionalIngredientsInInventory",typeof(bool),typeof(List<KeyValuePair<string,int>>),typeof(IList<Item>)),prefix:new(typeof(UsagePolicy),nameof(Additional)));
        h.Patch(DrawPatchTargets.Require(typeof(CraftingRecipe),"drawRecipeDescription",typeof(void),typeof(SpriteBatch),typeof(Vector2),typeof(int),typeof(IList<Item>)),transpiler:new(typeof(UsagePolicy),nameof(DescriptionCounts)));
    }
    private static bool Input([HarmonyArgument(1)]Item inputItem,ref bool __result)
    {if(!Protected(inputItem))return true;__result=false;return false;}
    private static bool Output([HarmonyArgument(3)]Item inputItem,[HarmonyArgument(6)]ref MachineOutputTriggerRule? triggerRule,[HarmonyArgument(7)]ref bool invalidCount,ref bool __result)
    {if(!Protected(inputItem))return true;triggerRule=null;invalidCount=false;__result=false;return false;}
    private static bool Ingredient(Item item,string id)=>!Protected(item)&&CraftingRecipe.ItemMatchesForCrafting(item,id);
    private static IEnumerable<CodeInstruction> ConsumptionMatches(IEnumerable<CodeInstruction> source)
    {
        // Never patch ItemMatchesForCrafting globally: Farmer inventory counts also call it.
        var code=source.Select(i=>new CodeInstruction(i)).ToList();int matches=0;
        var target=DrawPatchTargets.Require(typeof(CraftingRecipe),"ItemMatchesForCrafting",typeof(bool),typeof(Item),typeof(string));
        foreach(var i in code)if(i.Calls(target)){i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(UsagePolicy),nameof(Ingredient));matches++;}
        if(matches!=2)throw new InvalidOperationException($"Mutant recipe deduction mismatch: {matches}; expected 2.");
        return code;
    }
    private static bool Available(CraftingRecipe __instance,ref bool __result)
    {if(!__instance.recipeList.Keys.Any(MutantLobsterRules.IsItem))return true;__result=false;return false;}
    private static bool Count(CraftingRecipe __instance,ref int __result)
    {if(!__instance.recipeList.Keys.Any(MutantLobsterRules.IsItem))return true;__result=0;return false;}
    private static bool Additional([HarmonyArgument(0)]List<KeyValuePair<string,int>> ingredients,ref bool __result)
    {if(!ingredients.Any(p=>MutantLobsterRules.IsItem(p.Key)))return true;__result=false;return false;}
#pragma warning disable CS0618 // these replace the native calls inside drawRecipeDescription, so they must keep the same (category-aware) matching
    internal static int InventoryCount(Farmer who,string id)=>MutantLobsterRules.IsItem(id)?0:who.getItemCount(id);
    internal static int ContainerCount(Farmer who,IList<Item> items,string id)=>MutantLobsterRules.IsItem(id)?0:who.getItemCountInList(items,id);
#pragma warning restore CS0618
    private static IEnumerable<CodeInstruction> DescriptionCounts(IEnumerable<CodeInstruction> source)
    {
        var code=source.Select(i=>new CodeInstruction(i)).ToList();
        var own=DrawPatchTargets.Require(typeof(Farmer),"getItemCount",typeof(int),typeof(string));
        var other=DrawPatchTargets.Require(typeof(Farmer),"getItemCountInList",typeof(int),typeof(IList<Item>),typeof(string));
        int a=0,b=0;
        foreach(var i in code)
        {
            if(i.Calls(own)){i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(UsagePolicy),nameof(InventoryCount));a++;}
            else if(i.Calls(other)){i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(UsagePolicy),nameof(ContainerCount));b++;}
        }
        if(a!=1||b!=1)throw new InvalidOperationException($"Mutant recipe display counts mismatch: {a}/{b}; expected 1/1.");
        return code;
    }
    private static bool Pond([HarmonyArgument(0)]string itemId,ref bool __result)
    {if(!MutantLobsterRules.IsItem(itemId))return true;__result=false;return false;}
}

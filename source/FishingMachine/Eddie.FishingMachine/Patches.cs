using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using SObject=StardewValley.Object;
namespace Eddie.FishingMachine;

[HarmonyPatch]
internal static class WorldDrawPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(SObject),"draw",new[]{typeof(SpriteBatch),typeof(int),typeof(int),typeof(float)});
        yield return AccessTools.Method(typeof(Chest),"draw",new[]{typeof(SpriteBatch),typeof(int),typeof(int),typeof(float)});
        yield return AccessTools.Method(typeof(Chest),"draw",new[]{typeof(SpriteBatch),typeof(int),typeof(int),typeof(float),typeof(bool)});
    }
    static bool Prefix(SObject __instance,SpriteBatch spriteBatch,int x,int y,float alpha)
    {
        if(!ModEntry.IsMachine(__instance))return true;
        if(ModEntry.IsAnchor(__instance) || !ModEntry.IsPart(__instance))ModEntry.Instance.World.DrawMachine(spriteBatch,new(x,y),alpha,__instance as Chest);
        return false;
    }
}
[HarmonyPatch]
internal static class MenuIconPatch
{
    static System.Reflection.MethodBase TargetMethod() => BetterStardewValley.DrawPatchTargets.ObjectMenu();
    static bool Prefix(SObject __instance,SpriteBatch spriteBatch,Vector2 location,float scaleSize,float transparency,float layerDepth,StackDrawType drawStackNumber)
    {
        if(!ModEntry.IsMachine(__instance))return true;
        ModEntry.Instance.Art.DrawIcon(spriteBatch,location,scaleSize,transparency,layerDepth,ModEntry.Instance.Production.Level(__instance),ModEntry.IsCrab(__instance));
        if(__instance.Stack>1 && drawStackNumber!=StackDrawType.Hide)
            Utility.drawTinyDigits(__instance.Stack,spriteBatch,location+new Vector2(44,44),2,layerDepth,Color.White*transparency);
        return false;
    }
}
[HarmonyPatch(typeof(SObject),"drawWhenHeld",new[]{typeof(SpriteBatch),typeof(Vector2),typeof(Farmer)})]
internal static class HeldMachinePatch
{
    static bool Prefix(SObject __instance,SpriteBatch spriteBatch,Vector2 objectPosition,Farmer f)
    {
        if(!ModEntry.IsMachine(__instance))return true;
        ModEntry.Instance.Art.DrawHeld(spriteBatch,objectPosition,f,ModEntry.Instance.Production.Level(__instance),ModEntry.IsCrab(__instance));
        return false;
    }
}
[HarmonyPatch(typeof(SObject),"drawPlacementBounds")]
internal static class PlacementDrawPatch
{static bool Prefix(SObject __instance)=>!ModEntry.IsMachine(__instance);}

[HarmonyPatch(typeof(SObject),"canBePlacedHere")]
internal static class PlacementCheckPatch
{
    static bool Prefix(SObject __instance,GameLocation l,Vector2 tile,ref bool __result)
    {
        if(!ModEntry.IsMachine(__instance))return true;
        __result=ModEntry.Instance.World.CanPlace(l,tile,ModEntry.IsCrab(__instance));return false;
    }
}
[HarmonyPatch(typeof(SObject),"placementAction")]
internal static class PreventSingleTilePlacementPatch
{
    static bool Prefix(SObject __instance,ref bool __result)
    {if(!ModEntry.IsMachine(__instance))return true;__result=false;return false;}
}
[HarmonyPatch]
internal static class OpenPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(SObject),"checkForAction");
        yield return AccessTools.Method(typeof(Chest),"checkForAction");
    }
    static bool Prefix(SObject __instance,bool justCheckingForActivity,ref bool __result)
    {
        if(!ModEntry.IsPart(__instance))return true;
        __result=true;
        if(!justCheckingForActivity && __instance.Location!=null && ModEntry.Instance.World.TryAnchor(__instance.Location,__instance.TileLocation,out var c))
            ModEntry.Instance.World.Open(c!);
        return false;
    }
}
[HarmonyPatch]
internal static class ToolProtectionPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(SObject),"performToolAction",new[]{typeof(Tool)});
        yield return AccessTools.Method(typeof(Chest),"performToolAction",new[]{typeof(Tool)});
    }
    static bool Prefix(SObject __instance,Tool t,ref bool __result)
    {
        if(!ModEntry.IsPart(__instance))return true;
        // Native tool/controller paths used to be swallowed without routing a pickup.
        // Never allow vanilla chest debris/removal; use the same acknowledged transaction.
        var mod=ModEntry.Instance;
        if(StardewModdingAPI.Context.IsWorldReady && mod.Ready && !mod.Saving
            && t is (StardewValley.Tools.Axe or StardewValley.Tools.Pickaxe)
            && ReferenceEquals(t.getLastFarmerToUse(),Game1.player)
            && __instance.Location==Game1.player.currentLocation
            && mod.World.TryAnchor(__instance.Location,__instance.TileLocation,out var anchor)
            && !mod.Actions.HasPending) mod.World.Remove(anchor!);
        __result=false;return false;
    }
}
[HarmonyPatch(typeof(Chest),"HandleChestHit")]
internal static class RemoteHitProtectionPatch
{static bool Prefix(Chest __instance)=>!ModEntry.IsPart(__instance);}
[HarmonyPatch(typeof(SObject),"onExplosion")]
internal static class ExplosionProtectionPatch
{
    static bool Prefix(SObject __instance,ref bool __result)
    {if(!ModEntry.IsPart(__instance))return true;__result=false;return false;}
}
[HarmonyPatch(typeof(CraftingRecipe),"doesFarmerHaveIngredientsInInventory")]
internal static class IngredientCheckPatch
{
    static bool Prefix(CraftingRecipe __instance,ref bool __result)
    {if(!ModEntry.IsRecipe(__instance.name))return true;__result=CraftingSupport.Count(__instance.name==ModEntry.CrabId)>0;return false;}
}
[HarmonyPatch]
internal static class CraftableCountPatch
{
    static IEnumerable<MethodBase> TargetMethods()=>AccessTools.GetDeclaredMethods(typeof(CraftingRecipe)).Where(m=>m.Name=="getCraftableCount");
    static bool Prefix(CraftingRecipe __instance,ref int __result)
    {if(!ModEntry.IsRecipe(__instance.name))return true;__result=CraftingSupport.Count(__instance.name==ModEntry.CrabId);return false;}
}
[HarmonyPatch(typeof(CraftingPage),"clickCraftingRecipe")]
internal static class CraftClickPatch
{
    static bool Prefix(CraftingPage __instance,ClickableTextureComponent c,bool playSound)
    {
        var recipe=__instance.pagesOfCraftingRecipes[__instance.currentCraftingPage][c];
        if(!ModEntry.IsRecipe(recipe.name))return true;
        CraftingSupport.Craft(__instance,recipe,playSound);return false;
    }
}
[HarmonyPatch(typeof(CraftingRecipe),"drawMenuView")]
internal static class RecipeIconPatch
{
    static bool Prefix(CraftingRecipe __instance,SpriteBatch b,int x,int y,float layerDepth)
    {if(!ModEntry.IsRecipe(__instance.name))return true;ModEntry.Instance.DrawIcon(b,new(x,y+32),1,1,layerDepth,__instance.name==ModEntry.CrabId);return false;}
}
[HarmonyPatch(typeof(CraftingRecipe),"drawRecipeDescription")]
internal static class RecipeDescriptionPatch
{
    static bool Prefix(CraftingRecipe __instance,SpriteBatch b,Vector2 position,int width)
    {if(!ModEntry.IsRecipe(__instance.name))return true;CraftingSupport.DrawDescription(__instance,b,position,width);return false;}
}
[HarmonyPatch(typeof(CraftingRecipe),"getDescriptionHeight")]
internal static class RecipeDescriptionHeightPatch
{static void Postfix(CraftingRecipe __instance,ref int __result){if(ModEntry.IsRecipe(__instance.name))__result+=16;}}

// CraftingPage draws its own clickable components, not CraftingRecipe.drawMenuView.
[HarmonyPatch(typeof(CraftingPage), "layoutRecipes")]
internal static class CraftingPageMachineIconPatch
{
    static void Postfix(CraftingPage __instance)
    {
        foreach (var page in __instance.pagesOfCraftingRecipes)
            foreach (var pair in page)
            {
                if (!ModEntry.IsRecipe(pair.Value.name)) continue;
                pair.Key.texture = ModEntry.Instance.Art.MenuTextureFor(pair.Value.name==ModEntry.CrabId);
                pair.Key.sourceRect = new Rectangle(0, 0, 32, 16);
                pair.Key.scale = pair.Key.baseScale = 2f;
            }
    }
}

[HarmonyPatch(typeof(Chest),nameof(Chest.GetActualCapacity))]
internal static class MachineCapacityPatch
{
    static void Postfix(Chest __instance,ref int __result)
    {
        if(!ModEntry.IsMachine(__instance))return;
        __result=ModEntry.IsCrab(__instance)?Eddie.FishingMachine.Core.CrabRules.Capacity:Eddie.FishingMachine.Core.MachineUpgrade.Capacity(ModEntry.Instance.Production.Level(__instance));
        // Only construction of the native menu may expose the old overflow slots.
        // addItem, stack fill, production and automation always see the true new limit.
        if(ReferenceEquals(MachineOverflowMenuPatch.Opening,__instance)&&__instance.Items.Count>__result)
            __result=Math.Max(70,((__instance.Items.Count+4)/5)*5);
    }
}
[HarmonyPatch(typeof(Chest),nameof(Chest.ShowMenu))]
internal static class MachineOverflowMenuPatch
{
    [ThreadStatic] internal static Chest? Opening;
    static void Prefix(Chest __instance,out Chest? __state){__state=Opening;Opening=ModEntry.IsMachine(__instance)?__instance:null;}
    static Exception? Finalizer(Exception? __exception,Chest? __state){Opening=__state;return __exception;}
}
[HarmonyPatch(typeof(SObject),nameof(SObject.DisplayName),MethodType.Getter)]
internal static class MachineNamePatch
{
    static void Postfix(SObject __instance,ref string __result)
    {if(ModEntry.IsMachine(__instance) && ModEntry.Instance.Production.Level(__instance)>=2)__result=ModEntry.Instance.T(ModEntry.IsCrab(__instance)?"crab.lv2":ModEntry.Instance.Production.Level(__instance)==3?"machine.lv3":"machine.lv2");}
}
[HarmonyPatch(typeof(SObject),nameof(SObject.maximumStackSize))]
internal static class ProgressMachineStackPatch
{
    static void Postfix(SObject __instance,ref int __result)
    {if(ModEntry.IsMachine(__instance) && __instance.modData.ContainsKey(ProductionService.StateKey))__result=1;}
}

[HarmonyPatch(typeof(SObject),nameof(SObject.getDescription))]
internal static class MachineDescriptionPatch
{
    static void Postfix(SObject __instance,ref string __result)
    {
        if(ModEntry.IsMachine(__instance) && ModEntry.Instance.Production.Level(__instance)>=2)
            __result=Game1.parseText(ModEntry.Instance.T(ModEntry.IsCrab(__instance)?"crab.lv2-description":ModEntry.Instance.Production.Level(__instance)==3?"machine.lv3-description":"machine.lv2-description"),Game1.smallFont,320);
    }
}

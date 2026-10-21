using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace Eddie.FishingMachine;

/// <summary>The native level-5 profession branch omits its newCraftingRecipes list.</summary>
[HarmonyPatch(typeof(LevelUpMenu),nameof(LevelUpMenu.draw),new[]{typeof(SpriteBatch)})]
internal static class LevelUpRecipePatch
{
    static void Postfix(LevelUpMenu __instance,SpriteBatch b,int ___currentSkill,int ___currentLevel,
        int ___timerBeforeStart,List<int> ___professionsToChoose)
    {
        var mod=ModEntry.Instance;
        if(mod?.Ready!=true || ___currentSkill!=1 || ___currentLevel!=5
            || !__instance.isActive || !__instance.informationUp || !__instance.isProfessionChooser
            || ___timerBeforeStart>0 || ___professionsToChoose.Count==0)return;
        // Only a presentation repair: never grant/remove recipes, advance the menu,
        // change professions, or add another level-up screen. Old-save Unlock stays intact.
        string text=mod.T("machine.levelup-unlock");
        var size=Game1.smallFont.MeasureString(text);
        float scale=Math.Min(1f,Math.Max(1,__instance.width-160)/Math.Max(1,size.X+80));
        float left=__instance.xPositionOnScreen+(__instance.width-(size.X+80)*scale)/2;
        // Header: title and profession prompt above; native profession panels start at y+192.
        // Keep the added row wholly within the free y+132..180 header band.
        float centerY=__instance.yPositionOnScreen+156;
        mod.Art.DrawIcon(b,new Vector2(left-32+32*scale,centerY-32),scale,1,1);
        b.DrawString(Game1.smallFont,text,new Vector2(left+80*scale,centerY-size.Y*scale/2),
            Game1.textColor,0,Vector2.Zero,scale,SpriteEffects.None,1);
    }
}

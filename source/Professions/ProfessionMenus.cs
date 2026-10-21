using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace BetterStardewValley.Professions;

internal static class ProfessionMenus
{
    internal sealed class Page
    {
        internal Farmer Owner=null!;internal int Skill,Level;internal bool Confirmed;
    }
    private static ConditionalWeakTable<LevelUpMenu,Page> Pages=new();
    private static readonly FieldInfo Skill=AccessTools.Field(typeof(LevelUpMenu),"currentSkill");
    private static readonly FieldInfo Level=AccessTools.Field(typeof(LevelUpMenu),"currentLevel");
    private static readonly FieldInfo Recipes=AccessTools.Field(typeof(LevelUpMenu),"newCraftingRecipes");
    private static readonly FieldInfo Extra=AccessTools.Field(typeof(LevelUpMenu),"extraInfoForLevel");
    internal static void Clear()=>Pages=new();
    /// <summary>BSV machine recipes are unlocked by code, so the native page would not list them. Show them like native recipes.</summary>
    private static void AddMachineRecipes(LevelUpMenu menu)
    {
        if(menu.GetType()!=typeof(LevelUpMenu)||!menu.isActive||!menu.informationUp||menu.isProfessionChooser&&!ProfessionService.Active)return;
        int skill=(int)Skill.GetValue(menu)!,level=(int)Level.GetValue(menu)!;
        string? id=skill==Farmer.fishingSkill?level switch{5=>Eddie.FishingMachine.ModEntry.Id,6=>Eddie.FishingMachine.ModEntry.CrabId,_=>null}:null;
        if(id==null||!CraftingRecipe.craftingRecipes.ContainsKey(id))return;
        var recipes=(List<CraftingRecipe>)Recipes.GetValue(menu)!;
        if(recipes.Any(r=>r.name==id))return;
        var recipe=new CraftingRecipe(id,false);
        recipes.Add(recipe);
        if(!menu.isProfessionChooser)
        {
            menu.height+=recipe.bigCraftable?128:64;
            menu.gameWindowSizeChanged(Rectangle.Empty,Rectangle.Empty);
        }
    }
    internal static void Convert(LevelUpMenu __instance)
    {
        try{AddMachineRecipes(__instance);}catch(Exception ex){ProfessionService.Trace("BSV machine recipe could not be listed on the level-up page: "+ex.Message);}
        if(!ProfessionService.Active || __instance.GetType()!=typeof(LevelUpMenu) || !__instance.isActive
            || !__instance.informationUp || !__instance.isProfessionChooser)return;
        int skill=(int)Skill.GetValue(__instance)!,level=(int)Level.GetValue(__instance)!;
        // There is never a profession choice: the chooser becomes a normal level-up page.
        // A queued stage is granted on OK (native flow); a chooser opened without a queued
        // level (e.g. by another mod or a cheat) grants the stage immediately.
        bool queued=ProfessionGrant.CanConfirm(Game1.player,skill,level);
        if(!queued&&!(ProfessionGrant.Owns(Game1.player)&&ProfessionRules.Stage(skill,level)&&Game1.player.GetUnmodifiedSkillLevel(skill)>=level))return;
        var info=(List<string>)Extra.GetValue(__instance)!;
        // Native combat extra-info advertises +5 HP for ordinary levels. Profession pages
        // never grant that amount. Keep any unrelated mod-added info intact.
        if(skill==Farmer.combatSkill)
            info.Remove(Game1.content.LoadString("Strings\\UI:LevelUp_ExtraInfo_Combat"));
        // Like any other level: the page only lists what this level teaches (recipes); professions are granted silently.
        if(!queued){ProfessionGrant.Missing(Game1.player,skill,level);Game1.player.modData[ProfessionGrant.SettledKey(skill,level)]="1";}
        Pages.Remove(__instance);Pages.Add(__instance,new Page{Owner=Game1.player,Skill=skill,Level=level,Confirmed=!queued});
        __instance.isProfessionChooser=false;
        // Native confirmation, recipes, resize and controller navigation remain in use.
        __instance.leftProfession=null;__instance.rightProfession=null;
        __instance.height=256+((List<CraftingRecipe>)Recipes.GetValue(__instance)!).Sum(r=>r.bigCraftable?128:64)+info.Count*48;
        __instance.gameWindowSizeChanged(Rectangle.Empty,Rectangle.Empty);
        __instance.populateClickableComponentList();
        if(Game1.options.SnappyMenus)__instance.snapToDefaultClickableComponent();
    }
    internal static bool BeforeOk(LevelUpMenu __instance)
    {
        Convert(__instance);
        if(!Pages.TryGetValue(__instance,out var page))return true;
        if(ReferenceEquals(Game1.player,page.Owner) && (page.Confirmed
            || (!page.Owner.newLevels.Contains(new Point(page.Skill,page.Level))
                && page.Owner.modData.ContainsKey(ProfessionGrant.SettledKey(page.Skill,page.Level))
                && ProfessionRules.Expected(page.Skill,page.Level).All(page.Owner.professions.Contains))))
        {
            // A second menu may outlive the first confirmation. Retire only that already
            // settled page; never consume another pending level or run rewards again.
            __instance.isActive=false;__instance.informationUp=false;
            return false;
        }
        return !page.Confirmed && ReferenceEquals(Game1.player,page.Owner)
            && ProfessionGrant.CanConfirm(page.Owner,page.Skill,page.Level);
    }
    internal static bool Perk(LevelUpMenu __instance,int __0,int __1)
    {
        if(!Pages.TryGetValue(__instance,out var page) || page.Skill!=__0 || page.Level!=__1)return true;
        if(!page.Confirmed && ProfessionService.Enabled && ReferenceEquals(Game1.player,page.Owner))
        {
            int added=ProfessionGrant.Confirm(page.Owner,page.Skill,page.Level);
            if(added<0)throw new InvalidOperationException("BSV profession confirmation lost its pending level; original queue was not cleared.");
            page.Confirmed=true;
            ProfessionService.Trace($"Player {page.Owner.UniqueMultiplayerID}: confirmed {page.Skill}/{page.Level}, added {added} professions.");
        }
        return false;
    }
    internal static bool BeforeRemove(LevelUpMenu __instance) => !Pages.TryGetValue(__instance,out var page) || page.Confirmed;
    internal static void SkillHover(SkillsPage __instance,ref string ___hoverText,int ___professionImage)
    {
        if(!ProfessionService.Active || __instance.GetType()!=typeof(SkillsPage) || ___professionImage<0)return;
        var skill=ProfessionRules.Skills.FirstOrDefault(s=>s.All.Contains(___professionImage));
        if(skill==null)return;
        var names=skill.All.Where(Game1.player.professions.Contains).Select(LevelUpMenu.getProfessionTitleFromNumber).ToArray();
        if(names.Length<2)return;
        ___hoverText+="\n\n"+ProfessionService.Text("professions.owned")+"\n"+string.Join("\n",names);
    }
}

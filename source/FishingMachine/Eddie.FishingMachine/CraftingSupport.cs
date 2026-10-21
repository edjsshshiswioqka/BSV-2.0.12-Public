using Eddie.FishingMachine.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
namespace Eddie.FishingMachine;
internal static class CraftingSupport
{
    internal static IngredientSlot[] Slots()=>Game1.player.Items.Select((i,n)=>new IngredientSlot(n,i?.QualifiedItemId??"",i is StardewValley.Object {questItem.Value:true} || Game1.player.Items.Count(j=>ReferenceEquals(i,j))!=1?0:i?.Stack??0,
        i is FishingRod && !ReferenceEquals(i,Game1.player.CurrentTool) && ProductionService.SafeRod(i) && Game1.player.Items.Count(j=>ReferenceEquals(i,j))==1)).ToArray();
    internal static bool Trapper=>Game1.player.professions.Contains(7);
    internal static (string Id,int Count)[] Requirements(bool crab)=>crab?CrabRules.Craft(Trapper):PreparationRules.Requirements;
    internal static int Count(bool crab=false)
    {
        if(!crab)return PreparationRules.Craftable(Game1.player.fishingLevel.Value,Slots());
        if(Game1.player.fishingLevel.Value<6)return 0;
        return Requirements(true).Min(r=>Game1.player.Items.Where(i=>i?.QualifiedItemId==r.Id && i is StardewValley.Object o && !o.questItem.Value).Sum(i=>i.Stack)/r.Count);
    }
    internal static bool Craft(CraftingPage page,CraftingRecipe recipe,bool sound)
    {
        bool crab=recipe.name==ModEntry.CrabId;
        var crabPlan=crab && Game1.player.fishingLevel.Value>=6?CrabRules.Plan(Game1.player.Items.Select((i,n)=>(i,n))
            .Where(x=>x.i is StardewValley.Object o && !o.questItem.Value && x.i.Stack>0 && Game1.player.Items.Count(j=>ReferenceEquals(x.i,j))==1)
            .Select(x=>new UpgradeSlot(x.n,x.i.QualifiedItemId,x.i.Stack)),Requirements(true)):null;
        var plan=crab?crabPlan?.Select(x=>new IngredientSpend(x.Index,x.Count)).ToArray():PreparationRules.PlanCraft(Game1.player.fishingLevel.Value,Slots());
        if(plan==null){ModEntry.Instance.Message("recipe.materials");return false;}
        string resultId=crab?ModEntry.CrabQualifiedId:ModEntry.QualifiedId;
        if(ItemRegistry.GetData(resultId)==null)return false;
        var item=ItemRegistry.Create(resultId);
        if(page.heldItem!=null && (!page.heldItem.canStackWith(item) || page.heldItem.Stack>=page.heldItem.maximumStackSize()))return false;
        // Validate all materials before mutation; only player backpack, no unprotected remote chests.
        foreach(var spend in plan)
        {
            var existing=Game1.player.Items[spend.Index];
            if(existing==null || existing.Stack<spend.Count)return false;
        }
        var before=Game1.player.Items.ToArray();var stacks=before.Select(i=>i?.Stack??0).ToArray();
        var held=page.heldItem;int heldCount=held?.Stack??0;int crafted=Game1.player.craftingRecipes.GetValueOrDefault(recipe.name);
        try
        {
            foreach(var spend in plan)
            {
                var existing=Game1.player.Items[spend.Index];
                if(existing.Stack==spend.Count)Game1.player.Items[spend.Index]=null;
                else existing.Stack-=spend.Count;
            }
            if(page.heldItem==null)page.heldItem=item;else page.heldItem.Stack++;
            Game1.player.craftingRecipes[recipe.name]=checked(crafted+1);
        }
        catch
        {
            Game1.player.Items.Clear();for(int i=0;i<before.Length;i++){if(before[i]!=null)before[i].Stack=stacks[i];Game1.player.Items.Add(before[i]);}
            page.heldItem=held;if(held!=null)held.Stack=heldCount;Game1.player.craftingRecipes[recipe.name]=crafted;throw;
        }
        Game1.player.NotifyQuests(q=>q.OnRecipeCrafted(recipe,item));
        Game1.stats.checkForCraftingAchievements();
        if(sound)Game1.playSound("coin");
        if(Game1.options.gamepadControls && Game1.player.couldInventoryAcceptThisItem(page.heldItem))
        {Game1.player.addItemToInventoryBool(page.heldItem);page.heldItem=null;}
        return true;
    }
    internal static void DrawDescription(CraftingRecipe recipe,SpriteBatch b,Vector2 p,int width)
    {
        var mod=ModEntry.Instance;var slots=Slots();
        Utility.drawTextWithShadow(b,mod.T("recipe.ingredients"),Game1.smallFont,p+new Vector2(8,28),Game1.textColor);
        var requirements=Requirements(recipe.name==ModEntry.CrabId);
        for(int row=0;row<requirements.Length;row++)
        {
            var (id,needed)=requirements[row];
            int count=id==PreparationRules.FiberglassRod?slots.Count(x=>x.Id==id && x.SafeTool && x.Count==1):slots.Where(x=>x.Id==id).Sum(x=>x.Count);
            var data=ItemRegistry.GetDataOrErrorItem(id);
            var source=data.GetSourceRect();
            var pos=p+new Vector2(16,80+row*36);
            b.Draw(data.GetTexture(),pos,source,Color.White,0,new Vector2(source.Width/2f,source.Height/2f),32f/Math.Max(source.Width,source.Height),SpriteEffects.None,.86f);
            string label=$"{data.DisplayName} ×{needed}";
            if(Game1.options.showAdvancedCraftingInformation)label+=$" ({count})";
            Utility.drawTextWithShadow(b,label,Game1.smallFont,p+new Vector2(40,68+row*36),count>=needed?Game1.textColor:Color.Red);
        }
        Utility.drawTextWithShadow(b,Game1.parseText(recipe.description,Game1.smallFont,width-8),Game1.smallFont,p+new Vector2(0,76+requirements.Length*36),Game1.textColor*.75f);
    }
}

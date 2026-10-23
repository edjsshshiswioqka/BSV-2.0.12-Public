using Eddie.FishingMachine.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Tools;
namespace Eddie.FishingMachine;

// Shared vanilla-style information panel for the Crab Catcher and the Fishing Machine.
// It is drawn beside the native chest menu (never over the item grids). When the window has no room for it,
// the previous compact overlay is used unchanged, so nothing is ever drawn over the inventory.
internal sealed partial class ProductionService
{
    private const int PanelWidth=300,PanelMargin=12,PanelInner=24,CardWidth=46,CardGap=4,CardHeight=52;

    private sealed class PanelGeometry
    {
        public Rectangle Frame,Title,Badge,Bar,Stats,Take,UpgradeTitle,Cycles,Button,Note;
        public Rectangle[] Cards=Array.Empty<Rectangle>();
    }

    private static bool PanelLogged;
    internal void PanelFailed(Exception ex)
    {
        if(PanelLogged)return;PanelLogged=true;
        Mod.Monitor.Log("BSV machine panel failed; compact overlay used instead.\n"+ex,LogLevel.Warn);
    }

    internal static bool IsCollectible(Item? i)
        => i!=null && i.Stack>0 && !CrabBaitItem(i)
           && i.QualifiedItemId is not ("(O)685" or "(O)ChallengeBait" or "(O)DeluxeBait" or "(O)SpecificBait" or GiantLobsterRules.Bait);

    private static PanelGeometry? PanelFor(ItemGrabMenu menu,bool upgrade,int cards,bool button)
    {
        var grid=menu.ItemsToGrabMenu;var bag=menu.inventory;
        int left=Math.Min(grid.xPositionOnScreen,bag.xPositionOnScreen)-32;
        int right=Math.Max(grid.xPositionOnScreen+grid.width,bag.xPositionOnScreen+bag.width)+32;
        if(menu.okButton!=null)right=Math.Max(right,menu.okButton.bounds.Right+8);
        if(menu.trashCan!=null)right=Math.Max(right,menu.trashCan.bounds.Right+8);
        int viewportW=Game1.uiViewport.Width,viewportH=Game1.uiViewport.Height;
        int x;
        if(right+PanelMargin+PanelWidth<=viewportW-8)x=right+PanelMargin;
        else if(left-PanelMargin-PanelWidth>=8)x=left-PanelMargin-PanelWidth;
        else return null;
        int inner=PanelWidth-PanelInner*2,x0=x+PanelInner;
        int h=PanelInner+98+40+(upgrade?12+26+26+CardHeight+12+40:12+40)+PanelInner;
        if(h>viewportH-16)return null;
        int y=Math.Max(8,Math.Min(grid.yPositionOnScreen-PanelInner,viewportH-8-h));
        int y0=y+PanelInner;
        var g=new PanelGeometry{Frame=new(x,y,PanelWidth,h)};
        g.Badge=new(x0+inner-64,y0,64,30);
        g.Title=new(x0,y0,inner-72,30);
        g.Bar=new(x0,y0+38,inner,22);
        g.Stats=new(x0,y0+66,inner,26);
        g.Take=new(x0,y0+98,inner,40);
        int cursor=y0+98+40+12;
        if(upgrade)
        {
            g.UpgradeTitle=new(x0,cursor,inner,26);cursor+=26;
            g.Cycles=new(x0,cursor,inner,26);cursor+=26;
            g.Cards=Enumerable.Range(0,cards).Select(n=>new Rectangle(x0+n*(CardWidth+CardGap),cursor,CardWidth,CardHeight)).ToArray();
            cursor+=CardHeight+12;
            if(button)g.Button=new(x0,cursor,inner,40);else g.Note=new(x0,cursor,inner,40);
        }
        else g.Note=new(x0,cursor,inner,40);
        return g;
    }

    private (int Have,int Need,string Id)[] PanelMaterials(Chest c,ProductionState s)
    {
        var need=ModEntry.IsCrab(c)?(s.Level==1?CrabRules.Upgrade(false):Array.Empty<(string Id,int Count)>())
            :(s.Level<3?MachineUpgrade.Requirements(s.Level):Array.Empty<(string Id,int Count)>());
        return need.Select(r=>(
            Have:(int)Math.Min(int.MaxValue,c.Items.Where(i=>i!=null&&i.QualifiedItemId==r.Id&&(!r.Id.StartsWith("(T)")||SafeRod(i))
                &&!(i is StardewValley.Object o&&o.questItem.Value)).Sum(i=>(long)i.Stack)),
            Need:r.Count,Id:r.Id)).ToArray();
    }

    private bool PanelModel(Chest c,ItemGrabMenu menu,out ProductionState s,out PanelGeometry? g,out (int Have,int Need,string Id)[] materials)
    {
        s=null!;g=null;materials=Array.Empty<(int,int,string)>();
        try{s=Read(c);}catch{return false;}
        materials=PanelMaterials(c,s);
        bool crab=ModEntry.IsCrab(c);
        bool upgrade=crab?s.Level==1:s.Level<3;
        g=PanelFor(menu,upgrade,materials.Length,crab);
        return g!=null;
    }

    private static void PanelText(SpriteBatch b,string text,Vector2 at,Color color,float maxWidth,float scale=.8f)
    {
        var size=Game1.smallFont.MeasureString(text);
        float fit=Math.Min(scale,maxWidth/Math.Max(1f,size.X));
        b.DrawString(Game1.smallFont,text,at,color,0f,Vector2.Zero,fit,SpriteEffects.None,1f);
    }

    private static void PanelWrapped(SpriteBatch b,string text,Rectangle box,Color color,float scale=.62f)
    {
        string wrapped=Game1.parseText(text,Game1.smallFont,(int)(box.Width/scale));
        b.DrawString(Game1.smallFont,wrapped,new Vector2(box.X,box.Y),color,0f,Vector2.Zero,scale,SpriteEffects.None,1f);
    }

    private static void PanelOutline(SpriteBatch b,Rectangle r,Color color,int t=2)
    {
        b.Draw(Game1.staminaRect,new Rectangle(r.X,r.Y,r.Width,t),color);
        b.Draw(Game1.staminaRect,new Rectangle(r.X,r.Bottom-t,r.Width,t),color);
        b.Draw(Game1.staminaRect,new Rectangle(r.X,r.Y,t,r.Height),color);
        b.Draw(Game1.staminaRect,new Rectangle(r.Right-t,r.Y,t,r.Height),color);
    }

    private static void PanelBox(SpriteBatch b,Rectangle r,Color tint)
        => IClickableMenu.drawTextureBox(b,Game1.menuTexture,new Rectangle(0,256,60,60),r.X,r.Y,r.Width,r.Height,tint,1f,false);

    private bool TryDrawPanel(SpriteBatch b,Chest c,ItemGrabMenu menu)
    {
        if(!PanelModel(c,menu,out var s,out var g,out var materials)||g==null)return false;
        bool crab=ModEntry.IsCrab(c);
        int mx=Game1.getMouseX(),my=Game1.getMouseY();
        string hover="";
        Game1.drawDialogueBox(g.Frame.X,g.Frame.Y,g.Frame.Width,g.Frame.Height,false,true,null,false);

        // Header: name and level badge.
        string name=crab?Mod.T("crab.name"):Mod.T("machine.name");
        PanelText(b,name,new Vector2(g.Title.X,g.Title.Y+2),Game1.textColor,g.Title.Width,.85f);
        var badgeTint=s.Level>=3?new Color(190,140,240):s.Level==2?new Color(255,215,90):Color.White;
        PanelBox(b,g.Badge,badgeTint);
        string badge=Mod.T("panel.level-badge",new{level=s.Level});
        var bs=Game1.smallFont.MeasureString(badge)*.8f;
        b.DrawString(Game1.smallFont,badge,new Vector2(g.Badge.X+(g.Badge.Width-bs.X)/2,g.Badge.Y+(g.Badge.Height-bs.Y)/2),Game1.textColor,0f,Vector2.Zero,.8f,SpriteEffects.None,1f);
        if(!crab&&g.Badge.Contains(mx,my))hover=FishingBalanceTip(s);

        // Production progress.
        PanelBox(b,g.Bar,Color.White);
        var track=new Rectangle(g.Bar.X+5,g.Bar.Y+5,g.Bar.Width-10,g.Bar.Height-10);
        b.Draw(Game1.staminaRect,track,new Color(70,48,28));
        double fraction=0;string barText;
        bool paused=s.Phase=="working"&&s.Timeline?.PausedReason!=null;
        if(PreparingRecovery(c))barText=Mod.T(s.Phase=="working"?"remove.status-finishing":"remove.status-empty");
        else if(s.Phase=="working")
        {
            long total=Math.Max(1,s.DueAt-s.StartedAt),left=Math.Max(0,s.DueAt-Now);
            fraction=Math.Clamp(1.0-(double)left/total,0,1);
            barText=paused?Mod.T("status.timeline-paused",new{reason=Mod.T(MachinePauseText.KeyFor(s.Timeline!.PausedReason))})
                :Mod.T("panel.time-left",new{hours=left/60,minutes=left%60});
        }
        else if(s.Phase=="ready"){fraction=1;barText=s.Pending.Count>0?Mod.T("status.full"):Mod.T("panel.ready");}
        else barText=Mod.T("panel.idle");
        if(fraction>0)b.Draw(Game1.staminaRect,new Rectangle(track.X,track.Y,(int)(track.Width*fraction),track.Height),paused?new Color(200,150,60):new Color(95,194,180));
        PanelText(b,barText,new Vector2(track.X+4,track.Y-3),Color.White,track.Width-8,.55f);
        if(g.Bar.Contains(mx,my))hover=Status(c);

        // Cycles and item counts.
        string stats=Mod.T("panel.cycles-done",new{count=s.CompletedCycles})+" · "+Mod.T("panel.items",new{count=TakeCandidates(c,s).Sum(i=>(long)i.Stack)});
        PanelText(b,stats,new Vector2(g.Stats.X,g.Stats.Y+2),Game1.textColor,g.Stats.Width,.7f);

        // Take all.
        bool anything=TakeCandidates(c,s).Any();
        PanelBox(b,g.Take,anything?Color.White:new Color(190,180,170));
        string take=Mod.T("panel.take-all");
        var ts=Game1.smallFont.MeasureString(take)*.8f;
        b.DrawString(Game1.smallFont,take,new Vector2(g.Take.X+(g.Take.Width-ts.X)/2,g.Take.Y+(g.Take.Height-ts.Y)/2),anything?Game1.textColor:Game1.unselectedOptionColor,0f,Vector2.Zero,.8f,SpriteEffects.None,1f);
        if(g.Take.Contains(mx,my))PanelOutline(b,g.Take,Color.Gold);

        // Upgrade section.
        if(g.UpgradeTitle.Width>0)
        {
            PanelText(b,Mod.T("panel.upgrade-title",new{level=s.Level+1}),new Vector2(g.UpgradeTitle.X,g.UpgradeTitle.Y+2),Game1.textColor,g.UpgradeTitle.Width,.8f);
            long progress=MachineUpgrade.Progress(s);int needed=MachineCycleRules.RequiredCycles(s);
            bool cyclesMet=!s.StageProgressUnverified&&progress>=needed;
            b.Draw(Game1.mouseCursors,new Rectangle(g.Cycles.X,g.Cycles.Y,22,22),new Rectangle(128,256,64,64),cyclesMet?Color.White:Color.White*.25f);
            PanelText(b,Mod.T("panel.cycles-req",new{count=s.StageProgressUnverified?"?":progress.ToString(),needed}),new Vector2(g.Cycles.X+28,g.Cycles.Y+2),
                cyclesMet?new Color(30,120,60):Game1.textColor,g.Cycles.Width-28,.75f);
            for(int n=0;n<g.Cards.Length&&n<materials.Length;n++)
            {
                var card=g.Cards[n];var m=materials[n];bool met=m.Have>=m.Need;
                PanelBox(b,new Rectangle(card.X,card.Y,card.Width,card.Height),met?Color.White:new Color(255,215,205));
                var data=ItemRegistry.GetDataOrErrorItem(m.Id);
                b.Draw(data.GetTexture(),new Rectangle(card.X+7,card.Y+4,32,32),data.GetSourceRect(),Color.White);
                PanelText(b,m.Have+"/"+m.Need,new Vector2(card.X+3,card.Y+card.Height-20),met?new Color(30,120,60):new Color(190,30,30),card.Width-6,.55f);
                if(card.Contains(mx,my))hover=data.DisplayName;
            }
            if(g.Button.Width>0)
            {
                PanelBox(b,g.Button,Color.White);
                bool ready=Upgradable(c,s,materials);
                if(ready)PanelOutline(b,g.Button,new Color(60,170,90));
                string label=Mod.T("panel.upgrade-button",new{level=s.Level+1});
                float fit=Math.Min(.8f,(g.Button.Width-16)/Math.Max(1f,Game1.smallFont.MeasureString(label).X));
                var size=Game1.smallFont.MeasureString(label)*fit;
                b.DrawString(Game1.smallFont,label,new Vector2(g.Button.X+(g.Button.Width-size.X)/2,g.Button.Y+(g.Button.Height-size.Y)/2),Game1.textColor,0f,Vector2.Zero,fit,SpriteEffects.None,1f);
                if(g.Button.Contains(mx,my))PanelOutline(b,g.Button,Color.Gold);
            }
            else PanelWrapped(b,Mod.T("panel.auto-upgrade"),g.Note,Game1.textColor);
        }
        else PanelWrapped(b,Mod.T("panel.max-level"),g.Note,Game1.textColor);

        if(hover.Length>0)IClickableMenu.drawHoverText(b,hover,Game1.smallFont);
        return true;
    }

    private string FishingBalanceTip(ProductionState s)
    {
        var config=Mod.Config;int level=s.Level;
        double twice=level==1?config.FishCriticalChance:level==2?config.LevelTwoCriticalChance:config.LevelThreeCriticalChance;
        double triple=level==1?0:level==2?config.LevelTwoTripleChance:config.LevelThreeTripleChance;
        string P(double p)=>(p*100).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"%";
        var q=MachineLevelRules.QualityShares(level);var t=MachineLevelRules.TreasureShares(level);
        string tip=Mod.T("machine.balance",new{capacity=MachineUpgrade.Capacity(level),mult=string.Join(" / ",new[]{1-twice-triple,twice,triple}.Select(P)),
            quality=string.Join(" / ",q.Select(P)),treasure=string.Join(" / ",t.Select(P)),legend=P(CycleLegendaryRules.Chance(level))})+"\n"+Mod.T(level>=2?"machine.no-trash":"machine.trash-lv1");
        if(s.Phase=="ready"||s.LastNormalTreasures.HasValue)
            tip+="\n"+Mod.T("status.treasures",new{normal=s.Phase=="ready"?s.NormalTreasures:s.LastNormalTreasures??0,advanced=s.Phase=="ready"?s.AdvancedTreasures:s.LastAdvancedTreasures??0});
        return tip;
    }

    private bool Upgradable(Chest c,ProductionState s,(int Have,int Need,string Id)[] materials)
        => s.Level==1&&!s.StageProgressUnverified&&s.Phase=="idle"&&MachineUpgrade.Progress(s)>=MachineCycleRules.RequiredCycles(s)&&materials.All(m=>m.Have>=m.Need);

    // Items "Take all" may move: catch only. Bait and (while an upgrade is still possible) upgrade materials stay in the machine.
    private IEnumerable<Item> TakeCandidates(Chest c,ProductionState s)
    {
        bool crab=ModEntry.IsCrab(c);
        return c.Items.Where(i=>IsCollectible(i)
            &&!(crab?s.Level==1&&CrabRules.IsUpgradeMaterial(i!.QualifiedItemId):s.Level<3&&MachineUpgrade.IsMaterial(i!.QualifiedItemId,s.Level))
            &&!(i is FishingRod)).Cast<Item>().ToList();
    }

    // Only runs on the client that holds the chest lock, exactly like a normal drag out of a chest.
    private void TakeAll(Chest c,ProductionState s)
    {
        var items=TakeCandidates(c,s).ToList();
        if(items.Count==0){Game1.showRedMessage(Mod.T("panel.take-none"));return;}
        bool left=false,moved=false;
        foreach(var item in items)
        {
            int before=item.Stack;
            var rest=Game1.player.addItemToInventory(item);
            if(rest==null){c.Items.Remove(item);moved=true;}
            else{left=true;if(rest.Stack<before)moved=true;item.Stack=rest.Stack;}
        }
        c.clearNulls();
        if(left)Game1.showRedMessage(Mod.T("panel.take-full"));
        else if(moved)Game1.playSound("coin");
    }

    private void UpgradeFeedback(Chest c,ProductionState s,(int Have,int Need,string Id)[] materials)
    {
        // Matches the host check in CrabControl: only an idle machine can upgrade (K09).
        if(s.Phase!="idle"){Mod.Message("crab.upgrade-unavailable");return;}
        var lacking=new List<string>();
        long progress=MachineUpgrade.Progress(s);int needed=MachineCycleRules.RequiredCycles(s);
        if(progress<needed)lacking.Add(Mod.T("panel.cycles-req",new{count=progress,needed}));
        foreach(var m in materials.Where(m=>m.Have<m.Need))
            lacking.Add(ItemRegistry.GetDataOrErrorItem(m.Id).DisplayName+" "+m.Have+"/"+m.Need);
        Game1.showRedMessage(Mod.T("panel.missing",new{list=string.Join("; ",lacking)}));
    }

    // Handles clicks and hotkeys for the side panel. Returns false when the compact layout is active.
    private bool PanelButton(Chest c,ItemGrabMenu menu,SButton button)
    {
        bool click=button==SButton.MouseLeft;
        if(!click&&button is not (SButton.T or SButton.ControllerX or SButton.G))return false;
        bool crab=ModEntry.IsCrab(c);
        ProductionState s;(int Have,int Need,string Id)[] materials;Rectangle takeBox,buttonBox;
        if(ReferenceEquals(FullMenu,menu)&&FullGeometry is {} full)
        {
            try{s=Read(c);}catch{return false;}
            materials=PanelMaterials(c,s);takeBox=full.Take;buttonBox=full.Button;
        }
        else
        {
            if(!PanelModel(c,menu,out s,out var g,out materials)||g==null)return false;
            takeBox=g.Take;buttonBox=g.Button;
        }
        bool take=button==SButton.G,upgrade=button is SButton.T or SButton.ControllerX;
        if(click)
        {
            Game1.PushUIMode();
            try
            {
                int x=Game1.getMouseX(),y=Game1.getMouseY();
                if(!crab&&ReferenceEquals(FullMenu,menu)&&FullGeometry is {} power&&power.Power.Contains(x,y))
                {try{TogglePower(c);}catch{Mod.Message("status.error");}return true;}
                take=takeBox.Contains(x,y);
                upgrade=buttonBox.Width>0&&buttonBox.Contains(x,y);
            }
            finally{Game1.PopUIMode();}
            if(!take&&!upgrade)return false;
        }
        if(take){try{TakeAll(c,s);}catch{Mod.Message("status.error");}return true;}
        if(buttonBox.Width==0)return false;
        if(!crab)
        {
            // The fishing machine upgrades by itself; its bar (and T) only says what is still missing.
            var lacking=Lacking(s,materials);
            if(lacking.Count>0)Game1.showRedMessage(Mod.T("panel.missing",new{list=string.Join("; ",lacking)}));
            else Game1.showGlobalMessage(Mod.T("panel.auto-upgrade"));
            return true;
        }
        if(!Upgradable(c,s,materials)){UpgradeFeedback(c,s,materials);return true;}
        Mod.Actions.Control(c,ActionKind.Upgrade);Mod.ReleaseChest(startCrab:false);Game1.exitActiveMenu();return true;
    }
}

using System.Runtime.CompilerServices;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
namespace Eddie.FishingMachine;

/// <summary>
/// Draws BSV machine menus as one vanilla-style window (approved mockup):
/// header (name, level, progress, totals) → "Catch" + Take all → machine grid → upgrade section → "Backpack" → player grid.
/// The native ItemGrabMenu keeps all item logic; only its components are moved and the frame is drawn by BSV.
/// Any other chest menu is untouched. If the window does not fit the screen the native menu is drawn instead.
/// </summary>
[HarmonyPatch(typeof(ItemGrabMenu),nameof(ItemGrabMenu.draw),new[]{typeof(SpriteBatch)})]
internal static class MachineMenuDrawPatch
{
    static bool Prefix(ItemGrabMenu __instance,SpriteBatch b)
    {
        var mod=ModEntry.Instance;
        if(mod?.Ready!=true)return true;
        try{return !mod.Production.DrawMachineMenu(__instance,b);}
        catch(Exception ex){mod.Production.PanelFailed(ex);mod.Production.FullMenu=null;return true;}
    }
}

internal sealed partial class ProductionService
{
    internal sealed class FullLayout
    {
        public Rectangle Window,Title,Badge,Status,Bar,Stats,CatchLabel,Take,Upgrade,UpgradeTitle,Button,Note,BackpackLabel;
        public Rectangle[] Conditions=Array.Empty<Rectangle>();
        public Point Grid,Bag;
        public bool Labels;
    }
    /// <summary>The machine menu drawn with the full layout in the current frame (null when the fallback is used).</summary>
    internal ItemGrabMenu? FullMenu;
    internal FullLayout? FullGeometry;
    private static readonly ConditionalWeakTable<ItemGrabMenu,object> Prepared=new();
    private static readonly Color Good=new(36,122,58),Bad=new(186,36,36),Muted=new(120,98,72),BarBack=new(92,62,36),BarFill=new(95,194,180),BarPaused=new(214,160,64);

    private static FullLayout? Arrange(ItemGrabMenu menu,bool crab,ProductionState s,int conditions)
    {
        var grid=menu.ItemsToGrabMenu;var bag=menu.inventory;
        if(grid==null||bag==null)return null;
        int vw=Game1.uiViewport.Width,vh=Game1.uiViewport.Height;
        int inner=Math.Max(grid.width,bag.width),width=inner+64;
        bool upgrade=crab?s.Level==1:s.Level<3;
        int condRows=upgrade?(conditions+1)/2:0;
        int upgradeHeight=upgrade?44+condRows*34+6+(crab?60:40):0;
        int Height(bool labels)=>24+48+52+32+12+(labels?48:0)+grid.height+16+(upgrade?upgradeHeight+12:0)+(labels?40:0)+bag.height+28;
        bool withLabels=true;int height=Height(true);
        if(height>vh-16){withLabels=false;height=Height(false);}
        // The side buttons (organize, OK, trash) sit right of the window.
        if(height>vh-16||width+2*(64+16)>vw-16)return null;
        int x=(vw-width)/2,y=(vh-height)/2,cx=x+32,cw=inner,cy=y+24;
        var g=new FullLayout{Window=new(x,y,width,height),Labels=withLabels};
        g.Take=new(cx+cw-200,withLabels?0:cy+4,200,44);
        g.Badge=new(cx,cy,0,0);
        g.Title=new(cx,cy,withLabels?cw:cw-212,48);cy+=48;
        g.Status=new(cx,cy,cw,28);g.Bar=new(cx,cy+30,cw,18);cy+=52;
        g.Stats=new(cx,cy,cw,32);cy+=32+12;
        if(withLabels){g.CatchLabel=new(cx,cy,cw-212,48);g.Take.Y=cy+2;cy+=48;}
        g.Grid=new(x+(width-grid.width)/2,cy);cy+=grid.height+16;
        if(upgrade)
        {
            g.Upgrade=new(cx-12,cy-6,cw+24,upgradeHeight);
            g.UpgradeTitle=new(cx,cy,cw,40);cy+=44;
            int column=(cw-24)/2;
            g.Conditions=Enumerable.Range(0,conditions).Select(n=>new Rectangle(cx+n%2*(column+24),cy+n/2*34,column,30)).ToArray();
            cy+=condRows*34+6;
            if(crab){g.Button=new(cx+cw-320,cy,320,52);cy+=60;}else{g.Note=new(cx,cy,cw,36);cy+=40;}
            cy+=12;
        }
        if(withLabels){g.BackpackLabel=new(cx,cy,cw,40);cy+=40;}
        g.Bag=new(x+(width-bag.width)/2,cy);
        return g;
    }

    private static void MoveInventory(InventoryMenu m,Point to)
    {
        int dx=to.X-m.xPositionOnScreen,dy=to.Y-m.yPositionOnScreen;
        if(dx==0&&dy==0)return;
        m.xPositionOnScreen=to.X;m.yPositionOnScreen=to.Y;
        foreach(var slot in m.inventory)slot.bounds.Offset(dx,dy);
    }
    private static void Place(ClickableComponent? c,int x,int y){if(c!=null)c.bounds=new Rectangle(x,y,c.bounds.Width,c.bounds.Height);}

    /// <summary>Hide the colour picker once: machines have no colour, and its strip would overlap the header.</summary>
    private static void Prepare(ItemGrabMenu menu)
    {
        if(Prepared.TryGetValue(menu,out _))return;
        Prepared.Add(menu,new object());
        if(menu.chestColorPicker!=null)menu.chestColorPicker.visible=false;
        var offscreen=new Rectangle(-10000,-10000,1,1);
        if(menu.colorPickerToggleButton!=null)menu.colorPickerToggleButton.bounds=offscreen;
        if(AccessTools.Field(typeof(ItemGrabMenu),"discreteColorPickerCC")?.GetValue(menu) is List<ClickableComponent> picker)
            foreach(var cc in picker)cc.bounds=offscreen;
    }

    private static void Apply(ItemGrabMenu menu,FullLayout g)
    {
        MoveInventory(menu.ItemsToGrabMenu,g.Grid);
        MoveInventory(menu.inventory,g.Bag);
        int side=g.Window.Right+16;
        Place(menu.fillStacksButton,side,g.Grid.Y);
        Place(menu.organizeButton,side,g.Grid.Y+80);
        Place(menu.specialButton,side,g.Grid.Y+160);
        Place(menu.junimoNoteIcon,side,g.Grid.Y+240);
        Place(menu.trashCan,side,g.Bag.Y-8);
        Place(menu.okButton,side,g.Bag.Y+menu.inventory.height-64);
        // Clicks anywhere inside the window count as inside the menu (no accidental item drops).
        menu.xPositionOnScreen=g.Window.X;menu.yPositionOnScreen=g.Window.Y;
        menu.width=g.Window.Width;menu.height=g.Window.Height;
    }

    /// <summary>Draws the whole machine menu. Returns false when the native draw should run instead.</summary>
    internal bool DrawMachineMenu(ItemGrabMenu menu,SpriteBatch b)
    {
        if(!CurrentMenu(out var c,out var current)||!ReferenceEquals(current,menu)){if(ReferenceEquals(FullMenu,menu))FullMenu=null;return false;}
        ProductionState s;
        try{s=Read(c);}catch{FullMenu=null;return false;}
        bool crab=ModEntry.IsCrab(c);
        var materials=PanelMaterials(c,s);
        var g=Arrange(menu,crab,s,materials.Length+1);
        if(g==null){FullMenu=null;FullGeometry=null;return false;}
        Prepare(menu);Apply(menu,g);
        FullMenu=menu;FullGeometry=g;

        int mx=Game1.getMouseX(),my=Game1.getMouseY();string tip="";
        if(menu.drawBG&&!Game1.options.showClearBackgrounds)
            b.Draw(Game1.fadeToBlackRect,new Rectangle(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height),Color.Black*.5f);
        IClickableMenu.drawTextureBox(b,g.Window.X,g.Window.Y,g.Window.Width,g.Window.Height,Color.White);

        // Header: name + level badge.
        string name=Mod.T(crab?"crab.name":"machine.name");
        float titleScale=Math.Min(1f,(g.Title.Width-110)/Math.Max(1f,Game1.dialogueFont.MeasureString(name).X));
        Utility.drawTextWithShadow(b,name,Game1.dialogueFont,new Vector2(g.Title.X,g.Title.Y),Game1.textColor,titleScale);
        string badge=Mod.T("panel.level-badge",new{level=s.Level});
        var badgeSize=Game1.smallFont.MeasureString(badge);
        g.Badge=new Rectangle(g.Title.X+(int)(Game1.dialogueFont.MeasureString(name).X*titleScale)+16,g.Title.Y+6,(int)badgeSize.X+28,40);
        IClickableMenu.drawTextureBox(b,Game1.mouseCursors,new Rectangle(403,383,6,6),g.Badge.X,g.Badge.Y,g.Badge.Width,g.Badge.Height,
            s.Level>=3?new Color(205,170,255):s.Level==2?new Color(255,225,120):Color.White,4f,false);
        b.DrawString(Game1.smallFont,badge,new Vector2(g.Badge.X+14,g.Badge.Y+(g.Badge.Height-badgeSize.Y)/2+2),Game1.textColor);
        if(!crab&&g.Badge.Contains(mx,my))tip=FishingBalanceTip(s);

        // Production state, time left and progress bar.
        bool paused=s.Phase=="working"&&s.Timeline?.PausedReason!=null;double fraction=0;string phase,right="";
        if(PreparingRecovery(c))phase=Mod.T(s.Phase=="working"?"remove.status-finishing":"remove.status-empty");
        else if(s.Phase=="working")
        {
            long total=Math.Max(1,s.DueAt-s.StartedAt),left=Math.Max(0,s.DueAt-Now);
            fraction=Math.Clamp(1.0-(double)left/total,0,1);
            phase=paused?Mod.T("status.timeline-paused",new{reason=Mod.T(MachinePauseText.KeyFor(s.Timeline!.PausedReason))}):Mod.T("panel.producing");
            if(!paused)right=Mod.T("panel.time-left",new{hours=left/60,minutes=left%60});
        }
        else if(s.Phase=="ready"){fraction=1;phase=s.Pending.Count>0?Mod.T("status.full"):Mod.T("panel.ready");}
        else phase=Mod.T("panel.idle");
        float rightWidth=right.Length>0?Game1.smallFont.MeasureString(right).X:0;
        PanelText(b,phase,new Vector2(g.Status.X,g.Status.Y),paused?Bad:Game1.textColor,g.Status.Width-rightWidth-16,1f);
        if(right.Length>0)b.DrawString(Game1.smallFont,right,new Vector2(g.Status.Right-rightWidth,g.Status.Y),Muted);
        b.Draw(Game1.staminaRect,g.Bar,BarBack);
        if(fraction>0)b.Draw(Game1.staminaRect,new Rectangle(g.Bar.X+2,g.Bar.Y+2,(int)((g.Bar.Width-4)*fraction),g.Bar.Height-4),paused?BarPaused:BarFill);
        if(g.Status.Contains(mx,my)||g.Bar.Contains(mx,my))tip=Status(c);

        // Totals.
        string cycles=Mod.T("panel.cycles-done",new{count=s.CompletedCycles});
        string items=Mod.T("panel.items",new{count=TakeCandidates(c,s).Sum(i=>(long)i.Stack)});
        b.DrawString(Game1.smallFont,cycles,new Vector2(g.Stats.X,g.Stats.Y),Game1.textColor);
        float itemsWidth=Game1.smallFont.MeasureString(items).X;
        b.DrawString(Game1.smallFont,items,new Vector2(g.Stats.Right-itemsWidth,g.Stats.Y),Game1.textColor);
        b.Draw(Game1.staminaRect,new Rectangle(g.Stats.X,g.Stats.Bottom+4,g.Stats.Width,2),Muted*.4f);

        // Catch label + Take all.
        if(g.Labels)Utility.drawTextWithShadow(b,Mod.T("panel.catch"),Game1.smallFont,new Vector2(g.CatchLabel.X,g.CatchLabel.Y+10),Game1.textColor);
        bool anything=TakeCandidates(c,s).Any();
        TextButton(b,g.Take,Mod.T("panel.take-all"),anything,g.Take.Contains(mx,my));

        menu.ItemsToGrabMenu.draw(b);

        // Upgrade section.
        if(g.Upgrade.Width>0)
        {
            b.Draw(Game1.staminaRect,g.Upgrade,new Color(240,200,140)*.45f);
            Utility.drawTextWithShadow(b,Mod.T("panel.upgrade-title",new{level=s.Level+1}),Game1.smallFont,new Vector2(g.UpgradeTitle.X,g.UpgradeTitle.Y+6),Game1.textColor);
            long progress=MachineUpgrade.Progress(s);int needed=MachineCycleRules.RequiredCycles(s);
            bool cyclesMet=!s.StageProgressUnverified&&progress>=needed;
            Condition(b,g.Conditions[0],null,Mod.T("panel.cycles"),(s.StageProgressUnverified?"?":progress.ToString())+"/"+needed,cyclesMet,true);
            for(int n=0;n<materials.Length&&n+1<g.Conditions.Length;n++)
            {
                var m=materials[n];var data=ItemRegistry.GetDataOrErrorItem(m.Id);
                Condition(b,g.Conditions[n+1],data,data.DisplayName,m.Have+"/"+m.Need,m.Have>=m.Need,false);
            }
            if(g.Button.Width>0)
            {
                bool ready=Upgradable(c,s,materials);
                TextButton(b,g.Button,Mod.T("panel.upgrade-button",new{level=s.Level+1}),true,g.Button.Contains(mx,my),ready?Good:null);
            }
            else PanelText(b,Mod.T("panel.auto-upgrade"),new Vector2(g.Note.X,g.Note.Y+4),Muted,g.Note.Width,1f);
        }
        else
        {
            string max=Mod.T("panel.max-level");
            b.DrawString(Game1.smallFont,max,new Vector2(g.Stats.X+Game1.smallFont.MeasureString(cycles).X+32,g.Stats.Y),Muted);
        }

        if(g.Labels)Utility.drawTextWithShadow(b,Mod.T("panel.backpack"),Game1.smallFont,new Vector2(g.BackpackLabel.X,g.BackpackLabel.Y+6),Game1.textColor);
        menu.inventory.draw(b);

        // Native side buttons.
        menu.fillStacksButton?.draw(b);
        menu.organizeButton?.draw(b);
        menu.specialButton?.draw(b);
        menu.junimoNoteIcon?.draw(b);
        menu.okButton?.draw(b);
        if(menu.trashCan!=null)
        {
            menu.trashCan.draw(b);
            float lid=AccessTools.Field(typeof(MenuWithInventory),"trashCanLidRotation")?.GetValue(menu) is float r?r:0f;
            b.Draw(Game1.mouseCursors,new Vector2(menu.trashCan.bounds.X+60,menu.trashCan.bounds.Y+40),new Rectangle(564+Game1.player.trashCanLevel*18,129,18,10),Color.White,lid,new Vector2(16f,10f),4f,SpriteEffects.None,.86f);
        }
        (AccessTools.Field(typeof(ItemGrabMenu),"poof")?.GetValue(menu) as TemporaryAnimatedSprite)?.draw(b,true);

        // Tooltips, held item and cursor last, exactly like the native menu.
        if(menu.hoveredItem!=null)IClickableMenu.drawToolTip(b,menu.hoveredItem.getDescription(),menu.hoveredItem.DisplayName,menu.hoveredItem,menu.heldItem!=null);
        else if(!string.IsNullOrEmpty(menu.hoverText))IClickableMenu.drawHoverText(b,menu.hoverText,Game1.smallFont);
        else if(tip.Length>0)IClickableMenu.drawHoverText(b,tip,Game1.smallFont);
        menu.heldItem?.drawInMenu(b,new Vector2(Game1.getOldMouseX()+8,Game1.getOldMouseY()+8),1f);
        menu.drawMouse(b);
        return true;
    }

    private static void TextButton(SpriteBatch b,Rectangle r,string label,bool enabled,bool hovered,Color? accent=null)
    {
        IClickableMenu.drawTextureBox(b,Game1.mouseCursors,new Rectangle(432,439,9,9),r.X,r.Y,r.Width,r.Height,hovered?Color.Wheat:Color.White,4f,false);
        float scale=Math.Min(1f,(r.Width-24)/Math.Max(1f,Game1.smallFont.MeasureString(label).X));
        var size=Game1.smallFont.MeasureString(label)*scale;
        b.DrawString(Game1.smallFont,label,new Vector2(r.X+(r.Width-size.X)/2,r.Y+(r.Height-size.Y)/2+2),
            enabled?accent??Game1.textColor:Game1.unselectedOptionColor,0f,Vector2.Zero,scale,SpriteEffects.None,1f);
    }

    private static void Condition(SpriteBatch b,Rectangle r,StardewValley.ItemTypeDefinitions.ParsedItemData? item,string name,string count,bool met,bool check)
    {
        int textX=r.X+36;
        if(item!=null)b.Draw(item.GetTexture(),new Rectangle(r.X,r.Y,28,28),item.GetSourceRect(),Color.White);
        else if(check)b.Draw(Game1.mouseCursors,new Rectangle(r.X,r.Y,28,28),new Rectangle(128,256,64,64),met?Color.White:Color.White*.3f);
        float countWidth=Game1.smallFont.MeasureString(count).X;
        PanelText(b,name,new Vector2(textX,r.Y),Game1.textColor,r.Right-countWidth-12-textX,1f);
        b.DrawString(Game1.smallFont,count,new Vector2(r.Right-countWidth,r.Y),met?Good:Bad);
    }
}

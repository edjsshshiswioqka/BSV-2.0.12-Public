using System.Runtime.CompilerServices;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
namespace Eddie.FishingMachine;

/// <summary>
/// Draws BSV machine menus exactly as the approved mockup (v2): one machine window
/// (name + Lv badge + colour button / divider / state + time left + progress bar / cycles + items / divider /
/// "Catch" + Take all / machine grid / divider / "Upgrade to Lv N" + two-column condition boxes + upgrade button)
/// and a separate "Backpack" window under it. The native ItemGrabMenu keeps every item interaction; BSV only moves
/// its components and draws the frames. Other chest menus are untouched. When the screen is too small for even the
/// compact variant, the native menu and the side panel are used instead.
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
        public Rectangle Window,BagWindow,Title,Badge,ColorButton,Power,Status,Bar,Stats,Catch,Take,UpgradeTitle,Button,Note,Locked;
        public Rectangle[] Conditions=Array.Empty<Rectangle>();
        public int[] Dividers=Array.Empty<int>();
        public Point Grid,Bag;
        public bool Labels;
    }
    /// <summary>The machine menu drawn with the full layout in the current frame (null when the fallback is used).</summary>
    // Per screen: in split-screen each player has their own machine menu and layout.
    private readonly StardewModdingAPI.Utilities.PerScreen<ItemGrabMenu?> fullMenu=new();
    private readonly StardewModdingAPI.Utilities.PerScreen<FullLayout?> fullGeometry=new();
    internal ItemGrabMenu? FullMenu{get=>fullMenu.Value;set=>fullMenu.Value=value;}
    internal FullLayout? FullGeometry{get=>fullGeometry.Value;set=>fullGeometry.Value=value;}
    private static readonly ConditionalWeakTable<ItemGrabMenu,object> Prepared=new();
    private static readonly ConditionalWeakTable<ItemGrabMenu,object> Diagnosed=new();
    private static string? LastPoolLog;

    /// <summary>Logs the fish pool of every fishing machine (on load / new day), so catch problems show up in the SMAPI log.</summary>
    internal void LogAllFishPools()
    {
        foreach(var entry in Mod.Machines.All)
        {
            var c=entry.Key;if(ModEntry.IsCrab(c))continue;
            try{LogFishPool(c,Read(c));}catch(Exception ex){Mod.Monitor.Log("[BSV fishing pool] "+ex.Message,LogLevel.Warn);}
        }
    }
    /// <summary>Writes the fish this machine can catch right now (and why others are excluded) to the SMAPI log.</summary>
    private void LogFishPool(Chest c,ProductionState s)
    {
        try
        {
            var owner=Game1.GetPlayer(s.OwnerId==0?c.owner.Value:s.OwnerId,onlyOnline:false)??Game1.player;
            var rejected=new List<string>();
            var now=FishPool.Get(c.Location,c.TileLocation,WorldService.GetFacing(c),owner,WorldService.IsExtended(c),s.Level,Game1.year,null,null,(id,why)=>rejected.Add(id+" → "+why));
            string timeline;
            try{var t=TimelineEnvironment.Candidates(c,s,owner);timeline=t.Count+" ["+string.Join(", ",t.Select(f=>f.Id))+"]";}
            catch(Exception ex){timeline="error: "+ex.Message;}
            string journal="none";
            try
            {
                if(!string.IsNullOrEmpty(s.CycleToken))
                {
                    var env=TimelineEnvironment.At(s.CycleToken,Now);
                    journal=$"day {env.Day} unknownWeather {env.UnknownWeather} error {env.Error ?? "-"} segments "+string.Join(" ",env.Fish.Select(f=>$"{f.From}-{f.Until}:{f.Fish.Count}"))+$" other {env.Other.Count}";
                }
            }
            catch(Exception ex){journal="error: "+ex.Message;}
            Mod.Monitor.Log($"[BSV fishing pool] {c.Location.NameOrUniqueName} tile {c.TileLocation.X},{c.TileLocation.Y} facing {WorldService.GetFacing(c)} extended {WorldService.IsExtended(c)}; time {Game1.timeOfDay}, owner {owner.Name} fishing level {owner.fishingLevel.Value}, machine Lv{s.Level}, phase {s.Phase}."
                +$"\n  journal: {journal}\n  now: {now.Count} fish [{string.Join(", ",now.Select(f=>f.Id))}]\n  cycle candidates: {timeline}\n  rejected: {string.Join("; ",rejected.Take(80))}",LogLevel.Trace);
        }
        catch(Exception ex){Mod.Monitor.Log("[BSV fishing pool] diagnostic failed: "+ex,LogLevel.Warn);}
    }

    // Mockup palette.
    private static readonly Color Ink=new(74,44,10),Soft=new(122,74,20),Rule=new(199,118,31),
        ButtonFill=new(217,148,43),ButtonHover=new(232,171,69),ButtonEdge=new(143,90,18),ButtonInk=new(61,34,5),
        BoxFill=new(236,211,160),BoxEdge=new(201,160,90),Good=new(59,109,17),Bad=new(163,45,45),
        Track=new(217,197,155),TrackEdge=new(179,158,114),Progress=new(58,143,209),Paused=new(214,160,64),CheckFill=new(95,179,107);

    private const int Pad=24,Gap=12,RowStep=34,BoxHeight=30;

    private static FullLayout? Arrange(ItemGrabMenu menu,bool crab,ProductionState s,int conditions)
    {
        var grid=menu.ItemsToGrabMenu;var bag=menu.inventory;
        if(grid==null||bag==null)return null;
        int vw=Game1.uiViewport.Width,vh=Game1.uiViewport.Height;
        // Both machines reserve the same catch area (the 70-slot, 14 x 5 grid of the crab catcher and a Lv 2+ fishing
        // machine), so the two windows have identical size and structure. A smaller grid sits at the top of the area;
        // the rest shows the slots the next level unlocks.
        int columns0=Math.Max(1,grid.capacity/Math.Max(1,grid.rows));
        int cellW=grid.width/columns0,cellH=grid.height/Math.Max(1,grid.rows);
        int areaW=Math.Max(grid.width,cellW*14),areaH=Math.Max(grid.height,cellH*5);
        int inner=Math.Max(areaW,bag.width),width=inner+2*Pad;
        // The native side buttons (fill stacks, organize, trash, OK) stay to the right of the windows.
        if(width+2*(64+24)>vw)return null;
        bool upgrade=crab?s.Level==1:s.Level<3;
        // Variants, roomiest first: 0 = mockup; 1 = without section labels; 2 = also three condition columns.
        for(int variant=0;variant<3;variant++)
        {
            bool labels=variant==0;int columns=variant==2?3:2;
            int rows=upgrade?(conditions+columns-1)/columns:0;
            int upgradeHeight=upgrade?10+40+rows*RowStep+4+52:10+34;
            int machine=Pad+44+10+50+30+10+(labels?44:0)+areaH+Gap+upgradeHeight+Pad-4;
            int backpack=18+(labels?32:0)+bag.height+Pad;
            int total=machine+Gap+backpack;
            if(total>vh-16)continue;
            int x=(vw-width)/2,y=(vh-total)/2,cx=x+Pad,cw=inner;
            var g=new FullLayout{Window=new(x,y,width,machine),BagWindow=new(x,y+machine+Gap,width,backpack),Labels=labels};
            var dividers=new List<int>();
            int cy=y+Pad;
            g.ColorButton=new(cx+cw-44,cy+4,44,38);
            g.Title=new(cx,cy,cw-44-8,44);
            g.Take=new(0,0,170,40);
            cy+=44;dividers.Add(cy+4);cy+=10;
            g.Status=new(cx,cy,cw,28);g.Bar=new(cx,cy+30,cw,16);cy+=50;
            g.Stats=new(cx,cy,cw,28);cy+=30;dividers.Add(cy+2);cy+=10;
            if(labels){g.Catch=new(cx,cy,cw,44);g.Take=new(cx+cw-170,cy+2,170,40);cy+=44;}
            g.Grid=new(x+(width-grid.width)/2,cy);
            if(areaH-grid.height>=cellH)g.Locked=new(x+(width-areaW)/2,cy+grid.height+8,areaW,areaH-grid.height-8);
            cy+=areaH+Gap;
            dividers.Add(cy);cy+=10;
            if(upgrade)
            {
                g.UpgradeTitle=new(cx,cy,cw,40);cy+=40;
                int column=(cw-(columns-1)*12)/columns;
                // Column-major like the mockup: the first column is filled top to bottom before the next.
                g.Conditions=Enumerable.Range(0,conditions).Select(n=>new Rectangle(cx+n/rows*(column+12),cy+n%rows*RowStep,column,BoxHeight)).ToArray();
                cy+=rows*RowStep+4;
                // Same bar for both machines: the crab catcher's is a button, the fishing machine's explains its automatic upgrade.
                g.Button=new(cx,cy,cw,44);
            }
            else g.Note=new(cx,cy,cw,34);
            g.Dividers=dividers.ToArray();
            int by=g.BagWindow.Y+18;
            if(labels)by+=32;
            g.Bag=new(x+(width-bag.width)/2,by);
            return g;
        }
        return null;
    }

    private static void MoveInventory(InventoryMenu m,Point to)
    {
        int dx=to.X-m.xPositionOnScreen,dy=to.Y-m.yPositionOnScreen;
        if(dx==0&&dy==0)return;
        m.xPositionOnScreen=to.X;m.yPositionOnScreen=to.Y;
        foreach(var slot in m.inventory)slot.bounds.Offset(dx,dy);
    }
    private static void Place(ClickableComponent? c,int x,int y){if(c!=null)c.bounds=new Rectangle(x,y,c.bounds.Width,c.bounds.Height);}

    private static void Prepare(ItemGrabMenu menu)
    {
        if(Prepared.TryGetValue(menu,out _))return;
        Prepared.Add(menu,new object());
        // Mockup: the colour strip is closed until the colour button is clicked.
        if(menu.chestColorPicker!=null)menu.chestColorPicker.visible=false;
    }

    private static List<ClickableComponent>? PickerCells(ItemGrabMenu menu)
        =>AccessTools.Field(typeof(ItemGrabMenu),"discreteColorPickerCC")?.GetValue(menu) as List<ClickableComponent>;

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
        // The native colour toggle becomes the small colour button in the header.
        if(menu.colorPickerToggleButton!=null)menu.colorPickerToggleButton.bounds=g.ColorButton;
        // The colour strip floats just above the machine window (over the header if there is no room above).
        if(menu.chestColorPicker is {} picker)
        {
            int px=g.Window.X+(g.Window.Width-picker.width)/2,py=g.Window.Y-picker.height-4;
            if(py<4)py=g.Window.Y+Pad+48;
            int dx=px-picker.xPositionOnScreen,dy=py-picker.yPositionOnScreen;
            if(dx!=0||dy!=0)
            {
                picker.xPositionOnScreen=px;picker.yPositionOnScreen=py;
                if(PickerCells(menu) is {} cells)foreach(var cell in cells)cell.bounds.Offset(dx,dy);
            }
        }
        // Clicks anywhere inside the windows count as inside the menu (no accidental item drops).
        menu.xPositionOnScreen=g.Window.X;menu.yPositionOnScreen=g.Window.Y;
        menu.width=g.Window.Width;menu.height=g.BagWindow.Bottom-g.Window.Y;
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
        // Chest transfers rebuild the menu: log the pool once per machine and game time, not once per rebuild.
        if(!crab&&!Diagnosed.TryGetValue(menu,out _)){Diagnosed.Add(menu,new object());string mark=c.Location?.NameOrUniqueName+c.TileLocation+Game1.timeOfDay;if(mark!=LastPoolLog){LastPoolLog=mark;LogFishPool(c,s);}}

        int mx=Game1.getMouseX(),my=Game1.getMouseY();string tip="";
        if(menu.drawBG&&!Game1.options.showClearBackgrounds)
            b.Draw(Game1.fadeToBlackRect,new Rectangle(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height),Color.Black*.5f);
        IClickableMenu.drawTextureBox(b,g.Window.X,g.Window.Y,g.Window.Width,g.Window.Height,Color.White);
        IClickableMenu.drawTextureBox(b,g.BagWindow.X,g.BagWindow.Y,g.BagWindow.Width,g.BagWindow.Height,Color.White);
        int left=g.Window.X+Pad,right=g.Window.Right-Pad;
        foreach(int y in g.Dividers)Box(b,new Rectangle(left,y,right-left,2),Rule,null);

        // Header: name, level badge, colour button.
        var colorButton=g.ColorButton;
        Box(b,colorButton,BoxFill,BoxEdge);
        int cell=10,cx0=colorButton.X+(colorButton.Width-2*cell-4)/2,cy0=colorButton.Y+(colorButton.Height-2*cell-4)/2;
        b.Draw(Game1.staminaRect,new Rectangle(cx0,cy0,cell,cell),Progress);
        b.Draw(Game1.staminaRect,new Rectangle(cx0+cell+4,cy0,cell,cell),CheckFill);
        b.Draw(Game1.staminaRect,new Rectangle(cx0,cy0+cell+4,cell,cell),new Color(230,180,34));
        b.Draw(Game1.staminaRect,new Rectangle(cx0+cell+4,cy0+cell+4,cell,cell),new Color(212,40,60));
        if(colorButton.Contains(mx,my))tip=Mod.T("panel.color");
        string badge=Mod.T("panel.level-badge",new{level=s.Level});
        var badgeSize=Game1.smallFont.MeasureString(badge);
        g.Badge=new Rectangle(colorButton.X-12-((int)badgeSize.X+28),colorButton.Y,(int)badgeSize.X+28,colorButton.Height);
        Box(b,g.Badge,s.Level>=3?new Color(190,150,240):ButtonFill,ButtonEdge);
        b.DrawString(Game1.smallFont,badge,new Vector2(g.Badge.X+14,g.Badge.Y+(g.Badge.Height-badgeSize.Y)/2+2),ButtonInk);
        if(!crab&&g.Badge.Contains(mx,my))tip=FishingBalanceTip(s);
        int titleRight=g.Badge.X-12;
        {
            // On / off switch: green while on, red while off.
            bool off=PoweredOff(c);
            string label=Mod.T(off?"panel.power-off":"panel.power-on");
            var labelSize=Game1.smallFont.MeasureString(label);
            g.Power=new Rectangle(titleRight-((int)labelSize.X+36),colorButton.Y,(int)labelSize.X+36,colorButton.Height);
            bool hover=g.Power.Contains(mx,my);
            Box(b,g.Power,off?(hover?new Color(214,96,96):new Color(196,72,72)):(hover?new Color(120,196,96):CheckFill),off?new Color(120,30,30):Good);
            var dot=new Rectangle(g.Power.X+10,g.Power.Center.Y-5,10,10);
            b.Draw(Game1.staminaRect,dot,off?new Color(90,20,20):new Color(214,255,170));
            b.DrawString(Game1.smallFont,label,new Vector2(dot.Right+6,g.Power.Y+(g.Power.Height-labelSize.Y)/2+2),Color.White);
            if(hover)tip=Mod.T((crab?"crab.":"panel.")+(off?"power-off-help":"power-on-help"));
            titleRight=g.Power.X-12;
        }
        if(!g.Labels){g.Take=new Rectangle(titleRight-g.Take.Width,colorButton.Y,g.Take.Width,colorButton.Height);titleRight=g.Take.X-12;}
        // The level lives in the badge; drop a trailing "Lv1"-style suffix from the item name.
        string name=System.Text.RegularExpressions.Regex.Replace(Mod.T(crab?"crab.name":"machine.name"),@"\s*[Ll][Vv]\.?\s*\d+$","");
        Heading(b,name,new Vector2(g.Title.X,g.Title.Y+4),titleRight-g.Title.X);

        // State, time left and progress bar.
        bool paused=s.Phase=="working"&&s.Timeline?.PausedReason!=null;double fraction=0;string phase,timeLeft="";
        if(PreparingRecovery(c))phase=Mod.T(s.Phase=="working"?"remove.status-finishing":"remove.status-empty");
        else if(s.Phase=="working")
        {
            long total=Math.Max(1,s.DueAt-s.StartedAt),remaining=Math.Max(0,s.DueAt-Now);
            fraction=Math.Clamp(1.0-(double)remaining/total,0,1);
            phase=paused?Mod.T("status.timeline-paused",new{reason=Mod.T(MachinePauseText.KeyFor(s.Timeline!.PausedReason))}):Mod.T("panel.producing");
            if(!paused)timeLeft=Mod.T("panel.time-left",new{hours=remaining/60,minutes=remaining%60});
        }
        else if(s.Phase=="ready"){fraction=1;phase=s.Pending.Count>0?Mod.T("status.full"):Mod.T("panel.ready");}
        else phase=Mod.T(PoweredOff(c)?"panel.off":"panel.idle");
        float timeWidth=timeLeft.Length>0?Game1.smallFont.MeasureString(timeLeft).X:0;
        PanelText(b,phase,new Vector2(g.Status.X,g.Status.Y),paused?Bad:Ink,g.Status.Width-timeWidth-16,1f);
        if(timeLeft.Length>0)b.DrawString(Game1.smallFont,timeLeft,new Vector2(g.Status.Right-timeWidth,g.Status.Y),Ink);
        Box(b,g.Bar,Track,TrackEdge);
        if(fraction>0)b.Draw(Game1.staminaRect,new Rectangle(g.Bar.X+2,g.Bar.Y+2,(int)((g.Bar.Width-4)*fraction),g.Bar.Height-4),paused?Paused:Progress);
        if(g.Status.Contains(mx,my)||g.Bar.Contains(mx,my))tip=Status(c);

        // Totals.
        string cycles=Mod.T("panel.cycles-done",new{count=s.CompletedCycles});
        string items=Mod.T("panel.items",new{count=TakeCandidates(c,s).Sum(i=>(long)i.Stack)});
        float itemsWidth=Game1.smallFont.MeasureString(items).X;
        PanelText(b,cycles,new Vector2(g.Stats.X,g.Stats.Y),Soft,g.Stats.Width-itemsWidth-16,1f);
        b.DrawString(Game1.smallFont,items,new Vector2(g.Stats.Right-itemsWidth,g.Stats.Y),Soft);

        // Catch label + Take all.
        if(g.Labels)b.DrawString(Game1.smallFont,Mod.T("panel.catch"),new Vector2(g.Catch.X,g.Catch.Y+10),Ink);
        bool anything=TakeCandidates(c,s).Any();
        Button(b,g.Take,Mod.T("panel.take-all"),g.Take.Contains(mx,my),anything,false);
        if(g.Take.Contains(mx,my))tip=Mod.T("panel.take-help");

        menu.ItemsToGrabMenu.draw(b);
        if(g.Locked.Width>0)
        {
            Box(b,g.Locked,Track,TrackEdge);
            string more=Mod.T("panel.more-slots",new{level=s.Level+1,count=MachineUpgrade.Capacity(s.Level+1)});
            var moreSize=Game1.smallFont.MeasureString(more);
            b.DrawString(Game1.smallFont,more,new Vector2(g.Locked.Center.X-moreSize.X/2,g.Locked.Center.Y-moreSize.Y/2+2),Soft);
            if(g.Locked.Contains(mx,my))tip=Mod.T("panel.auto-upgrade");
        }

        // Upgrade section.
        if(g.UpgradeTitle.Width>0)
        {
            Heading(b,Mod.T("panel.upgrade-title",new{level=s.Level+1}),new Vector2(g.UpgradeTitle.X,g.UpgradeTitle.Y+2),g.UpgradeTitle.Width);
            long progress=MachineUpgrade.Progress(s);int needed=MachineCycleRules.RequiredCycles(s);
            bool cyclesMet=!s.StageProgressUnverified&&progress>=needed;
            Condition(b,g.Conditions[0],null,Mod.T("panel.cycles"),(s.StageProgressUnverified?"?":progress.ToString())+"/"+needed,cyclesMet);
            for(int n=0;n<materials.Length&&n+1<g.Conditions.Length;n++)
            {
                var m=materials[n];var data=ItemRegistry.GetDataOrErrorItem(m.Id);
                Condition(b,g.Conditions[n+1],data,data.DisplayName,m.Have+"/"+m.Need,m.Have>=m.Need);
                if(g.Conditions[n+1].Contains(mx,my))tip=data.DisplayName+" "+m.Have+"/"+m.Need;
            }
            if(g.Button.Width>0)
            {
                Button(b,g.Button,Mod.T(crab?"panel.upgrade-button":"panel.auto-upgrade-button",new{level=s.Level+1}),g.Button.Contains(mx,my),true,true);
                if(g.Button.Contains(mx,my))
                {
                    if(crab){if(!Upgradable(c,s,materials))tip=MissingText(s,materials);}
                    else tip=Lacking(s,materials).Count>0?Mod.T("panel.missing",new{list=string.Join("; ",Lacking(s,materials))}):Mod.T("panel.auto-upgrade");
                }
            }
            else
            {
                Box(b,g.Note,BoxFill,BoxEdge);
                PanelText(b,Mod.T("panel.auto-upgrade"),new Vector2(g.Note.X+12,g.Note.Y+(g.Note.Height-Game1.smallFont.LineSpacing)/2+2),Soft,g.Note.Width-24,1f);
            }
        }
        else
        {
            Heading(b,Mod.T("panel.max-level"),new Vector2(g.Note.X,g.Note.Y),g.Note.Width);
        }

        // Backpack window.
        if(g.Labels)b.DrawString(Game1.smallFont,Mod.T("panel.backpack"),new Vector2(g.BagWindow.X+Pad,g.BagWindow.Y+18),Ink);
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
        if(menu.chestColorPicker is {visible:true} picker)picker.draw(b);

        // Tooltips, held item and cursor last, exactly like the native menu.
        if(menu.hoveredItem!=null)IClickableMenu.drawToolTip(b,menu.hoveredItem.getDescription(),menu.hoveredItem.DisplayName,menu.hoveredItem,menu.heldItem!=null);
        else if(!string.IsNullOrEmpty(menu.hoverText))IClickableMenu.drawHoverText(b,menu.hoverText,Game1.smallFont);
        else if(tip.Length>0)IClickableMenu.drawHoverText(b,Game1.parseText(tip,Game1.smallFont,Math.Min(560,Game1.uiViewport.Width-96)),Game1.smallFont);
        menu.heldItem?.drawInMenu(b,new Vector2(Game1.getOldMouseX()+8,Game1.getOldMouseY()+8),1f);
        menu.drawMouse(b);
        return true;
    }

    /// <summary>Cycles and materials still missing for the next level (no machine-state check).</summary>
    internal List<string> Lacking(ProductionState s,(int Have,int Need,string Id)[] materials)
    {
        var lacking=new List<string>();
        long progress=MachineUpgrade.Progress(s);int needed=MachineCycleRules.RequiredCycles(s);
        if(progress<needed)lacking.Add(Mod.T("panel.cycles-req",new{count=progress,needed}));
        foreach(var m in materials.Where(m=>m.Have<m.Need))lacking.Add(ItemRegistry.GetDataOrErrorItem(m.Id).DisplayName+" "+m.Have+"/"+m.Need);
        return lacking;
    }

    private string MissingText(ProductionState s,(int Have,int Need,string Id)[] materials)
    {
        if(s.Phase!="idle")return Mod.T("crab.upgrade-unavailable");
        var lacking=new List<string>();
        long progress=MachineUpgrade.Progress(s);int needed=MachineCycleRules.RequiredCycles(s);
        if(progress<needed)lacking.Add(Mod.T("panel.cycles-req",new{count=progress,needed}));
        foreach(var m in materials.Where(m=>m.Have<m.Need))lacking.Add(ItemRegistry.GetDataOrErrorItem(m.Id).DisplayName+" "+m.Have+"/"+m.Need);
        return Mod.T("panel.missing",new{list=string.Join("; ",lacking)});
    }

    /// <summary>Flat box with a 2 px edge, as in the mockup.</summary>
    private static void Box(SpriteBatch b,Rectangle r,Color fill,Color? edge)
    {
        b.Draw(Game1.staminaRect,r,fill);
        if(edge is not {} e)return;
        b.Draw(Game1.staminaRect,new Rectangle(r.X,r.Y,r.Width,2),e);
        b.Draw(Game1.staminaRect,new Rectangle(r.X,r.Bottom-2,r.Width,2),e);
        b.Draw(Game1.staminaRect,new Rectangle(r.X,r.Y,2,r.Height),e);
        b.Draw(Game1.staminaRect,new Rectangle(r.Right-2,r.Y,2,r.Height),e);
    }

    /// <summary>Larger heading text (mockup "th" style): the game's dialogue font, scaled to fit.</summary>
    private static void Heading(SpriteBatch b,string text,Vector2 at,float maxWidth)
    {
        var size=Game1.dialogueFont.MeasureString(text);
        float scale=Math.Min(.8f,maxWidth/Math.Max(1f,size.X));
        Utility.drawTextWithShadow(b,text,Game1.dialogueFont,at,Ink,scale);
    }

    private static void Button(SpriteBatch b,Rectangle r,string label,bool hovered,bool enabled,bool large)
    {
        Box(b,r,hovered?ButtonHover:ButtonFill,ButtonEdge);
        var font=large?Game1.dialogueFont:Game1.smallFont;
        float scale=Math.Min(large?.7f:1f,(r.Width-24)/Math.Max(1f,font.MeasureString(label).X));
        var size=font.MeasureString(label)*scale;
        b.DrawString(font,label,new Vector2(r.X+(r.Width-size.X)/2,r.Y+(r.Height-size.Y)/2+2),enabled?ButtonInk:ButtonInk*.5f,0f,Vector2.Zero,scale,SpriteEffects.None,1f);
    }

    private static void Condition(SpriteBatch b,Rectangle r,StardewValley.ItemTypeDefinitions.ParsedItemData? item,string name,string count,bool met)
    {
        Box(b,r,BoxFill,BoxEdge);
        var icon=new Rectangle(r.X+6,r.Y+(r.Height-22)/2,22,22);
        if(item!=null)b.Draw(item.GetTexture(),icon,item.GetSourceRect(),Color.White);
        else
        {
            Box(b,icon,met?CheckFill:Track,met?null:TrackEdge);
            if(met)
            {
                // White check mark: two thin strokes.
                b.Draw(Game1.staminaRect,new Vector2(icon.X+5,icon.Y+11),null,Color.White,MathHelper.PiOver4,Vector2.Zero,new Vector2(7,3),SpriteEffects.None,1f);
                b.Draw(Game1.staminaRect,new Vector2(icon.X+8,icon.Y+16),null,Color.White,-MathHelper.PiOver4,Vector2.Zero,new Vector2(13,3),SpriteEffects.None,1f);
            }
        }
        float countWidth=Game1.smallFont.MeasureString(count).X;
        float textY=r.Y+(r.Height-Game1.smallFont.LineSpacing)/2+2;
        PanelText(b,name,new Vector2(icon.Right+8,textY),Ink,r.Right-10-countWidth-8-(icon.Right+8),1f);
        b.DrawString(Game1.smallFont,count,new Vector2(r.Right-10-countWidth,textY),met?Good:Bad);
    }
}

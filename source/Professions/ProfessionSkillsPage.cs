using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
namespace BetterStardewValley.Professions;

// Only the five vanilla rows are replaced. Native avatar, wallet, mastery and
// other draw code (including other mods' postfixes) continue to execute.
//
// Row layout (left to right): skill icon, name, native-style level beads, the two level-5
// professions side by side, the four level-10 professions as a 2x2 grid, level number.
// Grid columns follow the vanilla tree: the left column holds the two level-10 choices that
// grew out of the left level-5 profession, the right column those of the right one.
internal static class ProfessionSkillsPage
{
    // Pitch is adaptive: vanilla leaves roughly 55 px per skill, a roomier page gets up to 68.
    internal const int MaxPitch=68, MinPitch=52, HeaderHeight=18, MinWidth=336, SlotGap=2, GroupGap=12, NumberWidth=32, BeadSpan=101;
    internal const int RowHeight=MaxPitch;
    private static readonly FieldInfo Areas=AccessTools.Field(typeof(SkillsPage),"skillAreas"),Bars=AccessTools.Field(typeof(SkillsPage),"skillBars"),Avatar=AccessTools.Field(typeof(SkillsPage),"playerPanel");
    private static readonly ConditionalWeakTable<IClickableMenu,State> States=new();
    // SpaceCore replaces the Skills tab with its own page (same five rows, same component IDs).
    // Its row loop skips rows outside [skillScrollOffset, +4]; while BSV draws, the offset is parked out of range.
    private static Type? SpaceCorePage;
    private static FieldInfo? ScAreas,ScPanel,ScOffset,ScLastSnap;
    private static PropertyInfo? ScGameCount,ScVisible;
    private const int ParkedOffset=1000;
    internal sealed class State
    {
        internal Farmer Owner=null!;internal Rectangle Bounds;internal int First,Focus=-1,Pinned=-1,Hover=-1,HoverSkill=-1,NameWidth=72;
        internal readonly List<(int Id,Rectangle Box)> Icons=new();
        internal int Pitch=>PitchFor(Bounds.Height);
        internal int Visible=>Math.Max(1,Math.Min(5,Math.Max(0,Bounds.Height-HeaderHeight)/Pitch));
        internal int RowTop(int row)=>Bounds.Y+HeaderHeight+(row-First)*Pitch;
        /// <summary>Visible skill row under a screen y coordinate, or -1.</summary>
        internal int RowAt(int y)
        {
            int rel=y-Bounds.Y-HeaderHeight;if(rel<0)return -1;
            int row=First+rel/Pitch;return row<Math.Min(5,First+Visible)?row:-1;
        }
    }
    /// <summary>Horizontal arrangement of one row, relative to the left edge of the page area.</summary>
    internal readonly record struct Metrics(int Slot,float BeadScale,int NameX,int NameWidth,int BarX,int PairX,int GridX,int NumberX);
    internal static int PitchFor(int height)=>Math.Clamp((height-HeaderHeight)/5,MinPitch,MaxPitch);
    /// <summary>Slot edge in pixels: the 16 px profession sprite at 1.5x to 2x, small enough that the 2x2 grid fits a row.</summary>
    internal static int SlotFor(int pitch)=>Math.Clamp((pitch-8)/2,24,30); // 2x2 grid + 6 px clear gap to the next row's grid
    /// <summary>Slot edge limited by the width too: the bar, both icon groups and the level number must all fit.</summary>
    internal static int SlotFor(int pitch,int width)=>Math.Max(24,Math.Min(SlotFor(pitch),(width-220)/4));
    internal static Metrics Measure(int width,int slot,int nameWidth)
    {
        int group=2*slot+SlotGap,numberX=width-NumberWidth,gridX=numberX-10-group,pairX=gridX-GroupGap-group;
        const int nameX=0; // mockup: skill name first, no skill icon
        // The bar at its smallest scale always fits; a long skill name is truncated instead.
        nameWidth=Math.Max(24,Math.Min(nameWidth,pairX-12-(nameX+8)-BeadSpan));
        int barX=nameX+nameWidth+8,room=pairX-12-barX;
        // The bar stretches to fill the space up to the level-5 pair (no empty band between bar and icons), at most 3x.
        float scale=Math.Clamp(room/(float)BeadSpan,1f,3f);
        return new(slot,scale,nameX,nameWidth,barX,pairX,gridX,numberX);
    }
    internal static bool Installed;
    private static Harmony? UiPatches;
    internal static void Remove(){Installed=false;UiPatches?.UnpatchAll(UiPatches.Id);}
    internal static void Install(Harmony h)
    {
        h=new Harmony(h.Id+".SkillsPage");UiPatches=h;
        try {
        h.Patch(AccessTools.Method(typeof(SkillsPage),"draw",new[]{typeof(SpriteBatch)}),transpiler:new(typeof(ProfessionSkillsPage),nameof(ReplaceRows)),postfix:new(typeof(ProfessionSkillsPage),nameof(Details)));
        h.Patch(AccessTools.Method(typeof(SkillsPage),"performHoverAction",new[]{typeof(int),typeof(int)}),prefix:new(typeof(ProfessionSkillsPage),nameof(Hover)));
        h.Patch(AccessTools.Method(typeof(SkillsPage),"receiveLeftClick",new[]{typeof(int),typeof(int),typeof(bool)}),prefix:new(typeof(ProfessionSkillsPage),nameof(Click)));
        h.Patch(AccessTools.Method(typeof(GameMenu),"receiveKeyPress",new[]{typeof(Keys)}),prefix:new(typeof(ProfessionSkillsPage),nameof(Key)));
        h.Patch(AccessTools.Method(typeof(GameMenu),"receiveGamePadButton",new[]{typeof(Buttons)}),prefix:new(typeof(ProfessionSkillsPage),nameof(Pad)));
        h.Patch(AccessTools.Method(typeof(GameMenu),"receiveScrollWheelAction",new[]{typeof(int)}),prefix:new(typeof(ProfessionSkillsPage),nameof(Scroll)));
        Installed=true;
        InstallSpaceCore(h);
        } catch {Remove();throw;}
    }
    private static void InstallSpaceCore(Harmony h)
    {
        var type=AccessTools.TypeByName("SpaceCore.Interface.NewSkillsPage");
        if(type==null)return;
        ScAreas=AccessTools.Field(type,"skillAreas");ScPanel=AccessTools.Field(type,"playerPanel");ScOffset=AccessTools.Field(type,"skillScrollOffset");
        ScLastSnap=AccessTools.Field(type,"lastSnappedComponent");ScGameCount=AccessTools.Property(type,"GameSkillCount");ScVisible=AccessTools.Property(type,"VisibleSkills");
        if(ScAreas==null||ScPanel==null||ScOffset==null||ScGameCount==null||ScVisible==null||ScOffset.FieldType!=typeof(int))
        {ProfessionService.Trace("SpaceCore skills page layout not recognised; its own page is kept.");return;}
        var draw=AccessTools.Method(type,"draw",new[]{typeof(SpriteBatch)});
        var hover=AccessTools.Method(type,"performHoverAction",new[]{typeof(int),typeof(int)});
        var click=AccessTools.Method(type,"receiveLeftClick",new[]{typeof(int),typeof(int),typeof(bool)});
        if(draw==null||hover==null||click==null){ProfessionService.Trace("SpaceCore skills page methods not found; its own page is kept.");return;}
        h.Patch(draw,prefix:new(typeof(ProfessionSkillsPage),nameof(ScDrawPrefix)),postfix:new(typeof(ProfessionSkillsPage),nameof(ScDrawPostfix)),finalizer:new(typeof(ProfessionSkillsPage),nameof(ScDrawFinalizer)));
        h.Patch(hover,prefix:new(typeof(ProfessionSkillsPage),nameof(ScHover)));
        h.Patch(click,prefix:new(typeof(ProfessionSkillsPage),nameof(ScClick)));
        SpaceCorePage=type;
        ProfessionService.Trace("BSV skills page attached to SpaceCore's skills page.");
    }
    private static bool IsSpaceCore(IClickableMenu page)=>SpaceCorePage!=null&&page.GetType()==SpaceCorePage;
    /// <summary>Only the plain five-skill SpaceCore page is redrawn; with custom skills or scrolling SpaceCore keeps its own rows.</summary>
    private static bool SpaceCoreSimple(IClickableMenu page)
    {
        try{return (int)(ScGameCount!.GetValue(page)!)==5&&((Array)ScVisible!.GetValue(page)!).Length==0;}catch{return false;}
    }
    private static void ScDrawPrefix(IClickableMenu __instance,out int __state)
    {
        __state=-1;
        if(Get(__instance)==null)return;
        __state=(int)ScOffset!.GetValue(__instance)!;
        // Skip SpaceCore's own scroll correction for this frame; all five rows are visible anyway.
        ScLastSnap?.SetValue(__instance,__instance.currentlySnappedComponent);
        ScOffset.SetValue(__instance,ParkedOffset);
    }
    private static void ScDrawPostfix(IClickableMenu __instance,SpriteBatch b,int __state)
    {
        if(__state<0)return;
        ScOffset!.SetValue(__instance,__state);
        if(DrawRows(__instance,b))Details(__instance,b);
    }
    private static Exception? ScDrawFinalizer(IClickableMenu __instance,Exception? __exception,int __state)
    {
        if(__state>=0&&(int)(ScOffset!.GetValue(__instance)!)==ParkedOffset)ScOffset.SetValue(__instance,__state);
        return __exception;
    }
    private static bool ScHover(IClickableMenu __instance,int x,int y)
    {
        var s=Get(__instance);if(s==null)return true;
        bool native=HoverCore(s,x,y);
        if(native)return true;
        try{AccessTools.Field(__instance.GetType(),"hoverText")?.SetValue(__instance,"");AccessTools.Field(__instance.GetType(),"hoverTitle")?.SetValue(__instance,"");AccessTools.Field(__instance.GetType(),"professionImage")?.SetValue(__instance,-1);}catch{}
        return false;
    }
    private static bool ScClick(IClickableMenu __instance,int x,int y)=>ClickCore(__instance,x,y);
    internal static IEnumerable<CodeInstruction> ReplaceRows(IEnumerable<CodeInstruction> instructions,ILGenerator generator)
    {
        var original=instructions.ToList();
        var code=original.Select(c=>new CodeInstruction(c)).ToList();
        var bars=code.Select((c,i)=>(c,i)).Where(p=>p.c.opcode==OpCodes.Ldfld&&Equals(p.c.operand,Bars)).Select(p=>p.i).ToArray();
        if(bars.Length!=2){TranspilerGuard.Failed("SkillsPage.draw","SkillsPage vanilla bar boundaries changed; native UI retained.");return original;}
        int end=bars[0]-1;
        var starts=Enumerable.Range(0,end-3).Where(i=>code[i].LoadsConstant(68)&&code[i+1].IsStloc()&&code[i+2].LoadsConstant(0)&&code[i+3].IsStloc()).ToArray();
        if(starts.Length!=1){TranspilerGuard.Failed("SkillsPage.draw","SkillsPage vanilla row boundary changed; native UI retained.");return original;}
        int start=starts[0]+2;
        if(code.Skip(start).Take(end-start).Any(c=>c.blocks.Count>0)){TranspilerGuard.Failed("SkillsPage.draw","SkillsPage unexpected exception region.");return original;}
        var finish=generator.DefineLabel();code[end].labels.Add(finish);
        var native=generator.DefineLabel();code[start].labels.Add(native);
        foreach(int i in bars){code[i].opcode=OpCodes.Call;code[i].operand=AccessTools.Method(typeof(ProfessionSkillsPage),nameof(VisibleBars));}
        code.InsertRange(start,new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldarg_1),new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ProfessionSkillsPage),nameof(DrawRows))),new CodeInstruction(OpCodes.Brtrue,finish)});
        TranspilerGuard.Succeeded("SkillsPage.draw");
        return code;
    }
    private static IClickableMenu? Page(GameMenu menu)
    {
        var pages=(List<IClickableMenu>)AccessTools.Field(typeof(GameMenu),"pages").GetValue(menu)!;
        var page=menu.currentTab>=0&&menu.currentTab<pages.Count?pages[menu.currentTab]:null;
        return page is SkillsPage||page!=null&&IsSpaceCore(page)?page:null;
    }
    internal static Rectangle Layout(Rectangle avatar,IEnumerable<Rectangle> rows,Rectangle page,Rectangle viewport)
    {
        var boxes=rows.ToArray();if(boxes.Length!=5)return Rectangle.Empty;
        int left=Math.Max(avatar.Right+24,boxes.Min(b=>b.Left));
        // The "Lv 5 / Lv 10" header sits in the free band above the first native row, so rows keep their full pitch.
        int top=Math.Max(viewport.Top+16,boxes.Min(b=>b.Top)-HeaderHeight);
        int right=Math.Min(page.Right-48,viewport.Right-24);
        // The band under the last native row is free down to the mastery strip; use it for taller rows (bigger icons).
        // Native rows are 68 px apart (SkillsPage); the mastery bar starts 52 px under the last row. Never go past
        // the native row band, so the mastery bar and the Community Center / Joja box are never covered.
        int bottom=Math.Min(boxes.Max(b=>b.Bottom)+32,Math.Min(page.Bottom-32,viewport.Bottom-32));
        return right-left<MinWidth||bottom-top<HeaderHeight+MinPitch?Rectangle.Empty:new(left,top,right-left,Math.Min(HeaderHeight+MaxPitch*5,bottom-top));
    }
    private static State? Get(IClickableMenu page)
    {
        // K04: with the all-professions option off, leave the native Skills page untouched.
        if(!Installed||!ProfessionService.Active)return null;
        List<ClickableTextureComponent> areas;Rectangle avatar;
        if(page.GetType()==typeof(SkillsPage)){areas=(List<ClickableTextureComponent>)Areas.GetValue(page)!;avatar=(Rectangle)Avatar.GetValue(page)!;}
        else if(IsSpaceCore(page)&&SpaceCoreSimple(page)){areas=(List<ClickableTextureComponent>)ScAreas!.GetValue(page)!;avatar=((ClickableComponent)ScPanel!.GetValue(page)!).bounds;}
        else return null;
        var vanilla=areas.Where(c=>int.TryParse(c.name,out int n)&&n>=0&&n<5).ToArray();
        var rect=Layout(avatar,vanilla.Select(c=>c.bounds),new(page.xPositionOnScreen,page.yPositionOnScreen,page.width,page.height),new(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height));
        // Respect custom skill components that occupy the same horizontal region.
        foreach(var c in areas.Except(vanilla))if(c.bounds.Left<rect.Right&&c.bounds.Right>rect.Left&&c.bounds.Top>=rect.Top&&c.bounds.Top<rect.Bottom)rect.Height=c.bounds.Top-rect.Top;
        if(rect.Width<MinWidth||rect.Height<HeaderHeight+MinPitch)return null;
        var state=States.GetValue(page,_=>new State{Owner=Game1.player});
        if(state.Owner!=Game1.player){state.Owner=Game1.player;state.Focus=state.Pinned=state.Hover=-1;}
        if(rect!=state.Bounds){state.Bounds=rect;state.First=Math.Clamp(state.First,0,5-state.Visible);Build(state);}
        return state;
    }
    /// <summary>Hit boxes only. Icon positions depend on the area, never on font metrics.</summary>
    internal static void Build(State s)
    {
        s.Icons.Clear();
        int pitch=s.Pitch,slot=SlotFor(pitch,s.Bounds.Width),group=2*slot+SlotGap;
        var m=Measure(s.Bounds.Width,slot,s.NameWidth);
        int pairX=s.Bounds.X+m.PairX,gridX=s.Bounds.X+m.GridX;
        for(int row=s.First;row<Math.Min(5,s.First+s.Visible);row++)
        {
            var ids=ProfessionRules.Skills[row].All.ToArray();
            int top=s.RowTop(row),pairY=top+(pitch-slot)/2,gridY=top+(pitch-group)/2,step=slot+SlotGap;
            // Level 5: left and right profession. Level 10: one column per level-5 profession.
            s.Icons.Add((ids[0],new(pairX,pairY,slot,slot)));
            s.Icons.Add((ids[1],new(pairX+step,pairY,slot,slot)));
            s.Icons.Add((ids[2],new(gridX,gridY,slot,slot)));
            s.Icons.Add((ids[3],new(gridX,gridY+step,slot,slot)));
            s.Icons.Add((ids[4],new(gridX+step,gridY,slot,slot)));
            s.Icons.Add((ids[5],new(gridX+step,gridY+step,slot,slot)));
        }
    }
    private static List<ClickableTextureComponent> VisibleBars(IClickableMenu page)
    {
        var list=(List<ClickableTextureComponent>)Bars.GetValue(page)!;
        // Native component IDs: 100..104 for level 5, 200..204 for level 10.
        return Get(page)==null?list:list.Where(c=>!(c.myID is >=100 and <=104 or >=200 and <=204)).ToList();
    }
    private static string Fit(string text,float width)
    {
        if(Game1.smallFont.MeasureString(text).X<=width)return text;
        while(text.Length>0&&Game1.smallFont.MeasureString(text+"…").X>width)text=text[..^1];return text+"…";
    }
    internal static Rectangle ProfessionIcon(int id)=>new(id%6*16,624+id/6*16,16,16);

    // Flat slot palette: left branch teal, right branch purple, locked beige.
    private static readonly Color LeftFill=new(95,194,180),LeftEdge=new(31,111,101),RightFill=new(165,129,214),RightEdge=new(84,51,127),
        LockedFill=new(217,197,155),LockedEdge=new(179,158,114),LockedMark=new(150,128,86),HeaderText=new(122,74,20);
    private static void Fill(SpriteBatch b,Rectangle r,Color c,float depth)=>b.Draw(Game1.staminaRect,r,null,c,0f,Vector2.Zero,SpriteEffects.None,depth);
    private static void Outline(SpriteBatch b,Rectangle r,int t,Color c,float depth)
    {
        Fill(b,new Rectangle(r.X,r.Y,r.Width,t),c,depth);Fill(b,new Rectangle(r.X,r.Bottom-t,r.Width,t),c,depth);
        Fill(b,new Rectangle(r.X,r.Y+t,t,r.Height-2*t),c,depth);Fill(b,new Rectangle(r.Right-t,r.Y+t,t,r.Height-2*t),c,depth);
    }
    private static void Centered(SpriteBatch b,string text,float centerX,float y,float scale,Color c,float depth)
    {
        var size=Game1.smallFont.MeasureString(text)*scale;
        b.DrawString(Game1.smallFont,text,new Vector2(centerX-size.X/2f,y),c,0f,Vector2.Zero,scale,SpriteEffects.None,depth);
    }
    /// <summary>The native ten-bead level bar (8x9 beads, 14x9 at levels 5 and 10), scaled by an integer.</summary>
    private static void Beads(SpriteBatch b,int x,int midY,float scale,int level)
    {
        float top=midY-9*scale/2,extra=0;
        for(int n=0;n<10;n++)
        {
            bool red=level>n,big=(n+1)%5==0;
            var at=new Vector2(x+n*9*scale+extra,top);
            var shade=big?new Rectangle(145,338,14,9):new Rectangle(129,338,8,9);
            var bead=big?new Rectangle(145+(red?14:0),338,14,9):new Rectangle(129+(red?8:0),338,8,9);
            at=new Vector2((int)at.X,(int)at.Y);
            b.Draw(Game1.mouseCursors,at+new Vector2(-scale,scale),shade,Color.Black*.35f,0f,Vector2.Zero,scale,SpriteEffects.None,.85f);
            b.Draw(Game1.mouseCursors,at,bead,Color.White,0f,Vector2.Zero,scale,SpriteEffects.None,.87f);
            if(big)extra+=6*scale;
        }
    }
    private static int Branch(ProfessionRules.Skill skill,int id)=>skill.Basic.Contains(id)?Array.IndexOf(skill.Basic,id):Array.IndexOf(skill.Advanced,id)/2;
    private static void Slot(SpriteBatch b,ProfessionRules.Skill skill,int id,Rectangle box,bool owned,bool selected)
    {
        bool left=Branch(skill,id)==0;
        Fill(b,box,owned?left?LeftFill:RightFill:LockedFill,.87f);
        if(owned)b.Draw(Game1.mouseCursors,box,ProfessionIcon(id),Color.White,0f,Vector2.Zero,SpriteEffects.None,.88f);
        else Centered(b,"?",box.Center.X,box.Center.Y-Game1.smallFont.MeasureString("?").Y/2f,1f,LockedMark,.88f);
        // 2 px branch-coloured edge (teal left branch, purple right branch) keeps the tree readable around the icon.
        Outline(b,box,owned?2:1,owned?left?LeftEdge:RightEdge:LockedEdge,.89f);
        if(selected){var frame=box;frame.Inflate(2,2);Outline(b,frame,2,Color.White,.9f);}
    }
    internal static bool DrawRows(IClickableMenu page,SpriteBatch b)
    {
        var s=Get(page);if(s==null)return false;
        int pitch=s.Pitch,slot=SlotFor(pitch,s.Bounds.Width),group=2*slot+SlotGap;
        s.NameWidth=(int)Math.Ceiling(ProfessionRules.Skills.Max(k=>Game1.smallFont.MeasureString(Farmer.getSkillDisplayNameFromIndex(k.Id)).X));
        var m=Measure(s.Bounds.Width,slot,s.NameWidth);
        int left=s.Bounds.X;
        // Column headers sit above the first visible row and follow the icon groups.
        Centered(b,ProfessionService.Text("professions.ui.level-header",new{level=5}),left+m.PairX+group/2f,s.Bounds.Y,.8f,HeaderText,.87f);
        Centered(b,ProfessionService.Text("professions.ui.level-header",new{level=10}),left+m.GridX+group/2f,s.Bounds.Y,.8f,HeaderText,.87f);
        if(s.Visible<5)b.DrawString(Game1.smallFont,$"{s.First+1}–{s.First+s.Visible}/5",new Vector2(left+2,s.Bounds.Y),HeaderText,0f,Vector2.Zero,.8f,SpriteEffects.None,.87f);
        for(int row=s.First;row<Math.Min(5,s.First+s.Visible);row++)
        {
            var skill=ProfessionRules.Skills[row];int level=s.Owner.GetUnmodifiedSkillLevel(skill.Id),mid=s.RowTop(row)+pitch/2;
            string name=Fit(Farmer.getSkillDisplayNameFromIndex(skill.Id),m.NameWidth);
            b.DrawString(Game1.smallFont,name,new Vector2(left+m.NameX,mid-Game1.smallFont.MeasureString(name).Y/2f),Game1.textColor,0f,Vector2.Zero,1f,SpriteEffects.None,.87f);
            Beads(b,left+m.BarX,mid,m.BeadScale,level);
            string number=level.ToString();var numberSize=Game1.smallFont.MeasureString(number)*1.25f;
            var numberAt=new Vector2(s.Bounds.Right-numberSize.X-2,mid-numberSize.Y/2f);
            b.DrawString(Game1.smallFont,number,numberAt,Game1.textColor,0f,Vector2.Zero,1.25f,SpriteEffects.None,.87f);
        }
        foreach(var icon in s.Icons)
        {
            var skill=ProfessionRules.Skills.First(k=>k.All.Contains(icon.Id));
            Slot(b,skill,icon.Id,icon.Box,s.Owner.professions.Contains(icon.Id),icon.Id==s.Focus||icon.Id==s.Pinned);
        }
        return true;
    }
    private static bool Hover(SkillsPage __instance,int x,int y,ref string ___hoverText,ref string ___hoverTitle,ref int ___professionImage)
    {
        var s=Get(__instance);if(s==null||HoverCore(s,x,y))return true;
        ___hoverText=___hoverTitle="";___professionImage=-1;return false;
    }
    /// <summary>Updates hover state; true when the pointer is outside the BSV rows and native hover should run.</summary>
    private static bool HoverCore(State s,int x,int y)
    {
        s.Hover=s.Icons.Where(c=>c.Box.Contains(x,y)).Select(c=>c.Id).DefaultIfEmpty(-1).First();
        // The skill name tooltip belongs to the skill icon and name only, not the whole row.
        int row=s.RowAt(y);
        s.HoverSkill=row>=0&&x>=s.Bounds.X&&x<s.Bounds.X+24+s.NameWidth&&Math.Abs(y-(s.RowTop(row)+s.Pitch/2))<=14?row:-1;
        if(!s.Bounds.Contains(x,y)){s.Focus=-1;return true;}
        return false;
    }
    private static bool Click(SkillsPage __instance,int x,int y)=>ClickCore(__instance,x,y);
    private static bool ClickCore(IClickableMenu page,int x,int y)
    {
        var s=Get(page);if(s==null||!s.Bounds.Contains(x,y))return true;
        int id=s.Icons.Where(c=>c.Box.Contains(x,y)).Select(c=>c.Id).DefaultIfEmpty(-1).First();
        s.Pinned=s.Pinned==id?-1:id;s.Focus=id;return false;
    }
    internal static void Move(State s,int direction)
    {
        int[] ids=ProfessionRules.Skills.SelectMany(k=>k.All).ToArray();int index=Array.IndexOf(ids,s.Focus);
        index=index<0?s.First*6:Math.Clamp(index+direction,0,29);s.Focus=ids[index];
        int row=index/6;if(row<s.First)s.First=row;if(row>=s.First+s.Visible)s.First=row-s.Visible+1;Build(s);
    }
    private static bool NativeFocus(IClickableMenu page,State s)=>s.Focus<0&&page.currentlySnappedComponent is {} c
        && !(c.myID is >=0 and <=4 or >=100 and <=104 or >=200 and <=204 or >=60000 and <60030);
    private static bool Navigate(IClickableMenu page,State s,int direction)
    {
        if(NativeFocus(page,s))return false;
        int[] ids=ProfessionRules.Skills.SelectMany(k=>k.All).ToArray();int index=Array.IndexOf(ids,s.Focus);
        if(index>=0&&(index+direction<0||index+direction>=30))
        {
            var next=page.allClickableComponents?.Where(c=>c.myID<60000&&!(c.myID is >=0 and <=4 or >=100 and <=104 or >=200 and <=204))
                .Where(c=>direction>0?c.bounds.Top>=s.Bounds.Bottom:c.bounds.Bottom<=s.Bounds.Top)
                .OrderBy(c=>Math.Abs(c.bounds.Center.Y-s.Bounds.Center.Y)).FirstOrDefault();
            if(next!=null){s.Focus=s.Hover=-1;page.currentlySnappedComponent=next;page.snapCursorToCurrentSnappedComponent();return true;}
            s.Focus=-1;page.setCurrentlySnappedComponentTo(direction<0?0:4);return false;
        }
        Move(s,direction);var box=s.Icons.First(c=>c.Id==s.Focus).Box;
        page.currentlySnappedComponent=new ClickableComponent(box,"BSV profession "+s.Focus){myID=60000+s.Focus};
        Game1.setMousePosition(box.Center);return true;
    }
    private static bool Key(GameMenu __instance,Keys key)
    {
        var page=Page(__instance);var s=page==null?null:Get(page);if(s==null)return true;
        int direction=key switch{Keys.Left=>-1,Keys.Right=>1,Keys.Up=>-6,Keys.Down=>6,_=>0};
        if(direction!=0)return !Navigate(page!,s,direction);
        if(key is Keys.Enter or Keys.Space&&s.Focus>=0){s.Pinned=s.Pinned==s.Focus?-1:s.Focus;return false;}
        if(key==Keys.Escape&&s.Pinned>=0){s.Pinned=-1;return false;}return true;
    }
    private static bool Pad(GameMenu __instance,Buttons __0)
    {
        Buttons b=__0;
        var page=Page(__instance);var s=page==null?null:Get(page);if(s==null)return true;
        int direction=b switch{Buttons.DPadLeft or Buttons.LeftThumbstickLeft=>-1,Buttons.DPadRight or Buttons.LeftThumbstickRight=>1,Buttons.DPadUp or Buttons.LeftThumbstickUp=>-6,Buttons.DPadDown or Buttons.LeftThumbstickDown=>6,_=>0};
        if(direction!=0)return !Navigate(page!,s,direction);
        if(b==Buttons.A){if(NativeFocus(page!,s))return true;if(s.Focus<0)Navigate(page!,s,0);s.Pinned=s.Pinned==s.Focus?-1:s.Focus;return false;}
        if(b==Buttons.B&&s.Pinned>=0){s.Pinned=-1;return false;}return true;
    }
    private static bool Scroll(GameMenu __instance,int direction)
    {
        var page=Page(__instance);var s=page==null?null:Get(page);if(s==null||s.Visible==5||!s.Bounds.Contains(Game1.getMouseX(),Game1.getMouseY()))return true;
        s.First=Math.Clamp(s.First+(direction<0?1:-1),0,5-s.Visible);s.Focus=s.Hover=-1;Build(s);return false;
    }
    private static void Details(IClickableMenu __instance,SpriteBatch b)
    {
        var s=Get(__instance);if(s==null)return;
        int id=s.Pinned>=0?s.Pinned:s.Hover>=0?s.Hover:s.Focus;
        if(id<0){if(s.HoverSkill>=0&&s.HoverSkill<5)IClickableMenu.drawHoverText(b,Farmer.getSkillDisplayNameFromIndex(ProfessionRules.Skills[s.HoverSkill].Id),Game1.smallFont);return;}
        var skill=ProfessionRules.Skills.First(k=>k.All.Contains(id));int level=skill.Basic.Contains(id)?5:10;
        bool owned=s.Owner.professions.Contains(id);
        // Mockup: title + description; locked professions also say when they unlock.
        string text=string.Join("\n",LevelUpMenu.getProfessionDescription(id).Skip(1));
        if(!owned)
        {
            text+="\n\n"+ProfessionService.Text("professions.ui.locked",new{level});
            if(s.Owner.GetUnmodifiedSkillLevel(skill.Id)>=level)text+="\n"+ProfessionService.Text("professions.ui.pending");
        }
        text=Game1.parseText(text,Game1.smallFont,Math.Max(180,Math.Min(380,Game1.uiViewport.Width-96)));
        IClickableMenu.drawHoverText(b,text,Game1.smallFont,0,0,-1,LevelUpMenu.getProfessionTitleFromNumber(id));
    }
}

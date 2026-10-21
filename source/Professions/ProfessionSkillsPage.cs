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
internal static class ProfessionSkillsPage
{
    internal const int RowHeight=68, IconSize=32;
    private static readonly FieldInfo Areas=AccessTools.Field(typeof(SkillsPage),"skillAreas"),Bars=AccessTools.Field(typeof(SkillsPage),"skillBars"),Avatar=AccessTools.Field(typeof(SkillsPage),"playerPanel");
    private static readonly ConditionalWeakTable<SkillsPage,State> States=new();
    internal sealed class State
    {
        internal Farmer Owner=null!;internal Rectangle Bounds;internal int First,Focus=-1,Pinned=-1,Hover=-1,HoverSkill=-1;
        internal readonly List<(int Id,Rectangle Box)> Icons=new();
        internal int Visible=>Math.Max(1,Math.Min(5,Bounds.Height/RowHeight));
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
        } catch {Remove();throw;}
    }
    internal static IEnumerable<CodeInstruction> ReplaceRows(IEnumerable<CodeInstruction> instructions,ILGenerator generator)
    {
        var code=instructions.Select(c=>new CodeInstruction(c)).ToList();
        var bars=code.Select((c,i)=>(c,i)).Where(p=>p.c.opcode==OpCodes.Ldfld&&Equals(p.c.operand,Bars)).Select(p=>p.i).ToArray();
        if(bars.Length!=2)throw new InvalidOperationException("SkillsPage vanilla bar boundaries changed; native UI retained.");
        int end=bars[0]-1;
        var starts=Enumerable.Range(0,end-3).Where(i=>code[i].LoadsConstant(68)&&code[i+1].IsStloc()&&code[i+2].LoadsConstant(0)&&code[i+3].IsStloc()).ToArray();
        if(starts.Length!=1)throw new InvalidOperationException("SkillsPage vanilla row boundary changed; native UI retained.");
        int start=starts[0]+2;
        if(code.Skip(start).Take(end-start).Any(c=>c.blocks.Count>0))throw new InvalidOperationException("SkillsPage unexpected exception region.");
        var finish=generator.DefineLabel();code[end].labels.Add(finish);
        var native=generator.DefineLabel();code[start].labels.Add(native);
        foreach(int i in bars){code[i].opcode=OpCodes.Call;code[i].operand=AccessTools.Method(typeof(ProfessionSkillsPage),nameof(VisibleBars));}
        code.InsertRange(start,new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldarg_1),new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ProfessionSkillsPage),nameof(DrawRows))),new CodeInstruction(OpCodes.Brtrue,finish)});
        return code;
    }
    private static SkillsPage? Page(GameMenu menu)
    {
        var pages=(List<IClickableMenu>)AccessTools.Field(typeof(GameMenu),"pages").GetValue(menu)!;
        return menu.currentTab>=0&&menu.currentTab<pages.Count?pages[menu.currentTab] as SkillsPage:null;
    }
    internal static Rectangle Layout(Rectangle avatar,IEnumerable<Rectangle> rows,Rectangle page,Rectangle viewport)
    {
        var boxes=rows.ToArray();if(boxes.Length!=5)return Rectangle.Empty;
        int left=Math.Max(avatar.Right+24,boxes.Min(b=>b.Left));
        int top=Math.Max(viewport.Top+16,boxes.Min(b=>b.Top));
        int right=Math.Min(page.Right-32,viewport.Right-24);
        int bottom=Math.Min(boxes.Max(b=>b.Bottom)+32,Math.Min(page.Bottom-32,viewport.Bottom-32));
        return right-left<304||bottom-top<RowHeight?Rectangle.Empty:new(left,top,right-left,Math.Min(RowHeight*5,bottom-top));
    }
    private static State? Get(SkillsPage page)
    {
        if(!Installed||page.GetType()!=typeof(SkillsPage))return null;
        var areas=(List<ClickableTextureComponent>)Areas.GetValue(page)!;
        var vanilla=areas.Where(c=>int.TryParse(c.name,out int n)&&n>=0&&n<5).ToArray();
        var rect=Layout((Rectangle)Avatar.GetValue(page)!,vanilla.Select(c=>c.bounds),new(page.xPositionOnScreen,page.yPositionOnScreen,page.width,page.height),new(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height));
        // Respect custom skill components that occupy the same horizontal region.
        foreach(var c in areas.Except(vanilla))if(c.bounds.Left<rect.Right&&c.bounds.Right>rect.Left&&c.bounds.Top>=rect.Top&&c.bounds.Top<rect.Bottom)rect.Height=c.bounds.Top-rect.Top;
        if(rect.Width<304||rect.Height<RowHeight)return null;
        var state=States.GetValue(page,_=>new State{Owner=Game1.player});
        if(state.Owner!=Game1.player){state.Owner=Game1.player;state.Focus=state.Pinned=state.Hover=-1;}
        if(rect!=state.Bounds){state.Bounds=rect;state.First=Math.Clamp(state.First,0,5-state.Visible);Build(state);}
        return state;
    }
    internal static void Build(State s)
    {
        s.Icons.Clear();
        for(int row=s.First;row<Math.Min(5,s.First+s.Visible);row++)
        {
            var ids=ProfessionRules.Skills[row].All.ToArray();
            for(int col=0;col<6;col++)s.Icons.Add((ids[col],new(s.Bounds.X+col*40+(col>=2?16:0),s.Bounds.Y+(row-s.First)*RowHeight+30,IconSize,IconSize)));
        }
    }
    private static List<ClickableTextureComponent> VisibleBars(SkillsPage page)
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
    internal static bool DrawRows(SkillsPage page,SpriteBatch b)
    {
        var s=Get(page);if(s==null)return false;
        for(int row=s.First;row<Math.Min(5,s.First+s.Visible);row++)
        {
            var skill=ProfessionRules.Skills[row];int level=s.Owner.GetUnmodifiedSkillLevel(skill.Id),y=s.Bounds.Y+(row-s.First)*RowHeight;
            // The original five skill sprites are 10x10 pixels in mouseCursors.
            int sprite=skill.Id switch{0=>10,1=>20,2=>60,3=>30,_=>120};
            b.Draw(Game1.mouseCursors,new Rectangle(s.Bounds.X,y,20,20),new Rectangle(sprite,428,10,10),Color.White);
            string name=Farmer.getSkillDisplayNameFromIndex(skill.Id)+" "+level;
            int progressWidth=Math.Min(120,s.Bounds.Width/3);
            b.DrawString(Game1.smallFont,Fit(name,s.Bounds.Width-progressWidth-36),new Vector2(s.Bounds.X+24,y-3),Game1.textColor);
            for(int n=0;n<10;n++)b.Draw(Game1.staminaRect,new Rectangle(s.Bounds.Right-progressWidth+n*(progressWidth/10),y+5,Math.Max(3,progressWidth/10-3),10),n<level?Color.SaddleBrown:Color.Tan);
            string count=skill.All.Count(s.Owner.professions.Contains)+"/6";
            b.DrawString(Game1.smallFont,count,new Vector2(s.Bounds.Right-48,y+32),Game1.textColor);
        }
        foreach(var icon in s.Icons)
        {
            bool owned=s.Owner.professions.Contains(icon.Id);bool selected=icon.Id==s.Focus||icon.Id==s.Pinned;
            var border=icon.Box;border.Inflate(2,2);b.Draw(Game1.staminaRect,border,selected?Color.Goldenrod:owned?Color.SaddleBrown:Color.Gray);
            b.Draw(Game1.mouseCursors,icon.Box,ProfessionIcon(icon.Id),owned?Color.White:Color.White*.28f);
            // Ownership has an additional shape marker; never rely on colour alone.
            if(owned){b.Draw(Game1.staminaRect,new Rectangle(icon.Box.Right-7,icon.Box.Bottom-5,5,3),Color.White);b.Draw(Game1.staminaRect,new Rectangle(icon.Box.Right-4,icon.Box.Bottom-9,2,7),Color.White);}
        }
        if(s.Visible<5&&s.Bounds.Height-s.Visible*RowHeight>=22)b.DrawString(Game1.smallFont,$"{s.First+1}–{s.First+s.Visible}/5",new Vector2(s.Bounds.Right-74,s.Bounds.Bottom-22),Game1.textColor);
        return true;
    }
    private static bool Hover(SkillsPage __instance,int x,int y,ref string ___hoverText,ref string ___hoverTitle,ref int ___professionImage)
    {
        var s=Get(__instance);if(s==null)return true;
        s.Hover=s.Icons.Where(c=>c.Box.Contains(x,y)).Select(c=>c.Id).DefaultIfEmpty(-1).First();
        s.HoverSkill=s.Bounds.Contains(x,y)&&((y-s.Bounds.Top)%RowHeight)<28?s.First+(y-s.Bounds.Top)/RowHeight:-1;
        if(!s.Bounds.Contains(x,y)){s.Focus=-1;return true;}
        ___hoverText=___hoverTitle="";___professionImage=-1;return false;
    }
    private static bool Click(SkillsPage __instance,int x,int y)
    {
        var s=Get(__instance);if(s==null||!s.Bounds.Contains(x,y))return true;
        int id=s.Icons.Where(c=>c.Box.Contains(x,y)).Select(c=>c.Id).DefaultIfEmpty(-1).First();
        s.Pinned=s.Pinned==id?-1:id;s.Focus=id;return false;
    }
    internal static void Move(State s,int direction)
    {
        int[] ids=ProfessionRules.Skills.SelectMany(k=>k.All).ToArray();int index=Array.IndexOf(ids,s.Focus);
        index=index<0?s.First*6:Math.Clamp(index+direction,0,29);s.Focus=ids[index];
        int row=index/6;if(row<s.First)s.First=row;if(row>=s.First+s.Visible)s.First=row-s.Visible+1;Build(s);
    }
    private static bool NativeFocus(SkillsPage page,State s)=>s.Focus<0&&page.currentlySnappedComponent is {} c
        && !(c.myID is >=0 and <=4 or >=100 and <=104 or >=200 and <=204 or >=60000 and <60030);
    private static bool Navigate(SkillsPage page,State s,int direction)
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
    private static void Details(SkillsPage __instance,SpriteBatch b)
    {
        var s=Get(__instance);if(s==null)return;
        int id=s.Pinned>=0?s.Pinned:s.Hover>=0?s.Hover:s.Focus;
        if(id<0){if(s.HoverSkill>=0&&s.HoverSkill<5)IClickableMenu.drawHoverText(b,Farmer.getSkillDisplayNameFromIndex(ProfessionRules.Skills[s.HoverSkill].Id),Game1.smallFont);return;}
        var skill=ProfessionRules.Skills.First(k=>k.All.Contains(id));int level=skill.Basic.Contains(id)?5:10;
        bool owned=s.Owner.professions.Contains(id);
        string status=ProfessionService.Text(owned?"professions.ui.owned":"professions.ui.locked",new{level});
        string text=string.Join("\n",LevelUpMenu.getProfessionDescription(id).Skip(1))+"\n\n"+status;
        if(!owned&&s.Owner.GetUnmodifiedSkillLevel(skill.Id)>=level)text+="\n"+ProfessionService.Text("professions.ui.pending");
        text=Game1.parseText(text,Game1.smallFont,Math.Max(180,Math.Min(380,Game1.uiViewport.Width-96)));
        IClickableMenu.drawHoverText(b,text,Game1.smallFont,0,0,-1,LevelUpMenu.getProfessionTitleFromNumber(id));
    }
}

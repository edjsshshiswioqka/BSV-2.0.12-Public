using BetterStardewValley.Professions;
using Microsoft.Xna.Framework;
using StardewValley;
partial class Program
{
 static void SkillsR12Checks()
 {
  if(!ProfessionSkillsPage.Installed)Console.Error.WriteLine(string.Join("\n",Recorder.Logs));
  Check(ProfessionSkillsPage.Installed,"R12 native SkillsPage transpiler and input patches installed");
  foreach(var viewport in new[]{new Rectangle(0,0,1280,720),new(0,0,800,600),new(0,0,640,360)})
  {
   var rows=Enumerable.Range(0,5).Select(i=>new Rectangle(240,80+i*68,148,36));
   var area=ProfessionSkillsPage.Layout(new(64,80,128,192),rows,new(0,0,viewport.Width,viewport.Height),viewport);
   Check(area.Left>=216&&area.Right<=viewport.Right-24&&area.Bottom<=viewport.Bottom-32,"R12 skill layout respects avatar and viewport "+viewport);
   var state=new ProfessionSkillsPage.State{Owner=Host,Bounds=area};ProfessionSkillsPage.Build(state);
   Check(state.Icons.Count==state.Visible*6,"R12 six distinct profession hitboxes in every visible row");
   Check(state.Icons.All(c=>area.Contains(c.Box))&&state.Icons.All(a=>state.Icons.All(b=>a.Id==b.Id||!a.Box.Intersects(b.Box))),"R12 icons remain visible with disjoint hitboxes");
   for(int row=0;row<state.Visible;row++)
   {
    // Per row: two level-5 icons side by side, then the level-10 professions as a 2x2 grid.
    var r=state.Icons.Skip(row*6).Take(6).Select(c=>c.Box).ToArray();
    Check(r[0].Y==r[1].Y&&r[0].Right<r[1].X,"R13 level-5 professions sit side by side "+viewport);
    Check(r[2].X==r[3].X&&r[4].X==r[5].X&&r[2].Y==r[4].Y&&r[3].Y==r[5].Y&&r[2].Bottom<r[3].Y&&r[2].Right<r[4].X,"R13 level-10 professions form a 2x2 grid "+viewport);
    Check(r[1].Right<r[2].X,"R13 level-5 group precedes the level-10 grid "+viewport);
    Check(r.All(b=>b.Top>=state.RowTop(row)&&b.Bottom<=state.RowTop(row)+state.Pitch),"R13 icons stay inside their own row "+viewport);
   }
   for(int n=0;n<31;n++)ProfessionSkillsPage.Move(state,1);
   Check(state.Focus==29&&state.Icons.Any(c=>c.Id==29),"R12 controller focus scrolls last profession into view");
   for(int n=0;n<31;n++)ProfessionSkillsPage.Move(state,-1);
   Check(state.Focus==0&&state.First==0,"R12 reverse navigation returns to first profession");
  }
  // Horizontal arrangement: bar, level-5 pair, level-10 grid and level number never overlap at any supported width.
  foreach(int width in new[]{ProfessionSkillsPage.MinWidth,368,420,528,640,800,1000})
  foreach(int pitch in new[]{ProfessionSkillsPage.MinPitch,54,60,ProfessionSkillsPage.MaxPitch})
  foreach(int nameWidth in new[]{24,72,140,400})
  {
   int slot=ProfessionSkillsPage.SlotFor(pitch,width),group=2*slot+ProfessionSkillsPage.SlotGap;
   var m=ProfessionSkillsPage.Measure(width,slot,nameWidth);
   string at=$" width {width} pitch {pitch} name {nameWidth}";
   Check(slot is >=24 and <=32&&2*slot+ProfessionSkillsPage.SlotGap<=pitch,"R13 2x2 grid fits one row"+at);
   Check(m.BeadScale is >=1 and <=4&&m.BarX+ProfessionSkillsPage.BeadSpan*m.BeadScale<=m.PairX-8,"R13 level bar ends before the level-5 pair"+at);
   Check(m.PairX+group+ProfessionSkillsPage.GroupGap<=m.GridX&&m.GridX+group<=m.NumberX&&m.NumberX+ProfessionSkillsPage.NumberWidth==width,"R13 pair, grid and number are ordered inside the width"+at);
   Check(m.NameX+m.NameWidth<=m.BarX&&m.NameWidth>=24,"R13 skill name never runs into the bar"+at);
  }
  Check(ProfessionSkillsPage.PitchFor(0)==ProfessionSkillsPage.MinPitch&&ProfessionSkillsPage.PitchFor(10000)==ProfessionSkillsPage.MaxPitch,"R13 row pitch is bounded");
  var page=(StardewValley.Menus.SkillsPage)Blank(typeof(StardewValley.Menus.SkillsPage));page.width=1000;page.height=720;
  var areas=Enumerable.Range(0,5).Select(i=>new StardewValley.Menus.ClickableTextureComponent(i.ToString(),new Rectangle(240,80+i*68,148,36),"","",Game1.mouseCursors,Rectangle.Empty,1f)).ToList();
  Set(page,"skillAreas",areas);Set(page,"playerPanel",new Rectangle(64,80,128,192));
  var get=typeof(ProfessionSkillsPage).GetMethod("Get",Any)!;
  var actual=(ProfessionSkillsPage.State)get.Invoke(null,new object[]{page})!;
  Check(actual.Visible==5&&actual.Icons.Count==30&&actual.Owner==Host,"R12 actual SkillsPage adapter reads current player and shows all five rows");
  areas.Add(new StardewValley.Menus.ClickableTextureComponent("CustomSkill",new Rectangle(240,300,300,36),"","",Game1.mouseCursors,Rectangle.Empty,1f));
  actual=(ProfessionSkillsPage.State)get.Invoke(null,new object[]{page})!;
  Check(actual.Bounds.Bottom<=300&&actual.Icons.All(c=>c.Box.Bottom<=300),"R12 actual adapter reserves overlapping custom skill area");
  var expected=new HashSet<int>(Enumerable.Range(0,30));
  Check(ProfessionRules.Skills.SelectMany(s=>s.All).ToHashSet().SetEquals(expected),"R12 UI enumerates exact thirty native IDs once");
  foreach(int id in expected)Check(ProfessionSkillsPage.ProfessionIcon(id)==new Rectangle(id%6*16,624+id/6*16,16,16),"R12 native icon mapping "+id);
 }
}

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
   for(int n=0;n<31;n++)ProfessionSkillsPage.Move(state,1);
   Check(state.Focus==29&&state.Icons.Any(c=>c.Id==29),"R12 controller focus scrolls last profession into view");
   for(int n=0;n<31;n++)ProfessionSkillsPage.Move(state,-1);
   Check(state.Focus==0&&state.First==0,"R12 reverse navigation returns to first profession");
  }
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

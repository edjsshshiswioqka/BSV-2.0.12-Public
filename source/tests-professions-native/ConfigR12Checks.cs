using BetterStardewValley;
using BetterStardewValley.Configuration;
using BetterStardewValley.Professions;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
partial class Program
{
 static void ConfigR12Checks(IModHelper helper,IMonitor monitor,ProfessionService service,Harmony h)
 {
  typeof(Context).GetProperty("IsWorldReady",Any)!.SetValue(null,true);
  h.Patch(AccessTools.PropertyGetter(typeof(Context),"IsMainPlayer"),prefix:new(typeof(Program),nameof(Yes)));
  string dir=Path.Combine(Path.GetTempPath(),"bsv-r12-config-"+Guid.NewGuid());Directory.CreateDirectory(dir);Recorder.ModuleDirectory=dir;
  var live=new ModConfig();Check(live.EnableAllProfessions,"R12 fresh configuration defaults to ON");
  SharedPreferences.Register(helper,live);
  var menu=typeof(ConfigMenu).GetConstructors(Any).Single().Invoke(new object[]{helper,monitor,Recorder.Make(typeof(IManifest)),live});
  void Save(bool enabled)
  {((ModConfig)AccessTools.Field(typeof(ConfigMenu),"draft").GetValue(menu)!).EnableAllProfessions=enabled;AccessTools.Method(typeof(ConfigMenu),"Save").Invoke(menu,null);}
  Host=New();SetPlayer(Host);Farmers=new[]{Host};SetLevel(Host,1,5);Host.newLevels.Add(new(1,5));
  Save(false);
  Check(!live.EnableAllProfessions&&!ProfessionService.Active&&File.ReadAllText(Path.Combine(dir,"config.json")).Contains("\"EnableAllProfessions\": false"),"R12 real GMCM Save writes OFF and applies it immediately");
  var native=new LevelUpMenu(1,5);Check(native.isProfessionChooser,"R12 OFF keeps native two-choice upgrade");
  Host.professions.Add(6);Host.professions.Add(7);SetLevel(Host,1,10);Host.newLevels.Clear();Host.newLevels.Add(new(1,10));
  var advanced=new LevelUpMenu(1,10);Check(advanced.isProfessionChooser&&advanced.leftProfession!=null&&advanced.rightProfession!=null,"R12 OFF with both basics still constructs usable native advanced chooser");
  Game1.game1.zoomModifier=1f;Game1.options.baseZoomLevel=1f;Game1.options.baseUIScale=1f;Game1.uiMode=true;
  Set(advanced,"timerBeforeStart",0);InputMouse=default;
  advanced.update(new Microsoft.Xna.Framework.GameTime(TimeSpan.FromSeconds(1),TimeSpan.FromMilliseconds(16)));
  InputMouse=new Microsoft.Xna.Framework.Input.MouseState(advanced.xPositionOnScreen+advanced.width/4,advanced.yPositionOnScreen+advanced.height-32,0,Microsoft.Xna.Framework.Input.ButtonState.Pressed,Microsoft.Xna.Framework.Input.ButtonState.Released,Microsoft.Xna.Framework.Input.ButtonState.Released,Microsoft.Xna.Framework.Input.ButtonState.Released,Microsoft.Xna.Framework.Input.ButtonState.Released);
  AccessTools.Field(typeof(InputState),"_currentMouseState").SetValue(Game1.input,InputMouse);
  advanced.update(new Microsoft.Xna.Framework.GameTime(TimeSpan.FromSeconds(1),TimeSpan.FromMilliseconds(16)));InputMouse=default;
  Check(!advanced.isActive&&!Host.newLevels.Contains(new Microsoft.Xna.Framework.Point(1,10))&&Host.professions.Count==3,"R12 OFF native mouse choice completes level 10 while retaining both basics");
  Host.newLevels.Clear();Save(true);
  Check(live.EnableAllProfessions&&ProfessionService.Active,"R12 real GMCM Save applies ON without reload");
  // Invoke the same event handler target; graphics-only HUD notification is suppressed.
  h.Patch(AccessTools.Method(typeof(Game1),"addHUDMessage"),prefix:new(typeof(Program),nameof(Skip)));
  AccessTools.Method(typeof(ProfessionService),"MigrateLocal").Invoke(service,null);
  Check(ProfessionRules.Find(1)!.All.All(Host.professions.Contains)&&Host.professions.Count==6,"R12 enabling catches up eligible missing professions");
  int hp=Host.maxHealth;Save(false);Save(true);AccessTools.Method(typeof(ProfessionService),"MigrateLocal").Invoke(service,null);
  Check(Host.professions.Count==6&&Host.maxHealth==hp,"R12 repeated OFF/ON retains IDs and does not increase health");
  AccessTools.Field(typeof(SharedPreferences),"local").SetValue(null,null);Recorder.ModuleDirectory=null;Directory.Delete(dir,true);
 }
}

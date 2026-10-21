using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using BetterStardewValley.Professions;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.GameData.Objects;
using StardewValley.ItemTypeDefinitions;
using SObject=StardewValley.Object;
partial class Program
{
 static readonly BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static List<string> Checks=new();static Farmer Host=null!;static Farmer[] Farmers=Array.Empty<Farmer>();static string LastDialogue="";
 static void Check(bool b,string s){if(!b)throw new Exception(s);Checks.Add(s);}
 static bool Skip()=>false;
 static bool Yes(ref bool __result){__result=true;return false;}
 static bool Master(ref Farmer __result){__result=Host;return false;}
 static bool Everyone(ref IEnumerable<Farmer> __result){__result=Farmers;return false;}
 static bool Text(string path,ref string __result){__result=path;return false;}
 static bool Dialogue(string dialogue){LastDialogue=dialogue;return false;}
 static bool Source(ref Rectangle __result){__result=new(0,0,16,16);return false;}
 static MouseState InputMouse;
 static bool Mouse(ref MouseState __result){__result=InputMouse;return false;}
 static object Blank(Type t){var o=FormatterServices.GetUninitializedObject(t);GC.SuppressFinalize(o);return o;}
 sealed class Services:IServiceProvider{public object? GetService(Type t)=>null;}
 sealed class Content:LocalizedContentManager
 {internal Content():base(new Services(),"."){} public override T Load<T>(string n)=>(T)(object)(n.Replace('\\','/') switch {
   "LooseSprites/Cursors"=>Game1.mouseCursors,"Data/Objects"=>Game1.objectData,"Data/Machines"=>new Dictionary<string,StardewValley.GameData.Machines.MachineData>(),
   "Data/ObjectContextTags" or "Data/Furniture" or "Data/Fish" or "Strings/StringsFromCSFiles"=>new Dictionary<string,string>(),_=>throw new Exception("missing fixture "+n)});}
 static void SetPlayer(Farmer who)=>typeof(Game1).GetField("_player",Any)!.SetValue(null,who);
 static void SetLevel(Farmer who,int skill,int level)
 {switch(skill){case 0:who.farmingLevel.Value=level;break;case 1:who.fishingLevel.Value=level;break;case 2:who.foragingLevel.Value=level;break;case 3:who.miningLevel.Value=level;break;case 4:who.combatLevel.Value=level;break;default:who.luckLevel.Value=level;break;}}
 static Farmer New(int id=1){var p=new Farmer{UniqueMultiplayerID=id};p.buffs.SetOwner(p);p.buffs.Dirty=false;p.maxHealth=100;p.health=33;p.Stamina=42;return p;}
 static void Set(object o,string name,object value)=>o.GetType().GetField(name,Any)!.SetValue(o,value);
 static void Ok(LevelUpMenu menu)=>typeof(LevelUpMenu).GetMethod("okButtonClicked",Any)!.Invoke(menu,null);
 static LevelUpMenu Page(int skill,int level)
 {
   var m=new LevelUpMenu(skill,level);
   Check(!m.isProfessionChooser && m.isActive && m.informationUp,$"native ctor -> ordinary confirmation {skill}/{level}");return m;
 }
 static void Main()
 {
  Game1.game1=(Game1)Blank(typeof(Game1));Game1.game1._locations=new List<GameLocation>();Game1.otherFarmers=new();Game1._locationLookup=new();Game1.random=new Random(37);
  typeof(Game1).GetField("log",Any)!.SetValue(null,Recorder.Make(typeof(StardewValley.Logging.IGameLogger)));
  Game1.options=(Options)Blank(typeof(Options));Game1.content=new Content();Game1.uiViewport=new(0,0,1280,720);Game1.mouseCursors=(Texture2D)Blank(typeof(Texture2D));
  foreach(var f in typeof(Options).GetFields(Any).Where(f=>f.FieldType==typeof(InputButton[])))f.SetValue(Game1.options,Array.Empty<InputButton>());
  Game1.options.menuButton=new[]{new InputButton(Keys.Escape)};
  foreach(var f in typeof(Texture2D).GetFields(Any).Where(f=>f.FieldType==typeof(int)))
   if(f.Name.Contains("width",StringComparison.OrdinalIgnoreCase)||f.Name.Contains("height",StringComparison.OrdinalIgnoreCase))f.SetValue(Game1.mouseCursors,1024);
  var h=new Harmony("BSV.Professions.Headless");
  void Patch(Type t,string name,string callback,params Type[] types)=>h.Patch(t.GetMethod(name,Any,null,types,null)!,prefix:new(typeof(Program),callback));
  Patch(typeof(Farmer),"farmerInit",nameof(Skip));Patch(typeof(AnimatedSprite),"LoadTexture",nameof(Skip),typeof(string),typeof(bool));
  Patch(typeof(Context),"get_IsWorldReady",nameof(Yes));Patch(typeof(Game1),"get_MasterPlayer",nameof(Master));Patch(typeof(Game1),"get_IsMasterGame",nameof(Yes));
  Patch(typeof(Game1),"getAllFarmers",nameof(Everyone));Patch(typeof(Farmer),"isActive",nameof(Yes));
  Patch(typeof(StardewValley.BellsAndWhistles.PlayerStatusList),"AddSpriteDefinition",nameof(Skip),typeof(string),typeof(string),typeof(int),typeof(int),typeof(int),typeof(int));
  Patch(typeof(StardewValley.BellsAndWhistles.PlayerStatusList),"UpdateState",nameof(Skip),typeof(string));
  Patch(typeof(Stats),"checkForSkillAchievements",nameof(Skip),typeof(bool));Patch(typeof(Farmer),"AddMissedMailAndRecipes",nameof(Skip));
  Patch(typeof(Farmer),"completelyStopAnimatingOrDoingAction",nameof(Skip));Patch(typeof(LocalizedContentManager),"LoadString",nameof(Text),typeof(string));
  Patch(typeof(Game1),"getSourceRectForStandardTileSheet",nameof(Source),typeof(Texture2D),typeof(int),typeof(int),typeof(int));
  Patch(typeof(Game1),"drawObjectDialogue",nameof(Dialogue),typeof(string));
  Game1.input=(InputState)Blank(typeof(InputState));Patch(typeof(InputState),"GetMouseState",nameof(Mouse));
  Game1.netWorldState=new Netcode.NetRoot<StardewValley.Network.NetWorldState>(new StardewValley.Network.NetWorldState());
  Host=New();SetPlayer(Host);Farmers=new[]{Host};CraftingRecipe.craftingRecipes=new();CraftingRecipe.cookingRecipes=new();
  var helper=(IModHelper)Recorder.Make(typeof(IModHelper));var monitor=(IMonitor)Recorder.Make(typeof(IMonitor));
  var service=new ProfessionService(helper,monitor,"BSV.Profession.Tests",_=>{});
  Check(service.Install(),"actual production Harmony targets install exactly on 1.6.15");
  SkillsR12Checks();
  Check(typeof(Farmer).Module.ModuleVersionId.ToString()==ProfessionService.AuditedMvid,"audited game MVID");
  foreach(var skill in ProfessionRules.Skills)
  {
   Host=New();SetPlayer(Host);Farmers=new[]{Host};SetLevel(Host,skill.Id,4);
   Check(ProfessionGrant.Migrate(Host)==0,$"level 4 does not grant {skill.Name}");
   SetLevel(Host,skill.Id,5);Host.newLevels.Add(new(skill.Id,5));
   Check(ProfessionGrant.Migrate(Host)==0 && Host.professions.Count==0,$"queued level 5 deferred {skill.Name}");
   var page=Page(skill.Id,5);Check(Host.professions.Count==0,"opening menu cannot grant early "+skill.Name);
   int hp=Host.maxHealth;Ok(page);
   Check(skill.Basic.All(Host.professions.Contains)&&Host.professions.Count==2,"both basic professions "+skill.Name);
   Check(Host.maxHealth==hp+(skill.Id==4?15:0),"native stage HP no extra combat +5 "+skill.Name);
   Check(!Host.newLevels.Contains(new(skill.Id,5))&&!page.isActive,"native queue removes only confirmed stage "+skill.Name);
   Host.health=17;Host.Stamina=19;Ok(page);Check(Host.health==17&&Host.Stamina==19,"repeated confirmation never heals "+skill.Name);
   SetLevel(Host,skill.Id,10);Host.newLevels.Add(new(skill.Id,10));
   Check(ProfessionGrant.Migrate(Host)==0,"pending advanced migration deferred "+skill.Name);
   page=Page(skill.Id,10);hp=Host.maxHealth;Ok(page);
   Check(skill.All.All(Host.professions.Contains)&&Host.professions.Count==6,"both advanced branches all six "+skill.Name);
   Check(Host.maxHealth==hp+(skill.Id==4?25:0),"advanced stage HP once "+skill.Name);
   Host.health=13;Host.Stamina=21;for(int i=0;i<10;i++)ProfessionGrant.Migrate(Host);
   Check(Host.health==13&&Host.Stamina==21,"migration no recovery "+skill.Name);
  }
  Migration();MenuBoundaries();Prices();Recipes(h);MetadataChecks.Run(Checks);
  ConfigR12Checks(helper,monitor,service,h);
  foreach(string key in new[]{"farming","mining","foraging","fishing","combat"})
  {int gold=Host.Money;var before=Host.professions.ToArray();Check(new GameLocation().answerDialogueAction("professionForget_"+key,Array.Empty<string>()),"native statue response handled "+key);
   Check(Host.Money==gold&&before.SequenceEqual(Host.professions)&&LastDialogue==NativeTranslationFixture.Get("professions.statue").ToString(),"statue preserves money and professions and shows localized explanation "+key);}
  Check(!ProfessionService.IsVanillaRespec("professionForget_custom")&&!ProfessionService.IsVanillaRespec("dogStatue_Yes"),"foreign statue entries stay reachable");
  var foreign=new Harmony("Example.OverlappingProfessionMod");foreign.Patch(typeof(LevelUpMenu).GetMethod("getImmediateProfessionPerk",Any)!,prefix:new(typeof(Program),nameof(Skip)));
  Check(!service.Install()&&!ProfessionService.Enabled,"actual conflicting grant patch disables module before grants");
  Check(!Harmony.GetAllPatchedMethods().Any(m=>Harmony.GetPatchInfo(m)!.Owners.Contains("BSV.Profession.Tests.AllProfessions")),"conflict rollback leaves no partial All Professions patches");
  Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=Checks,real_game_assembly=true,headless=true,content="explicit fixtures, no shipped XNB",live_game=false,multiplayer_session=false}));
 }
 static void Migration()
 {
  Host=New();SetPlayer(Host);Farmers=new[]{Host};foreach(var s in ProfessionRules.Skills){SetLevel(Host,s.Id,10);Host.professions.Add(s.Basic[0]);Host.professions.Add(s.Advanced[0]);}
  Host.maxHealth=255;Host.health=31;Host.Stamina=42;Host.professions.Add(1234);Host.modData["OtherMod/Data"]="keep";
  Check(ProfessionGrant.Migrate(Host)==20,"old save one branch per skill adds 20 missing");
  Check(Enumerable.Range(0,30).All(Host.professions.Contains)&&Host.professions.Count==31,"30 vanilla IDs plus foreign ID");
  Check(Host.maxHealth==280&&Host.health==31&&Host.Stamina==42,"missing Defender adds 25, preserves foreign max HP and current HP/energy");
  for(int i=0;i<20;i++)Check(ProfessionGrant.Migrate(Host)==0&&Host.maxHealth==280&&Host.health==31,"repeated load/day/reconnect idempotency "+i);
  Check(Host.modData["OtherMod/Data"]=="keep"&&Host.stats.Get("MasteryExp")==0,"foreign data and mastery experience untouched");
  var guest=New(2);SetLevel(guest,1,4);Check(ProfessionGrant.Migrate(guest)==0&&guest.professions.Count==0,"host cannot grant to remote guest");
  SetPlayer(guest);Check(ProfessionGrant.Migrate(guest)==0,"new local farmhand does not inherit host professions");
  SetLevel(guest,1,8);Check(ProfessionGrant.Migrate(guest)==2&&guest.professions.SetEquals(new[]{6,7}),"owning farmhand only its level 8 basics");
  SetPlayer(Host);
  var old=New(3);SetPlayer(old);SetLevel(old,4,10);old.professions.Add(24);old.professions.Add(27);old.maxHealth=100;old.health=12;
  Check(ProfessionGrant.Migrate(old)==4&&old.maxHealth==180&&old.health==12,"existing HP professions use native floor repair without healing or double perks");
  Check(ProfessionGrant.Migrate(old)==0&&old.maxHealth==180,"native floor repair repeat safe");
  Host=New();SetPlayer(Host);Farmers=new[]{Host};SetLevel(Host,1,4);
  ((StardewValley.Buffs.BuffEffects)typeof(StardewValley.Buffs.BuffManager).GetField("CombinedEffects",Any)!.GetValue(Host.buffs)!).FishingLevel.Value=6;
  Check(Host.FishingLevel>=10&&ProfessionGrant.Migrate(Host)==0,"temporary Fishing buff cannot unlock professions");
 }
 static void MenuBoundaries()
 {
  Host=New();SetPlayer(Host);Farmers=new[]{Host};SetLevel(Host,4,10);
  foreach(int n in Enumerable.Range(1,10))Host.newLevels.Add(new(4,n));Host.newLevels.Add(new(0,3));Host.newLevels.Add(new(77,5));
  Check(ProfessionGrant.Migrate(Host)==0,"mass XP queue not consumed by migration");
  foreach(int n in Enumerable.Range(1,10))
  {var m=new LevelUpMenu(4,n);if(n is 5 or 10)Check(!m.isProfessionChooser,"milestone confirmation in mass queue "+n);Ok(m);}
  Check(Host.maxHealth==180&&Host.professions.Count==6,"ten combat levels give native 40 level HP plus 15 Fighter plus 25 Defender");
  Check(Host.newLevels.Count==2&&Host.newLevels.Contains(new(77,5))&&Host.newLevels.Contains(new(0,3)),"ordinary other skill and custom queue entries retained");
  var custom=new LevelUpMenu(77,5);Check(custom.isProfessionChooser,"non-vanilla skill menu not converted");
  // A queued historical respec has the same native record shape. Do not guess payment or refund.
  Host.professions.Clear();Host.maxHealth=140;Host.health=12;Host.Stamina=11;Host.newLevels.Add(new(4,5));Host.newLevels.Add(new(4,10));
  Check(ProfessionGrant.Migrate(Host)==0&&Host.newLevels.Contains(new(4,5)),"old respec deferred to confirmation");
  Ok(Page(4,5));Ok(Page(4,10));Check(Host.maxHealth==180&&Host.professions.Count==6,"historical respec restores removed permanent bonuses exactly once");
  // Confirm a duplicate pending stage via two independent menu instances.
  Host.newLevels.Add(new(4,10));var a=Page(4,10);var b=Page(4,10);Host.health=10;Host.Stamina=10;Ok(a);Ok(b);
  Check(Host.health==10&&Host.Stamina==10&&Host.maxHealth==180&&!b.isActive,"persisted settled stage retires duplicate page without recovery or queue loop");
  // Exercise the actual parameterless menu's late page creation in update, with input supplied by a headless boundary.
  Host.newLevels.Clear();SetLevel(Host,1,5);Host.newLevels.Add(new(1,5));
  var legacy=new LevelUpMenu();Set(legacy,"timerBeforeStart",0);legacy.isActive=true;
  // The parameterless native constructor is inert. Supply its legacy star-entry host UI,
  // without assigning currentSkill/currentLevel or constructing the resulting page ourselves.
  var star=new ClickableTextureComponent(new Rectangle(400,300,64,64),Game1.mouseCursors,new Rectangle(0,0,16,16),1f);
  Set(legacy,"starIcon",star);
  InputMouse=new MouseState(star.bounds.Center.X,star.bounds.Center.Y,0,ButtonState.Pressed,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released);
  legacy.update(new GameTime(TimeSpan.FromSeconds(1),TimeSpan.FromMilliseconds(16)));InputMouse=default;
  Check(legacy.informationUp&&!legacy.isProfessionChooser&&Host.professions.Contains(6)==false,"actual parameterless update creates a converted page without early grant");
  Ok(legacy);Check(Host.professions.Contains(6)&&Host.professions.Contains(7),"parameterless entry confirms same grant service");
  // Native gamepad B/Start invokes okButtonClicked on the ordinary page.
  SetLevel(Host,3,5);Host.newLevels.Add(new(3,5));var pad=Page(3,5);pad.receiveGamePadButton(Buttons.B);
  Check(Host.professions.Contains(18)&&Host.professions.Contains(19),"native gamepad B confirms without choosing profession");
  SetLevel(Host,2,5);Host.newLevels.Add(new(2,5));var keys=Page(2,5);keys.receiveKeyPress(Keys.Escape);
  Check(Host.professions.Contains(12)&&Host.professions.Contains(13),"native keyboard Escape confirms without choosing profession");
 }
 static void Prices()
 {
  Host=New();SetPlayer(Host);Farmers=new[]{Host};Host.professions.UnionWith(Enumerable.Range(0,30));
  Game1.objectData=new Dictionary<string,ObjectData>();ItemRegistry.AddTypeDefinition(new ObjectDataDefinition());
  foreach(var row in new[]{("fish",-4,150f),("crop",-75,110f),("Egg",-5,120f),("Cheese",-26,168f),("wine",-26,140f),("sap",-27,125f),("gem",-2,130f)})
  {Game1.objectData[row.Item1]=new(){Name=row.Item1,DisplayName=row.Item1,Type="Basic",Category=row.Item2,Price=100};var item=ItemRegistry.Create<SObject>("(O)"+row.Item1);
   Check(Math.Abs(Price(item)-row.Item3)<.01,"actual native all-profession price "+row.Item1+"="+row.Item3);}
  var other=New(2);other.professions.UnionWith(Enumerable.Range(0,30));Farmers=new[]{Host,other};
  Check(Math.Abs(Price(ItemRegistry.Create<SObject>("(O)fish"))-150)<.01,"shared-wallet maximum participant bonus, not multiplied by players");
  CraftingRecipe.craftingRecipes["Crab Pot"]="388 40 335 3/Field/fixture/false/Fishing 3/Crab Pot";
  var trapper=new CraftingRecipe("Crab Pot");
  Check(trapper.recipeList.Count==2&&trapper.recipeList["388"]==25&&trapper.recipeList["334"]==2,"native Trapper recipe uses 25 Wood and 2 Copper Bars with all professions");
  var pot=new StardewValley.Objects.CrabPot();
  Check(!(bool)typeof(StardewValley.Objects.CrabPot).GetMethod("NeedsBait",Any)!.Invoke(pot,new object[]{Host})!,"native Luremaster removes bait requirement when all professions coexist");
  CraftingRecipe.craftingRecipes.Clear();
 }
 static void Recipes(Harmony harness)
 {
  Host=New();SetPlayer(Host);Farmers=new[]{Host};SetLevel(Host,0,5);Host.newLevels.Add(new(0,5));
  Game1.objectData["fixture"]=new(){Name="Fixture",DisplayName="Fixture",Description="Recipe test output",Type="Basic",Category=0,Price=1};
  CraftingRecipe.craftingRecipes["ProfessionCraftFixture"]="388 1/Field/fixture/false/Farming 5/Fixture";
  CraftingRecipe.cookingRecipes["ProfessionCookFixture"]="388 1/0/fixture/Farming 5/Fixture";
  var native=typeof(Farmer).GetMethod("AddMissedMailAndRecipes",Any)!;
  harness.Unpatch(native,HarmonyPatchType.Prefix,harness.Id);
  var page=Page(0,5);
  Check(Host.craftingRecipes.ContainsKey("ProfessionCraftFixture")&&Host.cookingRecipes.ContainsKey("ProfessionCookFixture"),"native constructor grants both crafting and cooking recipes unchanged");
  Check(((List<CraftingRecipe>)typeof(LevelUpMenu).GetField("newCraftingRecipes",Any)!.GetValue(page)!).Count==2,"converted confirmation retains both native recipe entries");
  // Native cleanup path is another confirmation entry, not a second award mechanism.
  typeof(LevelUpMenu).GetMethod("cleanupBeforeExit",Any)!.Invoke(page,null);
  Check(Host.professions.SetEquals(new[]{0,1})&&!page.isActive,"native cleanup confirms same stage once");
  Host.craftingRecipes["ProfessionCraftFixture"]=7;Host.health=9;
  typeof(LevelUpMenu).GetMethod("cleanupBeforeExit",Any)!.Invoke(page,null);
  Check(Host.health==9&&Host.craftingRecipes["ProfessionCraftFixture"]==7,"repeat cleanup preserves recipe counters and does not heal");
  harness.Patch(native,prefix:new(typeof(Program),nameof(Skip)));
  CraftingRecipe.craftingRecipes.Clear();CraftingRecipe.cookingRecipes.Clear();
 }
 static float Price(SObject item)=>(float)typeof(SObject).GetMethod("getPriceAfterMultipliers",Any,null,new[]{typeof(float),typeof(long)},null)!.Invoke(item,new object[]{100f,-1L})!;
}

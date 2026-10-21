using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using StardewValley.Objects;
using StardewValley.Menus;
using StardewModdingAPI.Events;
namespace BetterStardewValley;
internal sealed partial class TeaFeature {
 internal void TestUnlock()=>Unlock();internal void TestWorld()=>World();internal static void TestNew(Bush b)=>NewBush(b);
 internal static bool TestDay(Bush b)=>Day(b);internal static bool TestHarvest(Bush b)=>Harvest(b);
 internal static bool TestTool(Bush b,StardewValley.Tool t){bool result=false;return Tool(b,t,ref result);}
 internal static bool TestDeadAxe(Bush b){bool result=false;Tool(b,new Axe(),ref result);return result;}
 internal static bool TestSeason(Bush b){bool r=false;SeasonUpdate(b,ref r);return r;}
 internal static void TestSprinkler(StardewValley.Object o,Vector2 v)=>Sprinkler(o,v);
 internal static void TestPotWater(IndoorPot pot)=>PotWater(pot);internal static void TestTick(Bush b)=>Tick(b);
 internal static bool TestPlant(StardewValley.Object o){bool result=false;return PlantOutside(o,Game1.currentLocation,0,0,out _,ref result);}
 internal static bool TestPotPlant(Item o,bool probe){bool result=false;return PlantPot(o,probe,ref result);}
 internal static void TestPrewateredOutside(StardewValley.Object o,GameLocation l){bool r=false;PlantOutside(o,l,0,0,out bool wet,ref r);l.terrainFeatures[Vector2.Zero]=new Bush{Location=l};PlacedOutside(o,l,0,0,wet,true);}
 internal static void TestPrewateredPot(IndoorPot pot,Item item)=>PlacedPot(pot,item,false,true);
 internal static Rectangle TestDeadSprite(Bush b){SourceRect(b);return b.sourceRect.Value;}
}
internal sealed partial class GoldenAppleFeature {
 internal void TestReconcile()=>Reconcile();
 internal static bool TestEat(Farmer f,StardewValley.Object apple){bool accepted=BeforeEat(f,apple);if(accepted){f.eatObject(apple);AfterEat(f,apple,true);if(f.isEating)apple.Stack--;}return accepted;}
 internal static void TestDone(Farmer f){f.doneEating();DoneEating(f);}
 internal static void TestDamage(Farmer f,int finalHealth){BeforeDamage(f,out long state);SetDamageHealth(f,finalHealth);AfterDamage(f,state);}
 internal void TestTick()=>Tick(null,new UpdateTickedEventArgs());
 internal static Color TestTint(Farmer f){Color color=Color.White;Tint(f,ref color);return color;}
 internal static bool TestDamageable(Farmer f){bool result=true;CanBeDamaged(f,ref result);return result;}
 internal static bool TestCraft(CraftingPage p,ClickableTextureComponent c)=>BeforeCraft(p,c);
 internal static (int,int) TestRecovery(StardewValley.Object o){int e=77,h=88;Energy(o,ref e);Health(o,ref h);return(e,h);}
 internal static void TestQuality(Item item)=>CraftedQuality(item);
 internal void TestNotice(ModMessageReceivedEventArgs e)=>Message(null,e);
 internal static bool TestDraw(Farmer f){BeforeDraw(f,out bool old);bool visible=!f.flashDuringThisTemporaryInvincibility;AfterDraw(f,old);return visible;}
}

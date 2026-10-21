using System.Reflection;
using Eddie.FishingMachine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
class Program
{
 static int Checks;
 static void Check(bool b,string message){Checks++;if(!b)throw new Exception(message);}
 static void Draw(LevelUpMenu menu,SpriteBatch b,int skill=1,int level=5,int delay=0,int professions=2)
 =>typeof(LevelUpRecipePatch).GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{menu,b,skill,level,delay,Enumerable.Range(0,professions).ToList()});
 static void Main(){
  foreach(string name in new[]{"钓鱼机","Fishing Machine",new string('L',180)})foreach(int width in new[]{640,960,1280}){
   ModEntry.Instance=new();ModEntry.Instance.Name=name;var menu=new LevelUpMenu{width=width};var b=new SpriteBatch();Draw(menu,b);
   Check(b.Rows.Count==2,"one icon and one native-localized label");
   Check(b.Text!.Contains(name),"localized recipe name included");
   foreach(var row in b.Rows){Check(row.X>=menu.xPositionOnScreen && row.X+row.W<=menu.xPositionOnScreen+width,"inside horizontal bounds");Check(row.Y>=menu.yPositionOnScreen+132 && row.Y+row.H<=menu.yPositionOnScreen+180,"free header band, no profession hit-box overlap");}
   Check(menu.isProfessionChooser&&menu.informationUp&&menu.isActive,"profession state unchanged");
  }
  foreach(int skill in new[]{0,2,3,4,5}){var b=new SpriteBatch();Draw(new(),b,skill:skill);Check(b.Rows.Count==0,"other skills untouched");}
  foreach(int level in new[]{1,4,6,10}){var b=new SpriteBatch();Draw(new(),b,level:level);Check(b.Rows.Count==0,"ordinary levels and level 10 untouched");}
  foreach(string state in new[]{"inactive","closed","normal","delay","no-professions","disabled"}){
   ModEntry.Instance=new();var menu=new LevelUpMenu();var b=new SpriteBatch();
   if(state=="inactive")menu.isActive=false;if(state=="closed")menu.informationUp=false;if(state=="normal")menu.isProfessionChooser=false;if(state=="disabled")ModEntry.Instance.Ready=false;
   Draw(menu,b,delay:state=="delay"?250:0,professions:state=="no-professions"?0:2);Check(b.Rows.Count==0,"hidden or unavailable UI untouched: "+state);
  }
  Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,assertions=Checks,scope="Actual level-up postfix against headless drawing boundary; localization, header fit, guards and profession-state preservation"}));
 }
}
namespace HarmonyLib{[AttributeUsage(AttributeTargets.Class)]public class HarmonyPatch:Attribute{public HarmonyPatch(Type t,string n,Type[] p){}}}
namespace Microsoft.Xna.Framework{
 public struct Vector2{public float X,Y;public Vector2(float x,float y){X=x;Y=y;}public static Vector2 Zero=>new(0,0);}
 public struct Color{}
}
namespace Microsoft.Xna.Framework.Graphics{
 public enum SpriteEffects{None}
 public class SpriteBatch{
  public List<(float X,float Y,float W,float H)> Rows=new();public string? Text;
  public void DrawString(StardewValley.Font font,string text,Vector2 p,Color c,float rotation,Vector2 origin,float scale,SpriteEffects effects,float depth){Text=text;var size=font.MeasureString(text);Rows.Add((p.X,p.Y,size.X*scale,size.Y*scale));}
 }
}
namespace StardewValley{
 public class Font{public Vector2 MeasureString(string s)=>new(s.Length*12,24);}
 public class Content{public string LoadString(string key,params object[] args)=>key.EndsWith("crafting")?"制作配方":"已学会 "+args[0]+"："+args[1];}
 public static class Game1{public static Content content=new();public static Font smallFont=new();public static Color textColor=new();}
}
namespace StardewValley.Menus{
 public class LevelUpMenu{public bool isActive=true,informationUp=true,isProfessionChooser=true;public int width=960,xPositionOnScreen=100,yPositionOnScreen=100;public void draw(SpriteBatch b){}}
}
namespace Eddie.FishingMachine{
 public class Art{public void DrawIcon(SpriteBatch b,Vector2 p,float scale,float alpha,float depth)=>b.Rows.Add((p.X+32-32*scale,p.Y+32-16*scale,64*scale,32*scale));}
 public class ModEntry{public static ModEntry Instance=new();public bool Ready=true;public string Name="钓鱼机";public Art Art=new();public string T(string s)=>Name;}
}

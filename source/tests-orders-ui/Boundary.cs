// A — draw-command recorder, approximate font metrics and scripted input. No original game art included.
namespace Microsoft.Xna.Framework
{
 public record struct Vector2(float X,float Y){public static Vector2 Zero=>new(0,0);}
 public record struct Rectangle(int X,int Y,int Width,int Height);
 public record struct Color(int R,int G,int B,int A=255){public static Color White=>new(255,255,255);public static Color Black=>new(0,0,0);public static Color operator*(Color c,float a)=>c with{A=(int)(c.A*a)};}
 public class GameTime{public TimeSpan ElapsedGameTime=TimeSpan.FromSeconds(1);}
}
namespace Microsoft.Xna.Framework.Input{public enum Keys{Escape,Left,Right,Tab,Up,Down,Enter,Space}public enum Buttons{B,A,LeftShoulder,RightShoulder,DPadUp,DPadDown}}
namespace Microsoft.Xna.Framework.Graphics
{
 using Microsoft.Xna.Framework;
 public enum SpriteEffects{None}
 public class Texture2D{public string Name="fill";}
 public class SpriteFont{public int Size=20;public Vector2 MeasureString(string value)=>new(value.Sum(c=>c>255?Size:Size*.52f),Size*1.3f);}
 public class SpriteBatch
 {
  public List<object> Commands=new();public void Draw(Texture2D texture,Rectangle r,Color color)=>Commands.Add(new{kind=texture.Name,rect=r,color});
  public void Draw(Texture2D texture,Rectangle r,Rectangle source,Color color)=>Commands.Add(new{kind=texture.Name,rect=r,source,color});
  public void DrawString(SpriteFont font,string text,Vector2 p,Color color,float rotation,Vector2 origin,float scale,SpriteEffects e,float layer)=>Commands.Add(new{kind="text",text,x=p.X,y=p.Y,size=font.Size*scale,color});
 }
}
namespace StardewValley.Menus
{
 using Microsoft.Xna.Framework;using Microsoft.Xna.Framework.Graphics;using Microsoft.Xna.Framework.Input;
 public class IClickableMenu
 {public static void drawHoverText(SpriteBatch b,string text,SpriteFont font)=>b.Commands.Add(new{kind="hover",text});public int xPositionOnScreen,yPositionOnScreen,width,height;public bool Closed;public IClickableMenu(int x,int y,int w,int h,bool close){}public virtual void gameWindowSizeChanged(Rectangle o,Rectangle n){}public virtual void update(GameTime t){}public virtual void receiveLeftClick(int x,int y,bool sound=true){}public virtual void receiveScrollWheelAction(int d){}public virtual void receiveKeyPress(Keys k){}public virtual void receiveGamePadButton(Buttons b){}public virtual void draw(SpriteBatch b){}public virtual bool readyToClose()=>true;public void exitThisMenu()=>Closed=true;public void drawMouse(SpriteBatch b){}}
}
namespace StardewValley
{
 using Microsoft.Xna.Framework;using Microsoft.Xna.Framework.Graphics;
 public class Farmer{public long UniqueMultiplayerID=1;public int Count=12;}
 public class Date{public int TotalDays=10;}
 public class Content{public List<string> Loaded=new();public T Load<T>(string path) where T:new(){Loaded.Add(path);return (T)(object)new Texture2D{Name=path};}}
 public static class Game1
 {public static Rectangle uiViewport=new(0,0,1120,760);public static Farmer player=new();public static Date Date=new();public static Texture2D staminaRect=new();public static SpriteFont smallFont=new(),dialogueFont=new(){Size=30};public static Content content=new();
 public static string parseText(string text,SpriteFont font,int width){var result="";float line=0;foreach(char c in text){float n=font.MeasureString(c.ToString()).X;if(line+n>width){result+='\n';line=0;}result+=c;line+=n;}return result;}
 public static void playSound(string s){}
 public static int MouseX=-100,MouseY=-100;public static int getMouseX(bool ui)=>MouseX;public static int getMouseY(bool ui)=>MouseY;
 }
 public class ItemData{private string Id;public ItemData(string id)=>Id=id;public Texture2D GetTexture()=>new(){Name=Id};public Rectangle GetSourceRect()=>new(0,0,16,16);}
 public static class ItemRegistry{public static ItemData GetDataOrErrorItem(string id)=>new(id);}
}
namespace BetterStardewValley.Orders
{
 internal class Value<T>{internal T Stored;internal Value(T v)=>Stored=v;internal T ValueProperty{get=>Stored;set=>Stored=value;}}
 internal class NoticeValue{internal string Value="";}
 internal class OrdersService
 {
  internal MarketBook Book=new();internal bool HasPending,Reach=true;internal NoticeValue Notice=new();internal List<string> Requests=new();internal Dictionary<string,string> Strings=new();
  internal MarketBook Read(long actor)=>Book.Copy();internal bool InReach(StardewValley.Farmer f,string l,int x,int y)=>Reach;
  internal string Number(int value)=>value.ToString("N0",System.Globalization.CultureInfo.InvariantCulture);
  internal void Request(string action,MarketOrder c,string l,int x,int y)=>Requests.Add(action+":"+c.Id);
  internal int Stock(MarketOrder c)=>StardewValley.Game1.player.Count;
  internal bool CanAccept(MarketOrder c,int displayedStock)=>!HasPending&&c.State is "available" or "legacy-active"&&displayedStock>=c.Quantity;
  internal string Tr(string key,object? args=null){string text=Strings.GetValueOrDefault(key,key);if(args!=null)foreach(var p in args.GetType().GetProperties())text=text.Replace("{{"+p.Name+"}}",p.GetValue(args)?.ToString());return text;}
 }
 internal static class OrdersInventory{internal static int Count(StardewValley.Farmer f,string id)=>f.Count;}
 internal static class OrderCatalog{internal static string ItemName(string id)=>id switch{"(O)24"=>"Parsnip","(O)128"=>"Pufferfish","(O)388"=>"Wood","(O)346"=>"Beer","(BC)272"=>"Auto-Petter","(O)287"=>"Bomb","(O)288"=>"Mega Bomb","(O)382"=>"Coal","(O)216"=>"Bread",_=>"Milk"};internal static string NpcName(string name)=>name;}
}

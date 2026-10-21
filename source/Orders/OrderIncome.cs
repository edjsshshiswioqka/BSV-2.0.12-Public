using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
namespace BetterStardewValley.Orders;

// A — Orders is a separate displayed income category, never a fabricated shippable item.
// Native ShippingMenu continues to own input, item-detail pages, night transition and SaveGameMenu.
internal static class OrderIncome
{
 private sealed class Receipt {internal int Gold;}
 private static readonly ConditionalWeakTable<ShippingMenu,Receipt> Receipts=new();
 private static readonly FieldInfo Totals=Field("categoryTotals"),Dials=Field("categoryDials"),Outro=Field("outro"),SaveMenu=Field("saveGameMenu");
 private static FieldInfo Field(string name)=>typeof(ShippingMenu).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)??throw new MissingFieldException(typeof(ShippingMenu).FullName,name);
 internal static void Install(Harmony h)
 {
  void Patch(Type type,string name,Type[] args,string handler,bool prefix=false)=>h.Patch(DrawPatchTargets.Require(type,name,typeof(void),args),prefix:prefix?new HarmonyMethod(typeof(OrderIncome),handler):null,postfix:prefix?null:new HarmonyMethod(typeof(OrderIncome),handler));
  Patch(typeof(ShippingMenu),"parseItems",new[]{typeof(IList<Item>)},nameof(Parsed));
  Patch(typeof(ShippingMenu),"RepositionItems",Type.EmptyTypes,nameof(Position));
  Patch(typeof(ShippingMenu),"draw",new[]{typeof(SpriteBatch)},nameof(Draw),true);
  Patch(typeof(Game1),"showEndOfNightStuff",Type.EmptyTypes,nameof(EnsureMenu));
 }
 private static void Parsed(ShippingMenu __instance)
 {
  int gold=OrdersService.Instance.Income();if(gold<=0)return;
  var totals=(List<int>)Totals.GetValue(__instance)!;var dials=(List<MoneyDial>)Dials.GetValue(__instance)!;
  if(totals.Count!=6||dials.Count!=6)throw new InvalidOperationException("Unknown native shipping layout; Orders ledger remains intact.");
  Receipts.GetOrCreateValue(__instance).Gold=gold;totals[5]=checked(totals[5]+gold);
  dials[5].currentValue=totals[5];dials[5].previousTargetValue=totals[5];
 }
 private static void EnsureMenu()
 {
  if(OrdersService.Instance.Income()<=0||Game1.activeClickableMenu is ShippingMenu||Game1.endOfNightMenus.Any(m=>m is ShippingMenu))return;
  if(Game1.activeClickableMenu is SaveGameMenu){Game1.activeClickableMenu=new ShippingMenu(new List<Item>());return;}
  // Level-up choices remain ahead of saving. Replace the queued native save menu, never add a second save.
  var stack=Game1.endOfNightMenus.ToArray();int at=Array.FindIndex(stack,m=>m is SaveGameMenu);
  if(at<0)return;stack[at]=new ShippingMenu(new List<Item>());Game1.endOfNightMenus.Clear();
  for(int i=stack.Length-1;i>=0;i--)Game1.endOfNightMenus.Push(stack[i]);
 }
 internal static (Rectangle Panel,int Row) Geometry(int width,int height)
 {
  int w=Math.Min(720,Math.Max(280,width-40)),row=Math.Clamp((height-150)/7,30,66),h=row*7+100;
  return (new((width-w)/2,Math.Max(8,(height-h)/2),w,h),row);
 }
 private static void Position(ShippingMenu __instance)
 {
  if(!Receipts.TryGetValue(__instance,out var receipt)||receipt.Gold<=0||__instance.categories.Count!=6)return;
  var (panel,row)=Geometry(Game1.uiViewport.Width,Game1.uiViewport.Height);
  for(int i=0;i<6;i++)__instance.categories[i].bounds=new Rectangle(panel.Right-54,panel.Y+45+(i==5?6:i)*row,40,Math.Min(44,row));
  __instance.okButton.bounds=new Rectangle(panel.Right-72,Math.Min(Game1.uiViewport.Height-70,panel.Bottom+6),64,64);
 }
 private static bool Draw(ShippingMenu __instance,SpriteBatch b)
 {
  if(!Receipts.TryGetValue(__instance,out var receipt)||receipt.Gold<=0||__instance.currentPage>=0||(bool)Outro.GetValue(__instance)!||SaveMenu.GetValue(__instance)!=null)return true;
  Position(__instance);var (panel,row)=Geometry(Game1.uiViewport.Width,Game1.uiViewport.Height);var totals=(List<int>)Totals.GetValue(__instance)!;
  b.Draw(Game1.staminaRect,new Rectangle(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height),new Color(26,39,53));
  b.Draw(Game1.staminaRect,panel,new Color(102,58,31));b.Draw(Game1.staminaRect,new Rectangle(panel.X+5,panel.Y+5,panel.Width-10,panel.Height-10),new Color(253,233,188));
  float scale=Math.Min(1f,(panel.Width-60)/620f);var ink=new Color(73,43,28);
  b.DrawString(Game1.smallFont,OrdersService.Instance.Tr("orders.income-title"),new Vector2(panel.X+22,panel.Y+13),ink,0,Vector2.Zero,scale,SpriteEffects.None,.99f);
  for(int n=0;n<7;n++)
  {
   bool orders=n==5;int index=n==6?5:n;int y=panel.Y+46+n*row;
   string label=orders?OrdersService.Instance.Tr("orders.income-category"):__instance.categories[index].hoverText;
   int amount=orders?receipt.Gold:totals[index];
   b.Draw(Game1.staminaRect,new Rectangle(panel.X+14,y,panel.Width-28,row-2),orders?new Color(236,209,144):new Color(246,223,176));
   b.DrawString(Game1.smallFont,label,new Vector2(panel.X+24,y+4),ink,0,Vector2.Zero,scale,SpriteEffects.None,.99f);
   string money=amount.ToString("N0")+"g";float x=panel.Right-80-Game1.smallFont.MeasureString(money).X*scale;
   b.DrawString(Game1.smallFont,money,new Vector2(x,y+4),ink,0,Vector2.Zero,scale,SpriteEffects.None,.99f);
   if(n<5&&__instance.categories[index].visible)__instance.categories[index].draw(b);
  }
  __instance.okButton.draw(b);__instance.drawMouse(b);return false;
 }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
namespace BetterStardewValley.Orders;

// A — original BSV renderer and interaction. Portraits are loaded from the installed game, never bundled.
internal sealed class OrdersMenu:IClickableMenu
{
 private readonly OrdersService Service;private readonly string Location;private readonly int TileX,TileY;
 private OrdersLayout Layout=null!;private int Tab,Selected,Scroll;private MarketBook Book=new();private double Elapsed;private int DisplayedStock;private bool Eligible;private string? ConfirmReject;
 private readonly Dictionary<string,Texture2D?> Portraits=new();
 private string HoverText="";
 private static readonly Color Ink=new(73,43,28),Muted=new(124,88,53),Paper=new(255,239,198),Pale=new(246,223,172),Wood=new(137,78,42),Gold=new(231,169,55),Green=new(70,125,76);
 internal OrdersMenu(OrdersService service,string location,int x,int y):base(0,0,1040,680,false)
 {Service=service;Location=location;TileX=x;TileY=y;Resize();Refresh();}
 private string Tr(string key,object? args=null)=>Service.Tr("orders."+key,args);
 private List<MarketOrder> Rows=>Tab==0?Book.Available:Tab==1?Book.Active:Book.Completed;
 private MarketOrder? Current=>Selected>=0&&Selected<Rows.Count?Rows[Selected]:null;
 private void Resize(){Layout=new(Game1.uiViewport.Width,Game1.uiViewport.Height);var r=Layout.Screen(new(0,0,1040,680));xPositionOnScreen=r.X;yPositionOnScreen=r.Y;width=r.W;height=r.H;}
 private void Refresh()
 {
  string? selected=Current?.Id;try{Book=Service.Read(Game1.player.UniqueMultiplayerID);}catch{Service.Notice.Value="orders.error";Book=new();}
  int found=selected==null?-1:Rows.FindIndex(o=>o.Id==selected);Selected=found>=0?found:Math.Clamp(Selected,0,Math.Max(0,Rows.Count-1));Scroll=Math.Clamp(Scroll,0,Math.Max(0,Rows.Count-4));
  DisplayedStock=Tab!=2&&Current is {} current?Service.Stock(current):0;Eligible=Current is {} selectedOrder&&Service.CanAccept(selectedOrder,DisplayedStock);
 }
 public override void gameWindowSizeChanged(Rectangle oldBounds,Rectangle newBounds)=>Resize();
 public override void update(GameTime time)
 {
  base.update(time);if(!Service.InReach(Game1.player,Location,TileX,TileY)){exitThisMenu();return;}
  Elapsed+=time.ElapsedGameTime.TotalSeconds;if(Elapsed>=.3){Elapsed=0;Refresh();}
 }
 private Rectangle R(OrderRect r){var p=Layout.Screen(r);return new(p.X,p.Y,p.W,p.H);}
 private bool Hit(OrderRect r,int x,int y)=>Layout.Screen(r).Contains(x,y);
 private void Fill(SpriteBatch b,OrderRect r,Color c)=>b.Draw(Game1.staminaRect,R(r),c);
 private void Panel(SpriteBatch b,OrderRect r,Color color,bool selected=false)
 {
  Fill(b,r,Ink);Fill(b,new(r.X+2,r.Y+2,r.W-4,r.H-4),selected?Gold:Wood);
  Fill(b,new(r.X+5,r.Y+5,r.W-10,r.H-10),color);Fill(b,new(r.X+5,r.Y+5,r.W-10,2),Color.White*.3f);
 }
 private void Text(SpriteBatch b,string value,int x,int y,int maxWidth=900,float size=1,Color? color=null,bool title=false)
 {
  var font=title?Game1.dialogueFont:Game1.smallFont;string text=value??"";
  text=DisplayText.Ellipsis(text,maxWidth,s=>font.MeasureString(s).X*size);
  if(text!=value&&Hit(new(x,y,maxWidth,(int)Math.Ceiling(font.MeasureString(value??"").Y*size)),Game1.getMouseX(true),Game1.getMouseY(true)))HoverText=value??"";
  b.DrawString(font,text,new Vector2(Layout.X+x*Layout.Scale,Layout.Y+y*Layout.Scale),color??Ink,0,Vector2.Zero,size*Layout.Scale,SpriteEffects.None,0.95f);
 }
 private void Wrap(SpriteBatch b,string value,int x,int y,int width,int lines=3)
 {
  string text=DisplayText.Wrap(value,width,s=>Game1.smallFont.MeasureString(s).X);int n=0;foreach(string line in text.Split('\n')){if(n>=lines)break;Text(b,line,x,y+n*29,width);n++;}
  if(text.Split('\n').Length>lines&&Hit(new(x,y,width,lines*29),Game1.getMouseX(true),Game1.getMouseY(true)))HoverText=value;
 }
 private void Portrait(SpriteBatch b,string npc,int x,int y,int size)
 {
  Panel(b,new(x-4,y-4,size+8,size+8),Pale);
  if(!Portraits.TryGetValue(npc,out var texture))
  {try{texture=Game1.content.Load<Texture2D>("Portraits/"+npc);}catch{texture=null;}Portraits[npc]=texture;}
  if(texture!=null)b.Draw(texture,R(new(x,y,size,size)),new Rectangle(0,0,64,64),Color.White);
 }
 private void ItemIcon(SpriteBatch b,string id,int x,int y,int size)
 {var data=ItemRegistry.GetDataOrErrorItem(id);var source=data.GetSourceRect();float scale=size/(float)Math.Max(source.Width,source.Height);int w=(int)(source.Width*scale),h=(int)(source.Height*scale);b.Draw(data.GetTexture(),R(new(x+(size-w)/2,y+(size-h)/2,w,h)),source,Color.White);}
 private void Coin(SpriteBatch b,int x,int y)
 {Fill(b,new(x+4,y,14,22),Ink);Fill(b,new(x,y+4,22,14),Ink);Fill(b,new(x+3,y+5,16,12),Gold);Fill(b,new(x+6,y+3,10,16),Gold);Fill(b,new(x+8,y+5,3,11),new Color(255,232,138));}
 private string Status(MarketOrder c)=>c.State=="completed"?Tr("completed-day",new{day=c.Completed+1}):Tr(c.State=="pending"?"settles-tonight":c.State=="legacy-active"?"legacy-active":"kind."+c.Kind);
 private bool CanAct()=>Current!=null&&Tab!=2&&!Service.HasPending&&Eligible;
 public override bool readyToClose()=>!Service.HasPending&&base.readyToClose();
 private void Act(){if(CanAct()&&Current is {} c){Service.Request("accept",c,Location,TileX,TileY);ConfirmReject=null;Refresh();}}
 private void Reject(){if(Tab==0&&Current is {} c&&!Service.HasPending){if(ConfirmReject==c.Id){Service.Request("reject",c,Location,TileX,TileY);ConfirmReject=null;Refresh();}else ConfirmReject=c.Id;}}
 private void SelectTab(int value){ConfirmReject=null;Tab=(value+3)%3;Selected=0;Scroll=0;Refresh();Game1.playSound("shwip");}
 public override void receiveLeftClick(int x,int y,bool playSound=true)
 {
  for(int n=0;n<3;n++)if(Hit(Layout.Tab(n),x,y)){SelectTab(n);return;}
  for(int n=0;n<4&&Scroll+n<Rows.Count;n++)if(Hit(Layout.Row(n),x,y)){Selected=Scroll+n;ConfirmReject=null;Refresh();Game1.playSound("smallSelect");return;}
  if(Hit(Layout.Close,x,y)){if(!Service.HasPending){if(ConfirmReject!=null)ConfirmReject=null;else exitThisMenu();}return;}if(Hit(Layout.Reject,x,y)){Reject();return;}if(Hit(Layout.Action,x,y)){Act();return;}
  if(Hit(Layout.Previous,x,y))Scroll=Math.Max(0,Scroll-4);if(Hit(Layout.Next,x,y))Scroll=Math.Min(Math.Max(0,Rows.Count-4),Scroll+4);
 }
 public override void receiveScrollWheelAction(int direction){Scroll=Math.Clamp(Scroll+(direction<0?1:-1),0,Math.Max(0,Rows.Count-4));}
 public override void receiveKeyPress(Keys key)
 {
  if(key==Keys.Escape){if(!Service.HasPending){if(ConfirmReject!=null)ConfirmReject=null;else exitThisMenu();}return;}if(key==Keys.Left){SelectTab(Tab-1);return;}if(key==Keys.Right||key==Keys.Tab){SelectTab(Tab+1);return;}
  if(key==Keys.Up||key==Keys.Down){Selected=Math.Clamp(Selected+(key==Keys.Up?-1:1),0,Math.Max(0,Rows.Count-1));Scroll=Math.Clamp(Scroll,Math.Max(0,Selected-3),Selected);ConfirmReject=null;Refresh();return;}
  if(key==Keys.Enter||key==Keys.Space)Act();
 }
 public override void receiveGamePadButton(Buttons b){switch(b){case Buttons.B:receiveKeyPress(Keys.Escape);break;case Buttons.A:Act();break;case Buttons.LeftShoulder:SelectTab(Tab-1);break;case Buttons.RightShoulder:SelectTab(Tab+1);break;case Buttons.DPadUp:receiveKeyPress(Keys.Up);break;case Buttons.DPadDown:receiveKeyPress(Keys.Down);break;}}
 public override void draw(SpriteBatch b)
 {
  HoverText="";
  b.Draw(Game1.staminaRect,new Rectangle(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height),Color.Black*.55f);
  Panel(b,new(0,0,1040,680),new Color(188,123,63));Panel(b,new(18,17,1004,57),Pale);
  Text(b,Tr("title"),40,23,620,1,Ink,true);Text(b,Tr("community"),620,34,373,.86f,Muted);
  string[] tabs={"available","active","completed"};for(int n=0;n<3;n++)
  {Panel(b,Layout.Tab(n),n==Tab?Paper:new Color(211,172,113),n==Tab);int count=n==0?Book.Available.Count:n==1?Book.Active.Count:Book.Completed.Count;Text(b,Tr(tabs[n])+"  "+count+(n==0?"/40":""),Layout.Tab(n).X+21,94,282);}
  Panel(b,new(22,143,430,475),Pale);Panel(b,new(462,143,556,475),Paper);
  for(int n=0;n<4&&Scroll+n<Rows.Count;n++)
  {
   int index=Scroll+n;var c=Rows[index];var rect=Layout.Row(n);Panel(b,rect,index==Selected?Paper:new Color(242,215,166),index==Selected);
   Portrait(b,c.Npc,rect.X+13,rect.Y+16,56);Text(b,OrderCatalog.NpcName(c.Npc),rect.X+82,rect.Y+8,220,.94f);
   ItemIcon(b,c.Item,rect.X+80,rect.Y+35,24);Text(b,OrderCatalog.ItemName(c.Item)+" × "+c.Quantity,rect.X+111,rect.Y+34,287,.86f);
   Text(b,c.Kind=="barter"?Tr("kind.barter"):Service.Number(c.Gold)+"g",rect.X+82,rect.Y+64,145,.8f,Muted);Text(b,Status(c),rect.X+233,rect.Y+64,171,.75f,Muted);
  }
  if(Rows.Count==0)Wrap(b,Tr(Tab==0?"empty-available":Tab==1?"empty-active":"empty-completed"),48,215,375,5);
  if(Rows.Count>4){Panel(b,Layout.Previous,Pale);Text(b,"<",54,576,30);Panel(b,Layout.Next,Pale);Text(b,">",405,576,30);Text(b,$"{Scroll+1}–{Math.Min(Scroll+4,Rows.Count)} / {Rows.Count}",174,576,190,.85f,Muted);}
  if(Current is {} order)
  {
   Portrait(b,order.Npc,485,162,88);Text(b,OrderCatalog.NpcName(order.Npc),594,159,384,1,Ink,true);Text(b,Tr("role."+order.Npc),594,205,390,.9f,Muted);
   Text(b,Tr("kind."+order.Kind),594,235,380,.77f,order.Npc=="Morris"?new Color(157,57,33):Muted);
   Fill(b,new(480,267,516,2),new Color(196,155,96));Wrap(b,Tr("quote."+order.Npc),485,279,505,2);
   Text(b,Tr("requested"),485,343,497,.83f,Muted);ItemIcon(b,order.Item,488,380,44);
   Text(b,OrderCatalog.ItemName(order.Item)+" × "+order.Quantity,547,373,443,.94f);
   int held=DisplayedStock;Text(b,Tab==2?Tr("fulfilled"):Tr("in-chests",new{count=held,quantity=order.Quantity}),547,405,443,.83f,Muted);Text(b,Tr("quality."+order.MinimumQuality),547,431,443,.76f,Muted);
   Fill(b,new(485,460,507,2),new Color(196,155,96));Coin(b,489,478);Text(b,order.Gold>0?Service.Number(order.Gold)+"g":Tr("no-gold"),522,473,229,.94f);
   for(int i=0;i<order.Rewards.Count;i++){var reward=order.Rewards[i];ItemIcon(b,reward.Id,744,477+i*32,28);Text(b,OrderCatalog.ItemName(reward.Id)+" × "+reward.Count,779,477+i*32,214,.73f);}
   Text(b,Status(order),489,516,505,.77f,Muted);
  }
  Panel(b,Layout.Action,CanAct()?Green:new Color(150,141,112));Text(b,Tr(Service.HasPending?"processing":"accept"),491,568,155,.9f,Color.White);
  Panel(b,Layout.Reject,Tab==0&&!Service.HasPending?new Color(164,77,49):new Color(150,141,112));Text(b,Tr(ConfirmReject==Current?.Id&&ConfirmReject!=null?"confirm-reject":"reject"),666,568,158,.85f,Color.White);
  Panel(b,Layout.Close,new Color(206,162,102));Text(b,Tr(ConfirmReject!=null?"cancel":"close"),865,568,134,.9f);
  Text(b,ConfirmReject!=null?Tr("reject-question"):string.IsNullOrEmpty(Service.Notice.Value)?Tr("footer"):Service.Tr(Service.Notice.Value),34,637,970,.78f,Ink);
  if(HoverText.Length>0)drawHoverText(b,DisplayText.Wrap(HoverText,Math.Max(80,Math.Min(480,Game1.uiViewport.Width-96)),s=>Game1.smallFont.MeasureString(s).X),Game1.smallFont);
  drawMouse(b);
 }
}

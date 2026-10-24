using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using SObject = StardewValley.Object;
namespace BetterStardewValley.Orders;

// One screen: orders on the left, the terminal's 48-slot order box and the backpack on the right.
// Click an item to move it between backpack and box (like a native chest). "Confirm" on an order reserves the goods
// in the box (pulling the missing ones from the backpack), they are locked with a green tick and delivered tonight.
internal sealed class OrderDeskMenu : IClickableMenu
{
 private const int LeftW=420,Gap=20,RightW=800,PanelH=640,RowH=96,Rows=4;
 private static readonly Color Ink=new(73,43,28),Muted=new(124,88,53),Paper=new(255,239,198),Pale=new(246,223,172),Wood=new(137,78,42),
  Gold=new(231,169,55),Green=new(70,125,76),Tick=new(46,160,67),Disabled=new(150,141,112),Red=new(164,77,49);
 private readonly OrdersService Service;private readonly string Location;private readonly int TileX,TileY;
 internal readonly string Key;
 private InventoryMenu BoxMenu=null!,Backpack=null!;
 private MarketBook Book=new();private int Tab,Scroll;private double Elapsed;private string? ConfirmReject,PendingCancel;
 private MarketOrder? HoverOrder;private string HoverText="";private Item? HoverItem;
 private readonly Dictionary<string,Texture2D?> Portraits=new();private readonly Dictionary<string,string> Names=new();
 private Rectangle Left,Right;
 private IList<Item> Box=>OrderBox.Items(Key);
 private string Tr(string key,object? args=null)=>Service.Tr("orders."+key,args);

 internal OrderDeskMenu(OrdersService service,string location,int x,int y,string key):base(0,0,LeftW+Gap+RightW,PanelH,true)
 {
  Service=service;Location=location;TileX=x;TileY=y;Key=key;
  Refresh();OrderBox.Pad(Box);OrderBox.StripStale(Box,Book,Key);Layout();
  if(Game1.options.SnappyMenus)snapToDefaultClickableComponent();
  Game1.playSound("bigSelect");
 }
 private void Layout()
 {
  width=LeftW+Gap+RightW;height=PanelH;
  xPositionOnScreen=Math.Max(8,(Game1.uiViewport.Width-width)/2);yPositionOnScreen=Math.Max(8,(Game1.uiViewport.Height-height)/2);
  Left=new(xPositionOnScreen,yPositionOnScreen,LeftW,PanelH);Right=new(Left.Right+Gap,yPositionOnScreen,RightW,PanelH);
  BoxMenu=new InventoryMenu(Right.X+16,Right.Y+76,false,Box,null,OrderBox.Capacity,OrderBox.Rows);
  Backpack=new InventoryMenu(Right.X+16,Right.Y+76+OrderBox.Rows*64+70,true);
  upperRightCloseButton=new ClickableTextureComponent(new Rectangle(Right.Right-44,Right.Y-8,48,48),Game1.mouseCursors,new Rectangle(337,494,12,12),4f);
  BuildComponents();
 }
 // ---------- controller navigation ----------
 // Ids: tabs 100-102, row buttons 200+, reject 300+, scroll 400/401, close 500, box slots 1000+, backpack slots 0-35.
 private void BuildComponents()
 {
  int keep=currentlySnappedComponent?.myID??-1;
  var all=new List<ClickableComponent>();
  int visible=Math.Max(0,Math.Min(Rows,RowsFor().Count-Scroll));bool arrows=RowsFor().Count>Rows;
  int RowTarget(int n)=>visible==0?100+Tab:200+Math.Clamp(n,0,visible-1);
  for(int n=0;n<3;n++)all.Add(new(TabRect(n),"tab"+n){myID=100+n,leftNeighborID=n>0?99+n:-1,rightNeighborID=n<2?101+n:1000,downNeighborID=RowTarget(0),upNeighborID=-1});
  var rows=RowsFor();
  for(int n=0;n<visible;n++)
  {
   var row=RowRect(n);bool reject=Tab==0&&rows[Scroll+n].State=="available";
   all.Add(new(Tab==2?row:ButtonRect(row),"row"+n){myID=200+n,upNeighborID=n==0?100+Tab:200+n-1,downNeighborID=n<visible-1?201+n:arrows?401:-1,
    leftNeighborID=reject?300+n:-1,rightNeighborID=1000+Math.Min(n,OrderBox.Rows-1)*OrderBox.Columns});
   if(reject)all.Add(new(RejectRect(row),"reject"+n){myID=300+n,rightNeighborID=200+n,downNeighborID=200+n,upNeighborID=n==0?100+Tab:200+n-1,leftNeighborID=-1});
  }
  if(arrows)
  {
   all.Add(new(UpRect,"up"){myID=400,rightNeighborID=401,upNeighborID=RowTarget(visible-1),leftNeighborID=-1,downNeighborID=-1});
   all.Add(new(DownRect,"down"){myID=401,leftNeighborID=400,upNeighborID=RowTarget(visible-1),rightNeighborID=1000+(OrderBox.Rows-1)*OrderBox.Columns,downNeighborID=-1});
  }
  upperRightCloseButton.myID=500;upperRightCloseButton.downNeighborID=1000+OrderBox.Columns-1;upperRightCloseButton.leftNeighborID=102;all.Add(upperRightCloseButton);
  for(int i=0;i<BoxMenu.inventory.Count;i++)
  {
   var c=BoxMenu.inventory[i];int col=i%OrderBox.Columns,row=i/OrderBox.Columns;
   c.myID=1000+i;c.leftNeighborID=col==0?RowTarget(row):c.myID-1;c.rightNeighborID=col==OrderBox.Columns-1?-1:c.myID+1;
   c.upNeighborID=row==0?(col==OrderBox.Columns-1?500:100+Tab):c.myID-OrderBox.Columns;
   c.downNeighborID=row==OrderBox.Rows-1?col:c.myID+OrderBox.Columns;all.Add(c);
  }
  for(int i=0;i<Backpack.inventory.Count;i++)
  {
   var c=Backpack.inventory[i];int col=i%OrderBox.Columns,row=i/OrderBox.Columns;
   c.myID=i;c.leftNeighborID=col==0?(arrows?401:RowTarget(visible-1)):i-1;c.rightNeighborID=col==OrderBox.Columns-1?-1:i+1;
   c.upNeighborID=row==0?1000+(OrderBox.Rows-1)*OrderBox.Columns+col:i-OrderBox.Columns;c.downNeighborID=i+OrderBox.Columns<Backpack.inventory.Count?i+OrderBox.Columns:-1;all.Add(c);
  }
  allClickableComponents=all;
  if(keep>=0&&Game1.options.SnappyMenus)
  {
   currentlySnappedComponent=getComponentWithID(keep)??getComponentWithID(RowTarget(keep-200))??getComponentWithID(1000);
   if(currentlySnappedComponent!=null&&currentlySnappedComponent.myID!=keep)snapCursorToCurrentSnappedComponent();
  }
 }
 public override void snapToDefaultClickableComponent()
 {
  currentlySnappedComponent=getComponentWithID(200)??getComponentWithID(1000);
  snapCursorToCurrentSnappedComponent();
 }
 public override void applyMovementKey(int direction)
 {
  // Moving past the first/last visible order scrolls the list instead of leaving it.
  if(currentlySnappedComponent is {} c&&c.myID is >=200 and <300)
  {
   int n=c.myID-200,count=RowsFor().Count;
   if(direction==2&&n==Rows-1&&Scroll+Rows<count){Scroll++;BuildComponents();Game1.playSound("shiny4");return;}
   if(direction==0&&n==0&&Scroll>0){Scroll--;BuildComponents();Game1.playSound("shiny4");return;}
  }
  base.applyMovementKey(direction);
 }
 private void SwitchTab(int tab){Tab=(tab+3)%3;Scroll=0;ConfirmReject=null;BuildComponents();Game1.playSound("shwip");
  if(Game1.options.SnappyMenus){currentlySnappedComponent=getComponentWithID(200)??getComponentWithID(100+Tab);snapCursorToCurrentSnappedComponent();}}
 public override void receiveGamePadButton(Buttons b)
 {
  switch(b)
  {
   case Buttons.LeftShoulder:SwitchTab(Tab-1);break;
   case Buttons.RightShoulder:SwitchTab(Tab+1);break;
   case Buttons.LeftTrigger:Scroll=Math.Max(0,Scroll-Rows);BuildComponents();break;
   case Buttons.RightTrigger:Scroll=Math.Min(Math.Max(0,RowsFor().Count-Rows),Scroll+Rows);BuildComponents();break;
   case Buttons.B:receiveKeyPress(Keys.Escape);break;
  }
 }
 public override void gameWindowSizeChanged(Rectangle oldBounds,Rectangle newBounds)=>Layout();
 private void Refresh()
 {
  try{Book=Service.Read();}catch{Service.Notice.Value="orders.error";Book=new();}
  Scroll=Math.Clamp(Scroll,0,Math.Max(0,RowsFor().Count-Rows));
  if(BoxMenu!=null)BuildComponents();
 }
 private List<MarketOrder> RowsFor()=>Tab==0?Book.Available.Concat(Book.Active.Where(o=>o.State=="legacy-active"&&o.Actor==Game1.player.UniqueMultiplayerID)).ToList()
  :Tab==1?Book.Active.Where(o=>o.State=="pending").OrderBy(o=>o.Box==Key?0:1).ToList():Book.Completed;

 public override void update(GameTime time)
 {
  base.update(time);
  var mutex=OrderBox.Mutex(Key);mutex.Update(Game1.getOnlineFarmers());
  if(!Service.InReach(Game1.player,Location,TileX,TileY)||!mutex.IsLockHeld()){exitThisMenu();return;}
  Elapsed+=time.ElapsedGameTime.TotalSeconds;if(Elapsed<.25)return;Elapsed=0;
  Refresh();
  // A cancelled order: its goods are released, so hand them back to the backpack.
  if(PendingCancel!=null&&!Service.HasPending)
  {
   string tag=PendingCancel;PendingCancel=null;
   if(!OrderBox.Pending(Book,Key).Any(o=>o.Reservation==tag))
    for(int i=0;i<Box.Count;i++)if(Box[i] is {} item&&OrderBox.Tag(item)==tag){item.modData.Remove(OrderBox.ReserveKey);Box[i]=Game1.player.addItemToInventory(item)!;}
  }
  if(!Service.HasPending)OrderBox.StripStale(Box,Book,Key);
 }
 protected override void cleanupBeforeExit(){Release();base.cleanupBeforeExit();}
 public override void emergencyShutDown(){Release();base.emergencyShutDown();}
 private void Release(){var mutex=OrderBox.Mutex(Key);if(mutex.IsLockHeld())mutex.ReleaseLock();}

 // ---------- items ----------
 private bool Locked(Item? item)=>OrderBox.Locked(item,Book,Key);
 private int RewardSlots(MarketOrder? extra=null,int reserved=0)=>OrderBox.RewardSlotsNeeded(Book,Box,Key,extra,reserved);
 /// <summary>Into the box: onto matching free stacks first, then an empty slot (keeping room for tonight's item rewards).</summary>
 private Item? AddToBox(Item item)
 {
  var box=Box;OrderBox.Pad(box);
  for(int i=0;i<box.Count&&item.Stack>0;i++)
   if(box[i] is {} slot&&OrderBox.Tag(slot)==null&&slot.canStackWith(item)){int room=slot.maximumStackSize()-slot.Stack;if(room<=0)continue;int move=Math.Min(room,item.Stack);slot.Stack+=move;item.Stack-=move;}
  if(item.Stack<=0)return null;
  if(OrderBox.Free(box)<=RewardSlots()){Service.Notice.Value=OrderBox.Free(box)==0?"orders.box-full":"orders.box-reward-room";return item;}
  for(int i=0;i<OrderBox.Capacity;i++)if(box[i]==null){box[i]=item;return null;}
  return item;
 }
 private void FromBackpack(int slot,bool one)
 {
  var items=Game1.player.Items;if(slot<0||slot>=items.Count||items[slot] is not {} item)return;
  if(one&&item.Stack>1){var single=item.getOne();single.Stack=1;if(AddToBox(single)==null){item.Stack--;Game1.playSound("stoneStep");}return;}
  items[slot]=AddToBox(item)!;Game1.playSound("stoneStep");
 }
 private void FromBox(int slot,bool one)
 {
  var box=Box;if(slot<0||slot>=box.Count||box[slot] is not {} item)return;
  if(Locked(item)){Service.Notice.Value="orders.box-locked";Game1.playSound("cancel");return;}
  item.modData.Remove(OrderBox.ReserveKey);
  if(one&&item.Stack>1){var single=item.getOne();single.Stack=1;if(Game1.player.addItemToInventory(single)==null){item.Stack--;Game1.playSound("stoneStep");}else Service.Notice.Value="orders.backpack-full";return;}
  var rest=Game1.player.addItemToInventory(item);box[slot]=rest!;
  if(rest!=null)Service.Notice.Value="orders.backpack-full";else Game1.playSound("stoneStep");
 }

 // ---------- orders ----------
 private int InBox(MarketOrder o)=>(int)Math.Min(int.MaxValue,Box.Where(i=>i!=null&&!Locked(i)&&OrderBox.Goods(i,o)).Sum(i=>(long)i.Stack));
 private int InBackpack(MarketOrder o)=>(int)Math.Min(int.MaxValue,Game1.player.Items.Where(i=>OrderBox.Goods(i,o)&&OrderBox.Tag(i)==null).Sum(i=>(long)i!.Stack));
 private bool CanConfirm(MarketOrder o)=>!Service.HasPending&&Service.Enabled&&InBox(o)+InBackpack(o)>=o.Quantity;
 private void Confirm(MarketOrder o)
 {
  if(!CanConfirm(o)){Service.Notice.Value=Service.HasPending?"orders.pending":"orders.not-enough";return;}
  var box=Box;OrderBox.Pad(box);OrderBox.StripStale(box,Book,Key);
  // Missing goods come from the backpack (lowest quality first, best kept for the player).
  int missing=o.Quantity-InBox(o);
  foreach(int i in Enumerable.Range(0,Game1.player.Items.Count).Where(i=>OrderBox.Goods(Game1.player.Items[i],o)&&OrderBox.Tag(Game1.player.Items[i])==null)
   .OrderBy(i=>Game1.player.Items[i].Quality).ThenBy(i=>i).ToArray())
  {
   if(missing<=0)break;var item=Game1.player.Items[i];int take=Math.Min(missing,item.Stack);
   var part=take==item.Stack?item:item.getOne();part.Stack=take;
   var rest=AddToBox(part);int moved=take-(rest?.Stack??0);
   if(ReferenceEquals(part,item))Game1.player.Items[i]=rest!;else item.Stack-=moved;
   missing-=moved;if(rest!=null)break;
  }
  if(InBox(o)<o.Quantity){Service.Notice.Value="orders.box-full";return;}
  // Tag exactly the ordered amount; a partly used stack is split so the rest stays free.
  string tag=Guid.NewGuid().ToString("N");int need=o.Quantity,tagged=0;
  foreach(int i in Enumerable.Range(0,box.Count).Where(i=>box[i] is {} it&&!Locked(it)&&OrderBox.Goods(it,o)).OrderBy(i=>box[i].Quality).ThenBy(i=>i).ToArray())
  {
   if(need<=0)break;var item=box[i];
   if(item.Stack<=need){item.modData[OrderBox.ReserveKey]=tag;need-=item.Stack;tagged++;continue;}
   int empty=Enumerable.Range(0,OrderBox.Capacity).FirstOrDefault(n=>box[n]==null,-1);
   if(empty<0||OrderBox.Free(box)<=RewardSlots(o,tagged+1)){Undo(tag);Service.Notice.Value="orders.box-full";return;}
   var part=item.getOne();part.Stack=need;item.Stack-=need;part.modData[OrderBox.ReserveKey]=tag;box[empty]=part;need=0;tagged++;
  }
  if(need>0||OrderBox.Free(box)<RewardSlots(o,tagged)){Undo(tag);Service.Notice.Value=need>0?"orders.not-enough":"orders.box-reward-space";return;}
  Service.Request("confirm",o,Location,TileX,TileY,Key,tag);
  Game1.playSound("purchaseClick");
 }
 private void Undo(string tag){foreach(var item in Box)if(item!=null&&OrderBox.Tag(item)==tag)item.modData.Remove(OrderBox.ReserveKey);}
 private void Cancel(MarketOrder o)
 {
  if(Service.HasPending||o.Box!=Key||o.Actor!=Game1.player.UniqueMultiplayerID)return;
  PendingCancel=o.Reservation;Service.Request("cancel",o,Location,TileX,TileY,Key,o.Reservation);Game1.playSound("trashcanlid");
 }
 private void Reject(MarketOrder o)
 {
  if(Service.HasPending||o.State!="available")return;
  if(ConfirmReject!=o.Id){ConfirmReject=o.Id;Game1.playSound("smallSelect");return;}
  ConfirmReject=null;Service.Request("reject",o,Location,TileX,TileY);Game1.playSound("trashcan");
 }

 // ---------- geometry ----------
 private Rectangle TabRect(int n)=>new(Left.X+16+n*((LeftW-32)/3),Left.Y+16,(LeftW-32)/3-6,44);
 private Rectangle RowRect(int n)=>new(Left.X+12,Left.Y+70+n*(RowH+6),LeftW-24,RowH);
 private static Rectangle ButtonRect(Rectangle row)=>new(row.Right-118,row.Bottom-46,106,38);
 private static Rectangle RejectRect(Rectangle row)=>new(row.Right-34,row.Y+6,26,26);
 private Rectangle UpRect=>new(Left.X+LeftW/2-60,Left.Bottom-56,52,34);
 private Rectangle DownRect=>new(Left.X+LeftW/2+8,Left.Bottom-56,52,34);

 public override void receiveLeftClick(int x,int y,bool playSound=true)
 {
  if(upperRightCloseButton.containsPoint(x,y)){exitThisMenu();return;}
  for(int n=0;n<3;n++)if(TabRect(n).Contains(x,y)){SwitchTab(n);return;}
  if(RowsFor().Count>Rows&&UpRect.Contains(x,y)){Scroll=Math.Max(0,Scroll-1);BuildComponents();return;}
  if(RowsFor().Count>Rows&&DownRect.Contains(x,y)){Scroll=Math.Min(Math.Max(0,RowsFor().Count-Rows),Scroll+1);BuildComponents();return;}
  var rows=RowsFor();
  for(int n=0;n<Rows&&Scroll+n<rows.Count;n++)
  {
   var row=RowRect(n);var o=rows[Scroll+n];
   if(Tab==0&&o.State=="available"&&RejectRect(row).Contains(x,y)){Reject(o);return;}
   if(ButtonRect(row).Contains(x,y)){if(Tab==0)Confirm(o);else if(Tab==1)Cancel(o);return;}
  }
  ConfirmReject=null;
  int b=BoxMenu.getInventoryPositionOfClick(x,y);if(b>=0){FromBox(b,false);return;}
  int p=Backpack.getInventoryPositionOfClick(x,y);if(p>=0){FromBackpack(p,false);return;}
 }
 public override void receiveRightClick(int x,int y,bool playSound=true)
 {
  int b=BoxMenu.getInventoryPositionOfClick(x,y);if(b>=0){FromBox(b,true);return;}
  int p=Backpack.getInventoryPositionOfClick(x,y);if(p>=0)FromBackpack(p,true);
 }
 public override void receiveScrollWheelAction(int direction)
 {
  if(!Left.Contains(Game1.getMouseX(),Game1.getMouseY()))return;
  Scroll=Math.Clamp(Scroll+(direction<0?1:-1),0,Math.Max(0,RowsFor().Count-Rows));BuildComponents();
 }
 public override void receiveKeyPress(Keys key)
 {
  if(key==Keys.Escape||Game1.options.doesInputListContain(Game1.options.menuButton,key)){if(ConfirmReject!=null)ConfirmReject=null;else exitThisMenu();return;}
  if(Game1.options.SnappyMenus){base.receiveKeyPress(key);return;} // controller: d-pad/stick move between buttons and slots
  if(key==Keys.Tab)SwitchTab(Tab+1);
 }
 public override bool readyToClose()=>true;
 public override void performHoverAction(int x,int y)
 {
  base.performHoverAction(x,y);HoverOrder=null;HoverText="";
  HoverItem=BoxMenu.getItemAt(x,y)??Backpack.getItemAt(x,y);
  var rows=RowsFor();
  for(int n=0;n<Rows&&Scroll+n<rows.Count;n++)if(RowRect(n).Contains(x,y))
  {
   var o=rows[Scroll+n];HoverOrder=o;
   HoverText=OrderCatalog.ItemName(o.Item)+" × "+o.Quantity+"\n"+Tr("quality."+o.MinimumQuality)+"\n\""+Tr("quote."+o.Npc)+"\"";
  }
 }

 // ---------- drawing ----------
 private static void Fill(SpriteBatch b,Rectangle r,Color c){if(r.Width>0&&r.Height>0)b.Draw(Game1.staminaRect,r,c);}
 private static void Frame(SpriteBatch b,Rectangle r,Color c,int t){Fill(b,new(r.X,r.Y,r.Width,t),c);Fill(b,new(r.X,r.Bottom-t,r.Width,t),c);Fill(b,new(r.X,r.Y,t,r.Height),c);Fill(b,new(r.Right-t,r.Y,t,r.Height),c);}
 private static void Panel(SpriteBatch b,Rectangle r,Color color,bool selected=false)
 {
  Fill(b,r,Ink);Fill(b,new(r.X+2,r.Y+2,r.Width-4,r.Height-4),selected?Gold:Wood);
  Fill(b,new(r.X+5,r.Y+5,r.Width-10,r.Height-10),color);Fill(b,new(r.X+5,r.Y+5,r.Width-10,2),Color.White*.3f);
 }
 private void Text(SpriteBatch b,string value,int x,int y,int maxWidth,float size=1,Color? color=null,bool title=false)
 {
  var font=title?Game1.dialogueFont:Game1.smallFont;
  string text=DisplayText.Ellipsis(value??"",maxWidth,s=>font.MeasureString(s).X*size);
  b.DrawString(font,text,new Vector2(x,y),color??Ink,0,Vector2.Zero,size,SpriteEffects.None,.95f);
 }
 private void CenterText(SpriteBatch b,string value,Rectangle r,float size,Color color)
 {
  string text=DisplayText.Ellipsis(value,r.Width-8,s=>Game1.smallFont.MeasureString(s).X*size);var m=Game1.smallFont.MeasureString(text)*size;
  b.DrawString(Game1.smallFont,text,new Vector2(r.X+(r.Width-m.X)/2,r.Y+(r.Height-m.Y)/2+2),color,0,Vector2.Zero,size,SpriteEffects.None,.95f);
 }
 private static void Icon(SpriteBatch b,string id,Rectangle r)
 {
  var data=ItemRegistry.GetDataOrErrorItem(id);var source=data.GetSourceRect();float scale=Math.Min(r.Width/(float)source.Width,r.Height/(float)source.Height);
  int w=(int)(source.Width*scale),h=(int)(source.Height*scale);b.Draw(data.GetTexture(),new Rectangle(r.X+(r.Width-w)/2,r.Y+(r.Height-h)/2,w,h),source,Color.White);
 }
 private static void Check(SpriteBatch b,int x,int y,int s=20)
 {
  Fill(b,new(x,y,s,s),new Color(20,80,30));Fill(b,new(x+2,y+2,s-4,s-4),Tick);
  int u=Math.Max(2,s/10);
  for(int i=0;i<3;i++)Fill(b,new(x+s/4+i*u,y+s/2+i*u-u,u+1,u+1),Color.White);
  for(int i=0;i<5;i++)Fill(b,new(x+s/4+2*u+i*u,y+s/2+u-i*u-u,u+1,u+1),Color.White);
 }
 private void Portrait(SpriteBatch b,string npc,Rectangle r)
 {
  Panel(b,new(r.X-4,r.Y-4,r.Width+8,r.Height+8),Pale);
  if(!Portraits.TryGetValue(npc,out var texture)){try{texture=Game1.content.Load<Texture2D>("Portraits/"+npc);}catch{texture=null;}Portraits[npc]=texture;}
  if(texture!=null)b.Draw(texture,r,new Rectangle(0,0,64,64),Color.White);
 }
 private string Name(string npc){if(!Names.TryGetValue(npc,out var n))Names[npc]=n=OrderCatalog.NpcName(npc);return n;}
 private string PlayerName(long id)=>Game1.GetPlayer(id)?.Name??"?";

 public override void draw(SpriteBatch b)
 {
  b.Draw(Game1.fadeToBlackRect,new Rectangle(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height),Color.Black*.5f);
  DrawOrders(b);DrawBox(b);
  upperRightCloseButton.draw(b);
  if(HoverItem!=null)drawToolTip(b,HoverItem.getDescription(),HoverItem.DisplayName,HoverItem);
  else if(HoverText.Length>0)drawHoverText(b,DisplayText.Wrap(HoverText,420,s=>Game1.smallFont.MeasureString(s).X),Game1.smallFont);
  drawMouse(b);
 }
 private void DrawOrders(SpriteBatch b)
 {
  Panel(b,Left,new Color(188,123,63));
  string[] tabs={Tr("available")+" "+Book.Available.Count,Tr("tab-confirmed")+" "+Book.Active.Count(o=>o.State=="pending"),Tr("completed")};
  for(int n=0;n<3;n++){var r=TabRect(n);Panel(b,r,n==Tab?Paper:new Color(211,172,113),n==Tab);CenterText(b,tabs[n],r,.82f,Ink);}
  var rows=RowsFor();
  if(rows.Count==0)
  {
   string empty=Tr(Tab==0?"empty-available":Tab==1?"box-empty-confirmed":"empty-completed");
   int line=0;foreach(var s in DisplayText.Wrap(empty,LeftW-60,t=>Game1.smallFont.MeasureString(t).X*.85f).Split('\n'))Text(b,s,Left.X+30,Left.Y+90+line++*28,LeftW-60,.85f,Muted);
  }
  for(int n=0;n<Rows&&Scroll+n<rows.Count;n++)DrawRow(b,rows[Scroll+n],RowRect(n));
  if(rows.Count>Rows)
  {
   Panel(b,UpRect,Scroll>0?Pale:Disabled);b.Draw(Game1.mouseCursors,new Vector2(UpRect.Center.X-22,UpRect.Y+2),new Rectangle(421,459,11,12),Color.White,0,Vector2.Zero,2.5f,SpriteEffects.None,.96f);
   Panel(b,DownRect,Scroll+Rows<rows.Count?Pale:Disabled);b.Draw(Game1.mouseCursors,new Vector2(DownRect.Center.X-14,DownRect.Y+2),new Rectangle(421,472,11,12),Color.White,0,Vector2.Zero,2.5f,SpriteEffects.None,.96f);
  }
  string notice=Service.Notice.Value;
  if(Tab==2&&ConfirmReject==null&&string.IsNullOrEmpty(notice))
  {
   // Completed tab: the income summary takes the footer (last night and this season, in gold).
   var (last,season,_)=Service.Earnings();
   Text(b,Tr("income-summary",new{last=Service.Number(last),season=Service.Number(season)}),Left.X+20,Left.Bottom-(rows.Count>Rows?104:62),LeftW-40,.9f,new Color(160,100,20),true);
   return;
  }
  string footer=ConfirmReject!=null?Tr("reject-question"):string.IsNullOrEmpty(notice)?Tr("desk-footer"):Service.Tr(notice);
  int lines=0;foreach(var s in DisplayText.Wrap(footer,LeftW-40,t=>Game1.smallFont.MeasureString(t).X*.72f).Split('\n').Take(2))
   Text(b,s,Left.X+20,Left.Bottom-(rows.Count>Rows?104:62)+lines++*22,LeftW-40,.72f,Ink);
 }
 private void DrawRow(SpriteBatch b,MarketOrder o,Rectangle r)
 {
  bool hover=HoverOrder?.Id==o.Id;bool pendingHere=o.State=="pending"&&o.Box==Key;
  Panel(b,r,hover?Paper:new Color(242,215,166),pendingHere);
  Portrait(b,o.Npc,new(r.X+12,r.Y+14,64,64));
  Text(b,Name(o.Npc),r.X+88,r.Y+8,r.Width-130,.9f);
  Icon(b,o.Item,new(r.X+88,r.Y+36,24,24));Text(b,OrderCatalog.ItemName(o.Item)+" × "+o.Quantity,r.X+116,r.Y+36,r.Width-130,.82f);
  int rx=r.X+88,ry=r.Y+64;
  if(o.Gold>0)Text(b,Service.Number(o.Gold)+"g",rx,ry,120,.82f,new Color(160,100,20));
  else foreach(var reward in o.Rewards){Icon(b,reward.Id,new(rx,ry,22,22));Text(b,"×"+reward.Count,rx+24,ry,40,.75f,Muted);rx+=64;}
  var button=ButtonRect(r);
  if(Tab==0)
  {
   if(o.State=="available"){var x=RejectRect(r);bool armed=ConfirmReject==o.Id;Fill(b,x,armed?Red:new Color(196,150,96));CenterText(b,armed?"!":"×",x,.8f,Color.White);}
   int have=InBox(o)+InBackpack(o);bool ok=CanConfirm(o);
   Text(b,Tr("have",new{count=Math.Min(have,99999),quantity=o.Quantity}),r.X+210,r.Y+64,button.X-r.X-214,.72f,have>=o.Quantity?Tick:Muted);
   Panel(b,button,ok?Green:Disabled);CenterText(b,Tr(Service.HasPending?"processing":"confirm"),button,.82f,Color.White);
  }
  else if(Tab==1)
  {
   if(pendingHere&&o.Actor==Game1.player.UniqueMultiplayerID){Panel(b,button,Service.HasPending?Disabled:Red);CenterText(b,Tr("cancel"),button,.82f,Color.White);}
   else Text(b,Tr("confirmed-by",new{name=PlayerName(o.Actor)}),button.X-10,button.Y+8,button.Width+10,.66f,Muted);
   Check(b,r.X+210,r.Y+64,18);Text(b,Tr(o.Box==""?"settles-tonight":"deliver-tonight"),r.X+234,r.Y+64,button.X-r.X-240,.7f,Tick);
  }
  else Text(b,Tr("completed-day",new{day=o.Completed+1}),r.X+210,r.Y+64,r.Width-220,.72f,Muted);
 }
 private void DrawBox(SpriteBatch b)
 {
  Panel(b,Right,new Color(188,123,63));
  Text(b,Tr("box-title"),Right.X+20,Right.Y+14,300,1,Ink,true);
  Text(b,Tr("box-hint"),Right.X+300,Right.Y+28,Right.Width-360,.75f,Muted);
  var grid=new Rectangle(BoxMenu.xPositionOnScreen-8,BoxMenu.yPositionOnScreen-8,OrderBox.Columns*64+16,OrderBox.Rows*64+16);
  Fill(b,grid,Pale);
  BoxMenu.draw(b);
  for(int i=0;i<BoxMenu.inventory.Count&&i<Box.Count;i++)
  {
   var item=Box[i];if(item==null)continue;var slot=BoxMenu.inventory[i].bounds;
   if(Locked(item)){Frame(b,slot,Tick,4);Check(b,slot.Right-22,slot.Y+2);}
   else if(HoverOrder!=null&&Tab==0&&OrderBox.Goods(item,HoverOrder))Frame(b,slot,Gold,3);
  }
  int py=Backpack.yPositionOnScreen;
  Text(b,Tr("backpack"),Right.X+20,py-40,300,.9f,Ink);
  Fill(b,new(Backpack.xPositionOnScreen-8,py-8,OrderBox.Columns*64+16,3*64+16),Pale);
  Backpack.draw(b);
  if(HoverOrder!=null&&Tab==0)
   for(int i=0;i<Backpack.inventory.Count&&i<Game1.player.Items.Count;i++)
    if(OrderBox.Goods(Game1.player.Items[i],HoverOrder))Frame(b,Backpack.inventory[i].bounds,Gold,3);
 }
}

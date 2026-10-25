using BetterStardewValley.Transactions;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.BigCraftables;
using StardewValley.Locations;
using StardewValley.Objects;
using SObject=StardewValley.Object;
namespace BetterStardewValley.Orders;

internal sealed class OrderTerminal
{
 private static OrderTerminal Instance=null!;private readonly IModHelper Helper;private readonly IMonitor Monitor;
 private bool Warned;private Texture2D? WorldTexture,IconTexture;
 [ThreadStatic] private static int CursorDepth;
 [ThreadStatic] private static bool CursorHasPreview;
 private readonly record struct CursorState(int Depth,bool Preview);
 internal const string MailId="Eddie.BetterStardewValley_OrderTerminalDelivery";
 internal const string LegacyDeliveryKey=OrderRules.Prefix+"TerminalMailLegacy.v1";
 private const string Asset="Mods/Eddie.BetterStardewValley/OrderTerminal";
 internal static bool IsTerminal(Item? i)=>i?.QualifiedItemId=="(BC)"+OrderRules.TerminalId;
 internal OrderTerminal(IModHelper helper,IMonitor monitor,Harmony h)
 {
  Instance=this;Helper=helper;Monitor=monitor;
  h.Patch(DrawPatchTargets.Require(typeof(SObject),nameof(SObject.checkForAction),typeof(bool),typeof(Farmer),typeof(bool)),prefix:new HarmonyMethod(typeof(OrderTerminal),nameof(Interact)));
  h.Patch(DrawPatchTargets.Require(typeof(SObject),nameof(SObject.drawWhenHeld),typeof(void),typeof(SpriteBatch),typeof(Vector2),typeof(Farmer)),prefix:new HarmonyMethod(typeof(OrderTerminal),nameof(DrawHeld)));
  h.Patch(AccessTools.Method(typeof(SObject),nameof(SObject.draw),new[]{typeof(SpriteBatch),typeof(int),typeof(int),typeof(float)}),prefix:new HarmonyMethod(typeof(OrderTerminal),nameof(DrawWorld)));
  h.Patch(DrawPatchTargets.ObjectMenu(),prefix:new HarmonyMethod(typeof(OrderTerminal),nameof(DrawMenu)));
  h.Patch(DrawPatchTargets.Require(typeof(Game1),nameof(Game1.drawMouseCursor),typeof(void)),
   prefix:new HarmonyMethod(typeof(OrderTerminal),nameof(BeginCursor)),finalizer:new HarmonyMethod(typeof(OrderTerminal),nameof(EndCursor)));
  _=new TerminalWorld(helper,monitor,h);
  _=new TerminalReplacement(helper,monitor);
  helper.Events.Content.AssetRequested+=Assets;
  helper.Events.GameLoop.SaveLoaded+=(_,_)=>ScheduleMail();
  helper.Events.GameLoop.DayStarted+=(_,_)=>ScheduleMail();
  helper.Events.GameLoop.OneSecondUpdateTicked+=(_,_)=>ScheduleMail();
  helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>Warned=false;
  helper.Events.Content.AssetsInvalidated+=(_,_)=>{WorldTexture=null;IconTexture=null;};
 }
 private string Tr(string key)=>BsvI18n.Get(Helper,key);
 private static void BeginCursor(out CursorState __state)
 { __state=new(CursorDepth,CursorHasPreview);CursorDepth++;CursorHasPreview=false; }
 private static void EndCursor(CursorState __state)
 { CursorDepth=__state.Depth;CursorHasPreview=__state.Preview; }
 internal static void MarkPlacementPreview(){if(CursorDepth>0)CursorHasPreview=true;}
 private void Assets(object? sender,AssetRequestedEventArgs e)
 {
  if(e.NameWithoutLocale.IsEquivalentTo(Asset)){e.LoadFromModFile<Texture2D>("assets/orders/terminal-native.png",AssetLoadPriority.Exclusive);return;}
  if(e.NameWithoutLocale.IsEquivalentTo("Data/mail"))e.Edit(a=>a.AsDictionary<string,string>().Data[MailId]=Tr("orders.terminal-mail")+"%item bigobject "+OrderRules.TerminalId+" %%[#]"+Tr("orders.terminal-mail-title"), BetterStardewValley.BsvAssets.EditPriority);
  if(e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))e.Edit(a=>a.AsDictionary<string,BigCraftableData>().Data[OrderRules.TerminalId]=new()
  {Name="BSV Order Terminal",DisplayName=Tr("orders.terminal-name"),Description=Tr("orders.terminal-description"),Texture=Asset,SpriteIndex=0,Price=0,Fragility=0,CanBePlacedIndoors=true,CanBePlacedOutdoors=true}, BetterStardewValley.BsvAssets.EditPriority);
 }
 // A — use each local farmer's native next-day mail queue. Native mail owns attachment claiming.
 private void ScheduleMail()
 {
  if(!Context.IsWorldReady||IntegrationHost.Saving||Game1.eventUp)return;
  try
  {
   var who=Game1.player;
   foreach(var item in who.Items.OfType<SObject>().Where(IsTerminal))if(item.Fragility!=0)item.Fragility=0;
   if(Context.IsMainPlayer&&Game1.getLocationFromName("FarmHouse") is FarmHouse oldHome)
    foreach(var item in oldHome.objects.Values.Where(IsTerminal))if(item.Fragility!=0)item.Fragility=0;
   if(who.mailReceived.Contains(MailId)||who.mailbox.Contains(MailId)||who.mailForTomorrow.Contains(MailId)||who.modData.ContainsKey(LegacyDeliveryKey))return;
   // Migrate an already delivered 1.1.3 terminal without granting a second host copy.
   bool legacy=who.Items.Any(IsTerminal);
   if(Context.IsMainPlayer)
   {
    legacy|=Game1.getFarm().modData.TryGetValue(OrderRules.SpawnKey,out string status)&&status=="placed";
    legacy|=Game1.getLocationFromName("FarmHouse") is FarmHouse house&&house.objects.Values.Any(IsTerminal);
   }
   if(!legacy)Utility.ForEachLocation(l=>
   {
    legacy=l.objects.Values.Any(o=>IsTerminal(o)&&!TerminalEntity.IsPart(o)&&o.owner.Value==who.UniqueMultiplayerID);
    return !legacy;
   },includeInteriors:true,includeGenerated:true);
   if(legacy){who.modData[LegacyDeliveryKey]="received-before-mail";return;}
   who.mailForTomorrow.Add(MailId);
  }
  catch(Exception ex){if(!Warned){Warned=true;Monitor.Log(Tr("orders.terminal-mail-error")+"\n"+ex,LogLevel.Error);}}
 }
 private static bool Interact(SObject __instance,Farmer who,bool justCheckingForActivity,ref bool __result)
 {
  if(!IsTerminal(__instance))return true;__result=true;
  if(!justCheckingForActivity&&Context.IsWorldReady&&who.UniqueMultiplayerID==Game1.player.UniqueMultiplayerID&&Game1.activeClickableMenu==null&&!Game1.eventUp
   &&TerminalEntity.Resolve(who.currentLocation,__instance.TileLocation,out var anchor))
  {
   if(!TerminalEntity.Allowed(who.currentLocation,who)){Game1.addHUDMessage(new HUDMessage(Instance.Tr("orders.terminal-map"),3));return false;}
   if(!TerminalEntity.CanUse(who,who.currentLocation.NameOrUniqueName,(int)anchor.TileLocation.X,(int)anchor.TileLocation.Y))return false;
   if(TerminalEntity.Pending(anchor))Game1.addHUDMessage(new HUDMessage(Instance.Tr("orders.terminal-migrate"),3));
   if(!OrdersService.Instance.Enabled){Game1.drawObjectDialogue(Instance.Tr("orders.disabled"));return false;}
   string id=TerminalEntity.Id(anchor);if(id.Length==0)return false;
   string key=OrderBox.Key(id),location=who.currentLocation.NameOrUniqueName;int x=(int)anchor.TileLocation.X,y=(int)anchor.TileLocation.Y;
   // Like a native chest: one player at a time uses a terminal's order box.
   OrderBox.Items(key); // create the box on first use so its lock is advanced every tick
   OrderBox.Mutex(key).RequestLock(()=>
   {
    if(Game1.activeClickableMenu==null&&TerminalEntity.CanUse(Game1.player,location,x,y))
     Game1.activeClickableMenu=new OrderDeskMenu(OrdersService.Instance,location,x,y,key);
    else OrderBox.Mutex(key).ReleaseLock();
   },()=>Game1.addHUDMessage(new HUDMessage(Instance.Tr("orders.box-busy"),3)));
  }
  return false;
 }
 private static bool DrawHeld(SObject __instance,SpriteBatch spriteBatch,Vector2 objectPosition,Farmer f)
 {
  if(!IsTerminal(__instance))return true;Instance.IconTexture??=Instance.Helper.ModContent.Load<Texture2D>("assets/orders/terminal-icon.png");
  spriteBatch.Draw(Instance.IconTexture,objectPosition,null,Color.White,0,Vector2.Zero,1,SpriteEffects.None,Math.Max(0,(f.StandingPixel.Y+3)/10000f));return false;
 }
 private static bool DrawWorld(SObject __instance,SpriteBatch spriteBatch,int x,int y,float alpha)
 {
  if(!IsTerminal(__instance))return true;
  if(!TerminalEntity.IsPart(__instance))DrawPlaced(spriteBatch,new(x,y),alpha,TerminalEntity.Pending(__instance));
  return false;
 }
 internal static void DrawPlaced(SpriteBatch spriteBatch,Vector2 tile,float alpha,bool legacy)
 {
  Instance.WorldTexture??=Instance.Helper.ModContent.Load<Texture2D>("assets/orders/terminal-world.png");
  float depth=Math.Max(0,(TerminalEntity.VisualBounds(tile).Bottom-1)/10000f);
  // Soft ground shadow under the desk, the same shadow sprite the game uses under things standing on the floor.
  var foot=Game1.GlobalToLocal(Game1.viewport,new Vector2(tile.X*64+TerminalEntity.Width*32,(tile.Y+TerminalEntity.Depth)*64-6));
  var sb=Game1.shadowTexture.Bounds;
  spriteBatch.Draw(Game1.shadowTexture,foot,sb,Color.White*(0.45f*alpha),0,new Vector2(sb.Width/2f,sb.Height/2f),
   new Vector2(104f/sb.Width,18f/sb.Height),SpriteEffects.None,Math.Max(0,depth-0.0001f));
  spriteBatch.Draw(Instance.WorldTexture,Game1.GlobalToLocal(Game1.viewport,TerminalEntity.DrawPosition(tile)),TerminalEntity.WorldSource,
   Color.White*alpha,0,Vector2.Zero,TerminalEntity.WorldScale,SpriteEffects.None,depth);
 }
 private static bool DrawMenu(SObject __instance,SpriteBatch spriteBatch,Vector2 location,float scaleSize,float transparency,float layerDepth,StackDrawType drawStackNumber,Color color)
 {
  if(!IsTerminal(__instance))return true;
  // Game1 draws a second carried-item menu icon after drawPlacementBounds. Suppress
  // it only in that cursor pass when our complete world preview was actually drawn.
  if(CursorDepth>0&&CursorHasPreview&&Game1.activeClickableMenu==null&&ReferenceEquals(Game1.player.ActiveObject,__instance))return false;
  Instance.IconTexture??=Instance.Helper.ModContent.Load<Texture2D>("assets/orders/terminal-icon.png");
  spriteBatch.Draw(Instance.IconTexture,location+new Vector2(32),null,color*transparency,0,new Vector2(32),scaleSize,SpriteEffects.None,layerDepth);
  __instance.DrawMenuIcons(spriteBatch,location,scaleSize,transparency,layerDepth,drawStackNumber,color);return false;
 }
}

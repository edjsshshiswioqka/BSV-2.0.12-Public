using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Xml.Serialization;
using BetterStardewValley.Orders;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Tools;
using StardewValley.TerrainFeatures;
using xTile.Tiles;
using SObject=StardewValley.Object;
partial class Program
{
 record TerminalDraw(Vector2 Position,Rectangle? Source,float Scale,float Depth);
 static readonly List<TerminalDraw> TerminalDraws=new();
 static readonly List<Rectangle> TerminalCells=new();
 static bool CaptureTerminal(Texture2D texture,Vector2 position,Rectangle? sourceRectangle,Color color,float rotation,Vector2 origin,float scale,SpriteEffects effects,float layerDepth)
 {TerminalDraws.Add(new(position,sourceRectangle,scale,layerDepth));return false;}
 static bool CaptureTerminalVector(Texture2D texture,Vector2 position,Rectangle? sourceRectangle,Color color,float rotation,Vector2 origin,Vector2 scale,SpriteEffects effects,float layerDepth)
 {if(scale.X!=scale.Y)throw new Exception("nonuniform terminal scale");TerminalDraws.Add(new(position,sourceRectangle,scale.X,layerDepth));return false;}
 static bool CaptureTerminalCell(Texture2D texture,Rectangle destinationRectangle,Color color){TerminalCells.Add(destinationRectangle);return false;}
 static bool TerminalHit(Vector2 tile)=>Farm.isCollidingPosition(new Rectangle((int)tile.X*64+8,(int)tile.Y*64+8,48,48),new(0,0,1536,1280),true,0,false,Host,false,false,false,true);
 static TerminalRequest TerminalIntent(Farmer who,string action,Vector2 tile,string entity="",long seq=1)=>new(1,Guid.NewGuid().ToString("N"),seq,who.UniqueMultiplayerID,action,who.currentLocation.NameOrUniqueName,(int)tile.X,(int)tile.Y,entity,action=="place"?TerminalItem.Pack(Item()):"");
 static void TerminalPending(Farmer who,TerminalRequest r)=>who.modData[TerminalNetwork.PendingKey]=JsonSerializer.Serialize(r);
 static void ContinuityTerminalChecks(Harmony h)
 {
  h.Patch(AccessTools.Method(typeof(HoeDirt),"initialize",new[]{typeof(GameLocation)}),prefix:new(typeof(Program),nameof(Skip)));
  Reset();
  var normal=ItemRegistry.Create<SObject>("(O)390");var tagged=ItemRegistry.Create<SObject>("(O)390");tagged.modData["source"]="foreign";
  Check(normal.canStackWith(tagged)&&!Eddie.FishingMachine.ProductionService.OutputCompatible(normal,tagged),"B07 native stack predicate ignores modData; production deposit correctly separates provenance");
  tagged.modData.Clear();tagged.preservedParentSheetIndex.Value="128";Check(!Eddie.FishingMachine.ProductionService.OutputCompatible(normal,tagged),"B07 native deposit guard preserves flavored source");
  tagged.preservedParentSheetIndex.Value=normal.preservedParentSheetIndex.Value;tagged.Price=normal.Price+1;Check(!Eddie.FishingMachine.ProductionService.OutputCompatible(normal,tagged),"B07 native deposit guard preserves per-item price variant");
  var pos=new Vector2(4,4);var a=Place();
  Check(Farm.objects.Count()==2&&TerminalEntity.Tiles(pos).SequenceEqual(new[]{pos,new Vector2(5,4)})&&TerminalEntity.Bounds(pos)==new Rectangle(256,256,128,64)&&TerminalEntity.VisualBounds(pos)==new Rectangle(256,192,128,128),"A01/A06 native 2x1 base and upward 2x2 display frame");
  foreach(var t in TerminalEntity.Tiles(pos))Check(TerminalEntity.Resolve(Farm,t,out var root)&&root==a&&TerminalHit(t)&&!Farm.objects[t].isPassable(),"A06 both bottom cells have native collision and resolve one anchor: "+t);
  foreach(var t in new[]{new Vector2(4,3),new Vector2(5,3),new Vector2(4,5),new Vector2(5,5)})Check(!TerminalHit(t)&&!TerminalEntity.Resolve(Farm,t,out _),"A06 artwork height adds no ground collision or interaction: "+t);
  foreach(var p in new[]{new Vector2(3,4),new Vector2(6,4),new Vector2(4,3),new Vector2(5,5)}){Put(Host,Farm,(int)p.X,(int)p.Y);Check(TerminalEntity.CanUse(Host,"Farm",4,4),"A06 physical interaction range from each side "+p);}
  foreach(string name in new[]{"Shed","Cabin","Town","Beach","IslandWest"})
  {
   Reset();var map=new GameLocation();map.name.Value=name;map.map=Map();map.IsOutdoors=name is not("Shed" or "Cabin");Put(Host,map,3,4);
   Check(TerminalEntity.CanPlace(map,pos,Host)&&World.Place(map,pos,Host,Guid.NewGuid().ToString("N"),Item(),()=>{})==null&&map.objects.Count()==2,"A02 mapped indoor/outdoor legal ground supported: "+name);
  }
  Reset();Put(Host,Home,3,4);Check(TerminalEntity.CanPlace(Home,pos,Host),"A01 farmhouse placement legal");
  Reset();var rug=new Furniture();rug.furniture_type.Value=12;rug.TileLocation=pos;rug.boundingBox.Value=new(256,256,128,64);Farm.furniture.Add(rug);
  Check(rug.isPassable()&&TerminalEntity.CanPlace(Farm,pos,Host),"A03 native passable rug permits both footprint cells");
  var chair=new Furniture();chair.furniture_type.Value=0;chair.TileLocation=new(5,4);chair.boundingBox.Value=new(320,256,64,64);Farm.furniture.Add(chair);
  Check(!TerminalEntity.CanPlace(Farm,pos,Host),"A03 overlapping rug cannot hide impassable furniture");
  Reset();Farm.terrainFeatures[pos]=new Flooring();Check(TerminalEntity.CanPlace(Farm,pos,Host),"A03 native flooring is allowed");
  Reset();Farm.objects[new(4,3)]=new SObject("390",9);Farm.objects[new(6,4)]=new SObject("390",7);Check(TerminalEntity.CanPlace(Farm,pos,Host),"A03 upper decoration and adjacent object do not expand ground footprint");
  for(int x=4;x<=5;x++)foreach(string blocker in new[]{"object","water","wall","warp","action","crop","restriction"})
  {
   Reset();var t=new Vector2(x,4);var back=Farm.map.GetLayer("Back");var wall=Farm.map.GetLayer("Buildings");
   if(blocker=="object")Farm.objects[t]=new SObject("390",9);
   if(blocker=="water")back.Tiles[x,4].Properties["Water"]="T";
   if(blocker is "wall" or "action"){wall.Tiles[x,4]=new StaticTile(wall,Farm.map.TileSheets[0],BlendMode.Alpha,0);if(blocker=="action")wall.Tiles[x,4].Properties["Action"]="Warp FarmHouse 3 4";}
   if(blocker=="warp")Farm.warps.Add(new Warp(x,4,"Town",1,1,false));
   if(blocker=="crop"){var dirt=new HoeDirt();dirt.crop=new Crop();Farm.terrainFeatures[t]=dirt;}
   if(blocker=="restriction")back.Tiles[x,4].Properties["NoFurniture"]="total";
   Host.Items[0]=Item();int before=Farm.objects.Count();World.Network.Request(Farm,pos);
   Check(!TerminalEntity.CanPlace(Farm,pos,Host)&&Farm.objects.Count()==before&&Host.Items[0]?.Stack==1&&!Host.modData.ContainsKey(TerminalNetwork.PendingKey),"A04 rejects either bottom cell without escrow or reservations: "+blocker+" "+x);
  }
  Reset();Home.warps.Add(new Warp(1,1,"Farm",5,4,false));Check(!TerminalEntity.CanPlace(Farm,pos,Host),"A04 known incoming warp arrival cannot be covered");Home.warps.Clear();
  Reset();Put(Host,Farm,22,4);Check(!TerminalEntity.CanPlace(Farm,new(23,4),Host),"A04 second base cell beyond map boundary rejected");
  Reset();TerminalEntity.PlacementDistance=3;Put(Host,Farm,8,4);Check(TerminalEntity.CanPlace(Farm,pos,Host),"A05 range and clear path use nearest right footprint cell");Put(Host,Farm,4,8);Check(!TerminalEntity.CanPlace(Farm,pos,Host),"A05 sprite height cannot extend approved placement distance");
  // Existing anchor is not translated; native base coordinate is the migration authority.
  Reset();a=Place();string id=TerminalEntity.Id(a);Farm.objects[new(4,5)]=TerminalEntity.NewPart(Farm,new(4,5),a);Farm.objects[new(5,5)]=TerminalEntity.NewPart(Farm,new(5,5),a);
  a.modData["player-note"]="preserve";Farm.modData[OrderMarket.Key]="unchanged order bytes";var neighbor=new SObject("390",17);Farm.objects[new(6,5)]=neighbor;
  for(int i=0;i<5;i++)World.Repair(Farm);
  Check(a.TileLocation==pos&&TerminalEntity.Id(a)==id&&TerminalEntity.Complete(Farm,a)&&Farm.objects.Count()==3&&Farm.objects[new(6,5)]==neighbor&&Farm.modData[OrderMarket.Key]=="unchanged order bytes","A09 repeated four-cell migration removes only own extra reservations; no anchor shift or order mutation");
  var serializer=new XmlSerializer(typeof(SObject));var saved=Farm.objects.Pairs.Select(p=>{using var w=new StringWriter();serializer.Serialize(w,p.Value);return(p.Key,Raw:w.ToString());}).ToArray();
  Farm.objects.Clear();foreach(var p in saved){using var r=new StringReader(p.Raw);Farm.objects[p.Key]=(SObject)serializer.Deserialize(r)!;}World.Repair(Farm);a=Farm.objects[pos];
  Check(TerminalEntity.Id(a)==id&&TerminalEntity.Complete(Farm,a)&&a.modData["player-note"]=="preserve","A09 native XML roundtrip keeps two-cell identity and metadata");
  Host.Items[0]=new Pickaxe();string packed="";World.Pickup(Farm,new(5,4),Host,id,raw=>packed=raw);
  Check(TerminalItem.Read(packed).Stack==1&&Farm.objects.Count()==1&&!TerminalHit(pos)&&!TerminalHit(new(5,4)),"A08 pickup through right base returns one item and clears both colliders");
  Reset();a=Item();a.TileLocation=pos;Farm.objects[pos]=a;Farm.objects[new(5,4)]=new SObject("390",31);World.Repair(Farm);id=TerminalEntity.Id(a);World.Repair(Farm);
  Check(TerminalEntity.Pending(a)&&Farm.objects[new(5,4)].Stack==31&&a.TileLocation==pos,"A09 blocked one-cell expansion preserves old anchor and neighbor");
  Host.Items[0]=new Pickaxe();World.Pickup(Farm,pos,Host,id,raw=>packed=raw);Check(Farm.objects.Count()==1&&TerminalItem.Read(packed).Stack==1,"A09 illegal old base remains safely recoverable");
  for(int fault=0;fault<3;fault++)
  {
   Reset();AtomicFault=fault;bool threw=false;try{World.Place(Farm,pos,Host,Guid.NewGuid().ToString("N"),Item(),()=>Host.modData[TerminalNetwork.ReceiptKey(Host.UniqueMultiplayerID)]="receipt");}catch(InvalidOperationException){threw=true;}
   Check(threw&&Farm.objects.Count()==0&&!Host.modData.ContainsKey(TerminalNetwork.ReceiptKey(Host.UniqueMultiplayerID)),"A10 failure after each actual placement write rolls back both cells and receipt "+fault);
  }
  Reset();Put(Guest,Farm,3,4);Put(Host,Farm,3,4);var hr=TerminalIntent(Host,"place",pos);var gr=TerminalIntent(Guest,"place",pos);TerminalPending(Host,hr);TerminalPending(Guest,gr);World.Network.Process(Host);World.Network.Process(Guest);World.Network.Process(Host);
  World.Network.Receive(Host);Put(Guest,Farm,3,4);World.Network.Receive(Guest);World.Network.Receive(Guest);
  Check(Farm.objects.Count()==2&&Guest.Items.Count(OrderTerminal.IsTerminal)==1&&Host.Items.All(i=>!OrderTerminal.IsTerminal(i)),"A10 same-point competing intents create one two-cell entity and refund loser once; simulated peers");
  a=Farm.objects[pos];Host.Items[0]=new Pickaxe();Guest.Items[1]=Guest.Items[0];Guest.Items[0]=new Pickaxe();hr=TerminalIntent(Host,"pickup",pos,TerminalEntity.Id(a),2);gr=TerminalIntent(Guest,"pickup",pos,TerminalEntity.Id(a),2);TerminalPending(Host,hr);TerminalPending(Guest,gr);
  Put(Host,Farm,3,4);World.Network.Process(Host);World.Network.Process(Guest);World.Network.Receive(Host);Put(Guest,Farm,3,4);World.Network.Receive(Guest);World.Network.Receive(Guest);
  Check(Farm.objects.Count()==0&&Host.Items.Concat(Guest.Items).Count(OrderTerminal.IsTerminal)==2,"A10 duplicate/racing pickups return one terminal and remove every own part; simulated peers");
  // Record the actual SpriteBatch commands. This verifies transforms, not GPU pixels.
  h.Patch(typeof(SpriteBatch).GetMethod("Draw",new[]{typeof(Texture2D),typeof(Vector2),typeof(Rectangle?),typeof(Color),typeof(float),typeof(Vector2),typeof(float),typeof(SpriteEffects),typeof(float)})!,prefix:new(typeof(Program),nameof(CaptureTerminal)));
  h.Patch(typeof(SpriteBatch).GetMethod("Draw",new[]{typeof(Texture2D),typeof(Vector2),typeof(Rectangle?),typeof(Color),typeof(float),typeof(Vector2),typeof(Vector2),typeof(SpriteEffects),typeof(float)})!,prefix:new(typeof(Program),nameof(CaptureTerminalVector)));
  h.Patch(typeof(SpriteBatch).GetMethod("Draw",new[]{typeof(Texture2D),typeof(Rectangle),typeof(Color)})!,prefix:new(typeof(Program),nameof(CaptureTerminalCell)));
  Recorder.Textures["assets/orders/terminal-world.png"]=Texture(64,128);Recorder.Textures["assets/orders/terminal-icon.png"]=Texture(64,64);Game1.staminaRect=Texture(1,1);
  var batch=(SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch));GC.SuppressFinalize(batch);
  foreach(bool controller in new[]{false,true})foreach(var camera in new[]{new Point(0,0),new Point(87,112)})
  {
   Reset();Game1.options.gamepadControls=controller;Game1.viewport=new(camera.X,camera.Y,1280,720);Recorder.CursorTile=pos;var item=Item();Host.Items[0]=item;
   TerminalDraws.Clear();TerminalCells.Clear();item.drawPlacementBounds(batch,Farm);var preview=TerminalDraws.Single();
   Check(Farm.objects.Count()==0&&TerminalCells.SequenceEqual(new[]{new Rectangle(256-camera.X,256-camera.Y,64,64),new Rectangle(320-camera.X,256-camera.Y,64,64)}),"A05/A08 actual preview draws two base cells without creating entities "+controller+camera);
   a=Place();TerminalDraws.Clear();foreach(var p in Farm.objects.Pairs)p.Value.draw(batch,(int)p.Key.X,(int)p.Key.Y,1);
   Check(TerminalDraws.Single()==preview&&preview.Position==Game1.GlobalToLocal(Game1.viewport,TerminalEntity.DrawPosition(pos))&&preview.Source==new Rectangle(6,66,54,59)&&Math.Abs(preview.Scale*59-128)<.001&&Math.Abs(preview.Depth-319/10000f)<.00001,"A05/A06 identical mouse/controller preview and placement; uniform alpha-trim fit, base depth and viewport offset");
  }
  h.Patch(typeof(SButtonExtensions).GetMethod("IsActionButton",new[]{typeof(SButton)})!,prefix:new(typeof(Program),nameof(Yes)));
  h.Patch(typeof(SButtonExtensions).GetMethod("IsUseToolButton",new[]{typeof(SButton)})!,prefix:new(typeof(Program),nameof(No)));
  foreach(string name in new[]{"FarmHouse","Shed","Town"})
  {
   Reset();var map=new GameLocation();map.name.Value=name;map.map=Map();Put(Host,map,3,4);Host.Items[0]=Item();map.warps.Add(new Warp(4,4,"Farm",3,3,false));Recorder.CursorTile=pos;Game1.options.gamepadControls=true;
   var ctor=typeof(ButtonPressedEventArgs).GetConstructors(Any).Single();var args=ctor.GetParameters().Select(p=>p.ParameterType==typeof(SButton)?(object)SButton.ControllerA:p.ParameterType.IsInterface?Recorder.Make(p.ParameterType):p.HasDefaultValue?p.DefaultValue:p.ParameterType.Name=="SInputState"?FormatterServices.GetUninitializedObject(p.ParameterType):throw new Exception("input fixture "+p)).ToArray();
   int suppressed=Recorder.SuppressCalls;typeof(TerminalWorld).GetMethod("Button",Any)!.Invoke(World,new object?[]{null,ctor.Invoke(args)});
   Check(Recorder.SuppressCalls==suppressed&&map.objects.Count()==0&&Host.Items[0]?.Stack==1&&!Host.modData.ContainsKey(TerminalNetwork.PendingKey),"A07 held terminal at entry leaves native action unsuppressed and inventory unchanged: "+name);
  }
  Reset();Game1.options.gamepadControls=false;Game1.viewport=new(0,0,1280,720);AtomicFault=-1;
 }
}

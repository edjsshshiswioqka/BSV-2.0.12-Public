using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using BetterStardewValley.Orders;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Tools;
using SObject = StardewValley.Object;

partial class Program
{
    static bool Hit(int x, int y) => Farm.isCollidingPosition(new Rectangle(x * 64 + 8, y * 64 + 8, 48, 48), new(0,0,1536,1280), true,0,false,Host,false,false,false,true);
    static void SingleCellChecks()
    {
        var a = Place();
        Check(Farm.objects.Count()==1 && TerminalEntity.Tiles(a.TileLocation).Count()==1 && a.GetBoundingBox()==new Rectangle(256,256,64,64), "one native object and exact 1x1/64x64 collision");
        Check(Hit(4,4) && !Hit(5,4) && !Hit(4,5) && !Hit(5,5), "only anchor blocks native walking; three visual-only cells remain walkable");
        foreach(var tile in new[]{new Vector2(4,4),new Vector2(5,4),new Vector2(4,5),new Vector2(5,5)})
            Check(TerminalEntity.Resolve(Farm,tile,out var root)&&ReferenceEquals(root,a), "empty visible artwork remains mapped to one terminal " + tile);
        Game1.activeClickableMenu=null;a.checkForAction(Host);
        Check(Game1.activeClickableMenu is OrdersMenu, "single terminal opens existing Orders UI"); Game1.activeClickableMenu=null;
        var other = new SObject("390",7);Farm.objects[new(5,4)]=other;
        Check(!TerminalEntity.Resolve(Farm,new(5,4),out _), "real neighbor interaction takes priority over artwork hit area");
        Check(!other.canBePlacedHere(Farm,new(4,4)), "occupied anchor still rejects overlapping placement");
        Reset();Host.Items[0]=Item();World.Network.Request(Farm,new(4,4));World.Network.Tick();
        Check(Farm.objects.Count()==1&&Host.Items.Count(OrderTerminal.IsTerminal)==0,"placement consumes exactly one inventory item");
        a=Farm.objects[new(4,4)];Host.Items[0]=new Pickaxe();World.Network.Request(Farm,a.TileLocation,a);World.Network.Tick();
        Check(Farm.objects.Count()==0&&Host.Items.Count(OrderTerminal.IsTerminal)==1,"pickup returns one item with no phantom reservations");
        Host.CurrentToolIndex=Host.Items.Select((v,i)=>(v,i)).Single(v=>OrderTerminal.IsTerminal(v.v)).i;World.Network.Request(Farm,new(4,4));World.Network.Tick();
        Check(Farm.objects.Count()==1,"pickup/re-place keeps one physical object");

        Reset();a=Place();a.modData["player-note"]="kept";string id=TerminalEntity.Id(a);
        a.boundingBox.Value=new Rectangle(256,256,128,128);
        foreach(var t in new[]{new Vector2(5,4),new Vector2(4,5),new Vector2(5,5)})Farm.objects[t]=TerminalEntity.NewPart(Farm,t,a);
        Check(Farm.objects.Count()==4,"fixture recreates previous four-cell save structure");
        World.Repair(Farm);
        Check(Farm.objects.Count()==1&&ReferenceEquals(Farm.objects[new(4,4)],a)&&TerminalEntity.Id(a)==id&&a.modData["player-note"]=="kept","legacy migration keeps original item/identity/data and removes only three internal reservations");
        Check(a.GetBoundingBox()==new Rectangle(256,256,64,64)&&!Hit(5,5),"legacy collision shrinks to one cell without leaving an invisible wall");
        for(int i=0;i<5;i++)World.Repair(Farm);
        Check(Farm.objects.Count()==1,"repeated repair cannot duplicate terminal or drop items");
        var serializer=new XmlSerializer(typeof(SObject));string raw;
        using(var w=new StringWriter()){serializer.Serialize(w,a);raw=w.ToString();}
        Farm.objects.Clear();using(var reader=new StringReader(raw))Farm.objects[new(4,4)]=(SObject)serializer.Deserialize(reader)!;World.Repair(Farm);
        Check(Farm.objects.Count()==1&&Farm.objects[new(4,4)].GetBoundingBox()==new Rectangle(256,256,64,64),"native Object XML reload retains one-cell geometry");
        Farm.objects[new(5,4)]=new SObject("390",19);World.Repair(Farm);
        Check(Farm.objects[new(5,4)].Stack==19&&Farm.objects.Count()==2,"neighboring real objects are never removed during shrink migration");
        Reset();Host.Items[0]=Item();Farm.objects[new(4,4)]=new SObject("390",1);World.Network.Request(Farm,new(4,4));
        Check(Host.Items[0].Stack==1&&!Host.modData.ContainsKey(TerminalNetwork.PendingKey),"failed placement does not consume inventory");
        Reset();Farm.objects[new(5,4)]=new SObject("390",1);Farm.map.GetLayer("Back").Tiles[5,5].Properties["Water"]="T";
        Check(TerminalEntity.CanPlace(Farm,new(4,4),Host),"visual-only neighbors are not counted as physical placement tiles");
        Farm.map.GetLayer("Back").Tiles[4,4].Properties["Water"]="T";
        Check(!TerminalEntity.CanPlace(Farm,new(4,4),Host),"water at actual anchor still rejects placement");
        Reset();Put(Host,Home,3,4);Check(TerminalEntity.CanPlace(Home,new(4,4),Host),"own farmhouse remains allowed");
        var town=new GameLocation();town.name.Value="Town";town.map=Map();Put(Host,town,3,4);
        Check(!TerminalEntity.CanPlace(town,new(4,4),Host),"nonfarm map restriction remains enforced");
        Reset();a=Place();
        var assembly=Assembly.LoadFrom(System.IO.Path.Combine(AppContext.BaseDirectory,"BetterStardewValley.dll"));
        var world=assembly.GetType("BetterStardewValley.AfkFishing.AfkWorld",true)!;
        foreach(var t in new[]{new Point(4,4),new Point(5,4),new Point(4,5),new Point(5,5)})
            Check((bool)world.GetMethod("StaticClear",Any)!.Invoke(null,new object[]{Farm,t,Host,true})! == (t!=new Point(4,4)),"production AFK collision follows one cell "+t);
    }
    record DrawCall(Vector2 Position,Rectangle? Source,float Scale);
    static readonly List<DrawCall> Draws=new();static readonly List<Rectangle> Cells=new();
    static bool RecordDraw(Texture2D texture,Vector2 position,Rectangle? sourceRectangle,Color color,float rotation,Vector2 origin,float scale,SpriteEffects effects,float layerDepth)
    {Draws.Add(new(position,sourceRectangle,scale));return false;}
    static bool RecordCell(Texture2D texture,Rectangle destinationRectangle,Color color){Cells.Add(destinationRectangle);return false;}
    static void DrawingChecks(Harmony h)
    {
        Reset();var a=Place();Recorder.Textures["assets/orders/terminal-world.png"]=Texture(64,128);Recorder.Textures["assets/orders/terminal-icon.png"]=Texture(64,64);Game1.staminaRect=Texture(1,1);
        h.Patch(typeof(SpriteBatch).GetMethod("Draw",new[]{typeof(Texture2D),typeof(Vector2),typeof(Rectangle?),typeof(Color),typeof(float),typeof(Vector2),typeof(float),typeof(SpriteEffects),typeof(float)})!,prefix:new(typeof(Program),nameof(RecordDraw)));
        h.Patch(typeof(SpriteBatch).GetMethod("Draw",new[]{typeof(Texture2D),typeof(Rectangle),typeof(Color)})!,prefix:new(typeof(Program),nameof(RecordCell)));
        var batch=(SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch));GC.SuppressFinalize(batch);Game1.viewport=new(0,0,1536,1280);
        Draws.Clear();a.draw(batch,4,4,1);
        Check(Draws.Count==1&&Draws[0].Source==TerminalEntity.WorldSource&&Draws[0].Scale==2&&Draws[0].Position==new Vector2(256,256),"world sprite remains unchanged 2x2 with original source and anchor");
        Host.Items[0]=Item();var item=Host.ActiveObject;object?[] state={null};typeof(OrderTerminal).GetMethod("BeginCursor",Any)!.Invoke(null,state);
        try{Draws.Clear();Cells.Clear();item.drawPlacementBounds(batch,Farm);((Item)item).drawInMenu(batch,new Vector2(280,260),1,.5f,.999f);
            Check(Cells.SequenceEqual(new[]{new Rectangle(256,256,64,64)})&&Draws.Count==1&&Draws[0].Scale==2,"preview highlights one actual cell, displays 2x2 artwork once and suppresses duplicate cursor icon");}
        finally{typeof(OrderTerminal).GetMethod("EndCursor",Any)!.Invoke(null,state);}
        Draws.Clear();item.drawInMenu(batch,Vector2.Zero,1,1,1,StackDrawType.Hide,Color.White,false);
        Check(Draws.Count==1&&Draws[0].Scale==1,"inventory icon retains normal size and visibility");
    }
}

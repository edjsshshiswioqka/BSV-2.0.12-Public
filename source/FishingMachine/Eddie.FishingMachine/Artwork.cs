using Eddie.FishingMachine.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Objects;
using System.Runtime.CompilerServices;
namespace Eddie.FishingMachine;

internal sealed class Artwork
{
    private readonly ModEntry Mod;
    private Texture2D? Body,Icon,GoldBody,GoldIcon,PurpleBody,PurpleIcon,Bait,Float,Ripple,Ready;
    private MachineLayout Layout=new();
    private CrabArtwork CrabArt=null!;
    private Texture2D? CraftIcon;
    /// <summary>Crafting-page cell (32x64): the inventory icon standing at the bottom, like other big craftables.</summary>
    internal Texture2D CraftTextureFor(bool crab)=>(crab?CrabArt.CraftTexture:CraftIcon) ?? throw new InvalidOperationException("Machine art is not loaded.");
    private sealed record LandingCache(GameLocation Location,Vector2 Anchor,long Minute,PixelPoint[] Points);
    private ConditionalWeakTable<Chest,LandingCache> Landings=new();
    internal Texture2D MenuTexture => Icon ?? throw new InvalidOperationException("Machine art is not loaded.");
    internal Artwork(ModEntry mod)=>Mod=mod;
    internal void Reset()=>Landings=new();
    internal void Load()
    {
        var layout=Mod.Helper.Data.ReadJsonFile<MachineLayout>("assets/layout.json")
            ?? throw new InvalidOperationException("Missing six-rod art layout.");
        layout.Validate();
        CrabArt=new(Mod);CrabArt.Load();
        var created=new List<Texture2D>();
        Texture2D Make(int width,int height,uint[] pixels)
        {
            var texture=new Texture2D(Game1.graphics.GraphicsDevice,width,height);
            created.Add(texture);texture.SetData(pixels);return texture;
        }
        Texture2D LoadNative(string file,int width,int height)
        {
            var source=Mod.Helper.ModContent.Load<Texture2D>("assets/"+file);
            if(source.Width!=width || source.Height!=height)throw new InvalidOperationException("Unexpected native art size: "+file);
            var pixels=new uint[width*height];source.GetData(pixels);
            return Make(width,height,PixelArt.Bake(pixels,width,height,0,0,width,height,width,height,true));
        }
        Texture2D Gold(Texture2D source)
        {
            // Runtime level palette; leave the original asset and transparency untouched.
            var pixels=new Color[source.Width*source.Height];source.GetData(pixels);
            var packed=new uint[pixels.Length];
            for(int i=0;i<pixels.Length;i++)
            {
                var c=pixels[i];
                float light=(c.R*.30f+c.G*.59f+c.B*.11f)/255f;
                var gold=new Color((byte)Math.Clamp(22+light*268,0,255),(byte)Math.Clamp(9+light*220,0,255),(byte)Math.Clamp(2+light*78,0,255),c.A);
                if(c.A==0)gold=Color.Transparent;
                packed[i]=gold.PackedValue;
            }
            return Make(source.Width,source.Height,packed);
        }
        Texture2D Purple(Texture2D source)
        {
            // Sample the installed game's iridium ore, including any texture pack palette.
            var ore=ItemRegistry.GetDataOrErrorItem("(O)386");var rect=ore.GetSourceRect();
            var orePixels=new Color[rect.Width*rect.Height];
            ore.GetTexture().GetData(0,rect,orePixels,0,orePixels.Length);
            var palette=orePixels.Where(c=>c.A>200 && c.B>c.G*1.15 && c.R>c.G*1.1)
                .Distinct().OrderBy(c=>c.R*.30+c.G*.59+c.B*.11).ToArray();
            if(palette.Length<2)palette=new[]{new Color(49,28,78),new Color(89,49,133),new Color(145,80,194),new Color(201,133,231),new Color(235,190,250)};
            var pixels=new Color[source.Width*source.Height];source.GetData(pixels);
            var packed=new uint[pixels.Length];
            for(int i=0;i<pixels.Length;i++)
            {
                var c=pixels[i];float light=(c.R*.30f+c.G*.59f+c.B*.11f)/255f;
                var purple=palette[Math.Min(palette.Length-1,(int)(light*palette.Length))];purple.A=c.A;
                packed[i]=c.A==0?Color.Transparent.PackedValue:purple.PackedValue;
            }
            return Make(source.Width,source.Height,packed);
        }
        Texture2D Glyph(PixelGlyph g)=>Make(g.Width,g.Height,g.Pixels);
        try
        {
            var body=LoadNative(layout.File,layout.Width,layout.Height);var icon=LoadNative(layout.IconFile,32,16);
            var goldBody=Gold(body);var goldIcon=Gold(icon);var purpleBody=Purple(body);var purpleIcon=Purple(icon);
            var bait=Glyph(MachineGlyphs.Bait);var bobber=Glyph(MachineGlyphs.Float);
            var ripple=Glyph(MachineGlyphs.Ripple);var ready=Glyph(MachineGlyphs.Ready);
            foreach(var old in new[]{Body,Icon,GoldBody,GoldIcon,PurpleBody,PurpleIcon,Bait,Float,Ripple,Ready})
                if(old!=null){BetterStardewValley.HdSprites.Unregister(old);old.Dispose();}
            (Body,Icon,GoldBody,GoldIcon,PurpleBody,PurpleIcon,Bait,Float,Ripple,Ready,Layout)=(body,icon,goldBody,goldIcon,purpleBody,purpleIcon,bait,bobber,ripple,ready,layout);
            CraftIcon?.Dispose();CraftIcon=CrabArtwork.CraftCell(icon);
            Reset();
        }
        catch{foreach(var texture in created)texture.Dispose();throw;}
    }
    internal int BobOffset(Vector2 anchor)=>FloatMotion.Offset(Game1.currentGameTime.TotalGameTime.TotalSeconds,
        new((int)anchor.X,(int)anchor.Y),Mod.Config.Visual);
    private PixelPoint[] WaterLandings(Chest c,Vector2 topLeft)
    {
        if(Landings.TryGetValue(c,out var cache) && cache.Location==c.Location && cache.Anchor==c.TileLocation
            && cache.Minute==ProductionService.Now)return cache.Points;
        var anchor=new GridTile((int)c.TileLocation.X,(int)c.TileLocation.Y);
        var points=Layout.Rods.Select((r,i)=>
        {
            var desired=new PixelPoint(topLeft.X+r.Landing.X*4,topLeft.Y+r.Landing.Y*4);
            var wet=VisualLanding.Resolve(desired,anchor,i,p=>WorldService.Water(c.Location,p));
            return new PixelPoint(wet.X-topLeft.X,wet.Y-topLeft.Y);
        }).ToArray();
        Landings.Remove(c);Landings.Add(c,new(c.Location,c.TileLocation,ProductionService.Now,points));return points;
    }
    internal void Draw(SpriteBatch b,Vector2 anchor,string phase,bool storedBait,float alpha,int bob=0,Chest? chest=null)
    {
        if(ModEntry.IsCrab(chest ?? Game1.player.ActiveObject))
        {
            CrabArt.Draw(b,anchor,alpha,chest);
            // Same ready glyph/state as Fishing Machine. Preview never advertises output.
            if(chest!=null && VisualState.For(phase,storedBait).Ready && Ready!=null)
            {
                var p=SpriteGeometry.World(new((int)anchor.X,(int)anchor.Y),CrabArtwork.OriginX,CrabArtwork.OriginY);
                var readyScreen=Game1.GlobalToLocal(Game1.viewport,new Vector2((float)p.X+120,(float)p.Y+20));
                DrawTexture(b,Ready,new Vector2(MathF.Round(readyScreen.X),MathF.Round(readyScreen.Y)),4,alpha,
                    Math.Clamp((anchor.Y+2)*64/10000f,.0005f,.998f)+.0002f);
            }
            return;
        }
        if(Body==null)return;
        var visual=VisualState.For(phase,storedBait);
        var origin=SpriteGeometry.World(new((int)anchor.X,(int)anchor.Y),Layout.OriginX,Layout.OriginY);
        var world=new Vector2((float)origin.X,(float)origin.Y);
        var screen=Game1.GlobalToLocal(Game1.viewport,world);
        screen=new(MathF.Round(screen.X),MathF.Round(screen.Y));
        float depth=Math.Clamp((anchor.Y+2)*64/10000f,.0005f,.998f);
        var grid=new GridTile((int)anchor.X,(int)anchor.Y);
        if(visual.Lines)
        {
            var landings=chest!=null?WaterLandings(chest,world):null;
            Span<PixelPoint> pose=stackalloc PixelPoint[LineMotion.Segments+1];
            for(int i=0;i<Layout.Rods.Length;i++)
            {
                var r=Layout.Rods[i];var end=landings?[i]??new(r.Landing.X*4,r.Landing.Y*4);
                LineMotion.Pose(new(r.Tip.X*4,r.Tip.Y*4),end,Game1.currentGameTime.TotalGameTime.TotalSeconds,grid,i,bob,Mod.Config.Visual,pose);
                for(int s=1;s<pose.Length;s++)
                {
                    var p=screen+new Vector2((float)Math.Round(pose[s-1].X),(float)Math.Round(pose[s-1].Y));
                    var q=screen+new Vector2((float)Math.Round(pose[s].X),(float)Math.Round(pose[s].Y));
                    var d=q-p;
                    b.Draw(Game1.staminaRect,p,null,new Color(235,224,198)*alpha,MathF.Atan2(d.Y,d.X),Vector2.Zero,
                        new Vector2(Math.Max(1,d.Length()),1),SpriteEffects.None,depth-.0003f);
                }
                var point=screen+new Vector2((float)Math.Round(pose[^1].X),(float)Math.Round(pose[^1].Y));
                DrawTexture(b,Ripple!,point+new Vector2(-7,0),2,alpha*.7f,depth-.0004f);
                DrawTexture(b,Float!,point+new Vector2(-3,-6),2,alpha,depth-.0002f);
            }
        }
        DrawTexture(b,(Mod.Production.Level(chest ?? Game1.player.ActiveObject) switch {3=>PurpleBody,2=>GoldBody,_=>Body})!,screen+new Vector2(0,bob),4,alpha,depth);
        if(visual.Bait)DrawTexture(b,Bait!,screen+new Vector2((float)Layout.BaitOrigin.X*4,(float)Layout.BaitOrigin.Y*4+bob),4,alpha,depth+.0001f);
        if(visual.Ready)DrawTexture(b,Ready!,screen+new Vector2((float)Layout.ReadyOrigin.X*4,(float)Layout.ReadyOrigin.Y*4+bob),4,alpha,depth+.0002f);
    }
    private static void DrawTexture(SpriteBatch b,Texture2D texture,Vector2 position,float zoom,float alpha,float depth)
        =>b.Draw(texture,position,null,Color.White*alpha,0,Vector2.Zero,zoom,SpriteEffects.None,depth);
    internal void DrawIcon(SpriteBatch b,Vector2 p,float scale,float alpha,float depth,int level=1,bool crab=false)
    {
        if(crab){CrabArt.DrawIcon(b,p,scale,alpha,depth,level);return;}
        if(Icon==null)return;
        float zoom=2*scale;
        var offset=SpriteGeometry.MenuOffset(Icon.Width,Icon.Height,scale);
        var position=p+new Vector2((float)offset.X,(float)offset.Y);
        DrawTexture(b,(level switch {3=>PurpleIcon,2=>GoldIcon,_=>Icon})!,position,zoom,alpha,depth);
    }
    internal void DrawHeld(SpriteBatch b,Vector2 p,Farmer farmer,int level=1,bool crab=false)
    {
        float depth=Math.Clamp((farmer.StandingPixel.Y+3)/10000f,0,1);
        if(crab){CrabArt.DrawHeld(b,p,depth,level);return;}
        if(Body==null)return;
        // Exactly the placed sprite (same art and level colour), at half the placed size, standing on the farmer's head.
        var body=(level switch {3=>PurpleBody,2=>GoldBody,_=>Body})!;
        b.Draw(body,HeldOrigin(p,body.Width,body.Height),null,Color.White,0,Vector2.Zero,HeldZoom,SpriteEffects.None,depth);
    }
    internal const float HeldZoom=2;
    /// <summary>Bottom-centre of the art sits where the bottom-centre of a native held big craftable sits.</summary>
    internal static Vector2 HeldOrigin(Vector2 objectPosition,int width,int height)
        =>new(MathF.Round(objectPosition.X+32-width*HeldZoom/2),MathF.Round(objectPosition.Y+128-height*HeldZoom));
}

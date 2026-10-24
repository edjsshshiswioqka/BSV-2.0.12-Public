using Eddie.FishingMachine.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Objects;
namespace Eddie.FishingMachine;

internal sealed class CrabArtwork
{
    internal const int Width=72,Height=48,OriginX=36,OriginY=42;
    private readonly ModEntry Mod;
    private readonly Texture2D[] World=new Texture2D[2],Legs=new Texture2D[2],Icon=new Texture2D[2];
    internal Texture2D MenuTexture=>Icon[0];
    internal Texture2D? CraftTexture;
    internal static Texture2D CraftCell(Texture2D icon)
    {
        var input=new Color[icon.Width*icon.Height];icon.GetData(input);
        var cell=new Color[32*64];
        for(int y=0;y<icon.Height;y++)Array.Copy(input,y*icon.Width,cell,(64-icon.Height+y)*32,icon.Width);
        var texture=new Texture2D(Game1.graphics.GraphicsDevice,32,64);texture.SetData(cell);return texture;
    }
    internal CrabArtwork(ModEntry mod)=>Mod=mod;
    internal void Load()
    {
        Texture2D Read(string file,int w,int h)
        {var t=Mod.Helper.ModContent.Load<Texture2D>("assets/crab-catcher/"+file);if(t.Width!=w||t.Height!=h)throw new InvalidOperationException("Invalid crab sprite "+file);return t;}
        for(int n=0;n<2;n++)
        {int l=n+1;World[n]=Read($"world-lv{l}.png",Width*6,Height);Legs[n]=Read($"legs-lv{l}.png",Width,Height);Icon[n]=Read($"icon-lv{l}.png",32,16);}
        CraftTexture?.Dispose();CraftTexture=CraftCell(Icon[0]);
    }
    private static void DrawTexture(SpriteBatch b,Texture2D t,Vector2 p,Rectangle? source,float scale,float alpha,float depth)
        =>b.Draw(t,p,source,Color.White*alpha,0,Vector2.Zero,scale,SpriteEffects.None,depth);
    internal void DrawIcon(SpriteBatch b,Vector2 p,float scale,float alpha,float depth,int level)
    {float zoom=2*scale;var offset=SpriteGeometry.MenuOffset(32,16,scale);DrawTexture(b,Icon[Math.Clamp(level-1,0,1)],p+new Vector2((float)offset.X,(float)offset.Y),new Rectangle(0,0,32,16),zoom,alpha,depth);}
    internal void DrawHeld(SpriteBatch b,Vector2 objectPosition,float depth,int level)
    {
        // Exactly what stands in the world while idle (legs plus the first body frame), at half size.
        int n=Math.Clamp(level-1,0,1);var p=Artwork.HeldOrigin(objectPosition,Width,Height);
        DrawTexture(b,Legs[n],p,null,Artwork.HeldZoom,1,depth);
        DrawTexture(b,World[n],p,new Rectangle(0,0,Width,Height),Artwork.HeldZoom,1,depth+.00001f);
    }

    internal void Draw(SpriteBatch b,Vector2 anchor,float alpha,Chest? c)
    {
        int level=Math.Clamp(Mod.Production.Level(c??Game1.player.ActiveObject)-1,0,1),frame=0;
        bool working=c!=null&&Mod.Production.Phase(c)=="working";
        if(working)
        {
            var state=Mod.Production.Read(c!);
            double elapsed=ProductionService.Now-state.StartedAt+Math.Clamp(Game1.gameTimeInterval/7000d,0,1)*10;
            frame=Math.Clamp((int)(elapsed/1.6),0,5);
        }
        var origin=SpriteGeometry.World(new((int)anchor.X,(int)anchor.Y),OriginX,OriginY);
        var screen=Game1.GlobalToLocal(Game1.viewport,new Vector2((float)origin.X,(float)origin.Y));
        screen=new(MathF.Round(screen.X),MathF.Round(screen.Y));
        float depth=Math.Clamp((anchor.Y+2)*64/10000f,.0005f,.998f);
        int bob=frame==5&&Mod.Config.Visual.EnableBobbing?(int)Math.Round(Math.Sin(Game1.currentGameTime.TotalGameTime.TotalSeconds*1.5)):0;
        // Feet, collision, interaction and footprint never move with body/cable animation.
        DrawTexture(b,Legs[level],screen,null,4,alpha,depth);
        DrawTexture(b,World[level],screen+new Vector2(0,bob*4),new Rectangle(frame*Width,0,Width,Height),4,alpha,depth+.00001f);
        if(frame==5)
        {
            int[] columns={4,11,59,66},tops={10,11,11,10};
            for(int n=0;n<4;n++)for(int y=tops[n]+bob;y<44;y++)
            {
                double t=(y-tops[n]-bob)/(double)(44-tops[n]-bob);
                int sway=Mod.Config.Visual.EnableLineSway?(int)Math.Round(Math.Sin(Game1.currentGameTime.TotalGameTime.TotalSeconds*1.8+n)*Math.Sin(Math.PI*t)):0;
                int x=(int)screen.X+(columns[n]+sway)*4,sy=(int)screen.Y+y*4;
                b.Draw(Game1.staminaRect,new Rectangle(x,sy,4,4),null,new Color(41,78,104)*alpha,0,Vector2.Zero,SpriteEffects.None,depth-.00002f);
                b.Draw(Game1.staminaRect,new Rectangle(x+4,sy,4,4),null,new Color(188,174,148)*alpha,0,Vector2.Zero,SpriteEffects.None,depth-.00002f);
            }
        }
    }
}
internal sealed class CrabSettings : Eddie.FishingMachine.Core.CrabBalanceConfig {public bool DebugMode {get;set;}}

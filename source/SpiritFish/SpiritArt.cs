using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using SObject=StardewValley.Object;
namespace BetterStardewValley.SpiritFish;

// Original BSV fish/soup only. Flavored derivatives use the native renderer and runtime Roe mask.
internal static class SpiritArt
{
    private static IModHelper Helper=null!;
    private static readonly Dictionary<string,Texture2D> Cache=new();
    internal static void Install(Harmony h,IModHelper helper,IMonitor monitor)
    {
        Helper=helper;
        SpiritRoeArt.Install(h,helper,monitor);
        foreach(Type type in new[]{typeof(SObject),typeof(ColoredObject)})
        {
            h.Patch(DrawPatchTargets.Require(type,"drawInMenu",typeof(void),typeof(SpriteBatch),typeof(Vector2),typeof(float),typeof(float),typeof(float),typeof(StackDrawType),typeof(Color),typeof(bool)),prefix:new HarmonyMethod(typeof(SpiritArt),nameof(Menu)));
            h.Patch(DrawPatchTargets.Require(type,"drawWhenHeld",typeof(void),typeof(SpriteBatch),typeof(Vector2),typeof(Farmer)),prefix:new HarmonyMethod(typeof(SpiritArt),nameof(Held)));
        }
        helper.Events.Content.AssetsInvalidated+=(_,e)=>
        {
            if(e.NamesWithoutLocale.Any(n=>n.Name.StartsWith(SpiritRules.TextureRoot,StringComparison.Ordinal)))Clear();
        };
    }
    private static void Clear(){foreach(var t in Cache.Values)HdSprites.Unregister(t);foreach(var t in Cache.Values.Distinct())t.Dispose();Cache.Clear();}
    internal static string? Kind(Item item)
    {
        if(item.QualifiedItemId==SpiritRules.Fish)return "fish";
        if(item.QualifiedItemId==SpiritRules.Soup)return "soup";
        if(item is not SObject o||!SpiritRules.IsFish(o.preservedParentSheetIndex.Value))return null;
        return item.QualifiedItemId switch {"(O)SmokedFish"=>"smoked","(O)812"=>"roe","(O)447"=>"aged_roe",_=>null};
    }
    internal static Texture2D Texture(string name,int size=64)
    {
        if(name is not ("fish" or "soup"))throw new ArgumentException("Only original fish/soup assets are supported.");
        string key=name+"/"+size;
        if(Cache.TryGetValue(key,out var found))return found;
        var source=Helper.ModContent.Load<Texture2D>("assets/spirit/"+name+".png");
        var logical=HdSprites.CreateSourceResolution(source,out var full);
        if(size==16){Cache[key]=logical;Cache[name+"/source"]=full;return logical;}
        var display=new Texture2D(Game1.graphics.GraphicsDevice,size,size);display.SetData(new Color[size*size]);
        HdSprites.RegisterPrepared(display,full);HdSprites.Unregister(logical);logical.Dispose();
        Cache[key]=display;Cache[name+"/source64"]=full;return display;
    }

    // Historical offline --export-art fixture only; never called by a runtime rendering/asset path.
    internal static Color[] Import(Color[] input,int width,int height,int size)
    {
        int left=width,top=height,right=-1,bottom=-1;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(input[y*width+x].A>=200)
        {left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
        if(right<left)throw new InvalidOperationException("Empty Spirit sprite.");
        float fit=Math.Min((size-2f)/(right-left+1),(size-2f)/(bottom-top+1));
        int w=Math.Max(1,(int)Math.Round((right-left+1)*fit)),h=Math.Max(1,(int)Math.Round((bottom-top+1)*fit));
        var output=new Color[size*size];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            int sx=Math.Min(right,left+(int)((x+.5f)*(right-left+1)/w));int sy=Math.Min(bottom,top+(int)((y+.5f)*(bottom-top+1)/h));
            var c=input[sy*width+sx];if(c.A<200)continue;
            output[(y+(size-h)/2)*size+x+(size-w)/2]=c;
        }
        return output;
    }
    private static bool Menu(SObject __instance,SpriteBatch spriteBatch,Vector2 location,float scaleSize,float transparency,float layerDepth,StackDrawType drawStackNumber,[HarmonyArgument(6)]Color color,bool drawShadow)
    {
        string? kind=Kind(__instance);if(kind is not ("fish" or "soup"))return true;
        __instance.AdjustMenuDrawForRecipes(ref transparency,ref scaleSize);
        var tex=Texture(kind);
        spriteBatch.Draw(tex,location+new Vector2(32),null,color*transparency,0,new Vector2(32),scaleSize,SpriteEffects.None,layerDepth);
        __instance.DrawMenuIcons(spriteBatch,location,scaleSize,transparency,layerDepth,drawStackNumber,color);return false;
    }
    private static bool Held(SObject __instance,SpriteBatch spriteBatch,Vector2 objectPosition,Farmer f)
    {
        string? kind=Kind(__instance);if(kind is not ("fish" or "soup"))return true;
        spriteBatch.Draw(Texture(kind),objectPosition,null,Color.White,0,Vector2.Zero,1,SpriteEffects.None,Math.Max(0,(f.StandingPixel.Y+3)/10000f));return false;
    }
}

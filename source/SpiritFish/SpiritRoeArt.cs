using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Objects;

namespace BetterStardewValley.SpiritFish;

// Runtime derivatives of the currently loaded game atlas. No game textures are redistributed.
// The original renderer still owns positioning, base/container layer, stack, quality and opacity.
internal static class SpiritRoeArt
{
    private readonly record struct Key(Texture2D Source, Rectangle Mask, bool Aged);
    private static readonly Dictionary<Key,Texture2D> Cache = new();
    private static readonly HashSet<string> Failed = new();
    private static IMonitor Monitor = null!;
    private static readonly Color[] Fresh={new(65,230,230),new(90,155,255),new(190,110,250),new(245,140,225),new(125,245,195)};
    private static readonly Color[] Aged={new(95,115,235),new(165,90,240),new(235,110,205),new(70,190,225)};
    internal static bool Applies(Item item) => SpiritArt.Kind(item) is "roe" or "aged_roe";
    internal static void Install(Harmony h, IModHelper helper, IMonitor monitor)
    {
        Monitor=monitor;
        foreach (var target in new[] {
            DrawPatchTargets.Require(typeof(ColoredObject),"drawInMenu",typeof(void),typeof(SpriteBatch),typeof(Vector2),typeof(float),typeof(float),typeof(float),typeof(StackDrawType),typeof(Color),typeof(bool)),
            DrawPatchTargets.Require(typeof(ColoredObject),"drawWhenHeld",typeof(void),typeof(SpriteBatch),typeof(Vector2),typeof(Farmer)),
            DrawPatchTargets.Require(typeof(ColoredObject),"draw",typeof(void),typeof(SpriteBatch),typeof(int),typeof(int),typeof(float)) })
            h.Patch(target,transpiler:new HarmonyMethod(typeof(SpiritRoeArt),nameof(RenderIL)));
        helper.Events.Content.AssetsInvalidated += (_,_) => Clear();
        helper.Events.GameLoop.ReturnedToTitle += (_,_) => Clear();
    }
    private static void Clear()
    { foreach(var tex in Cache.Values)tex.Dispose();Cache.Clear();Failed.Clear(); }

    internal static Color Rainbow(Color original,int x,int y,int width,int height,bool aged)
    {
        if(original.A==0)return original;
        // Preserve alpha and the source's shading intensity. Hue varies spatially, never over time.
        Color[] palette=aged ? Aged : Fresh;
        float phase=(x/(float)Math.Max(1,width-1)*.75f+y/(float)Math.Max(1,height-1)*.25f)*(palette.Length-1);
        int index=Math.Min(palette.Length-2,(int)phase);
        var tint=Color.Lerp(palette[index],palette[index+1],phase-index);
        // Channel multiplication is the native mask operation; alpha is kept byte-for-byte.
        return new Color(original.R*tint.R/255,original.G*tint.G/255,original.B*tint.B/255,(int)original.A);
    }

    internal static Texture2D Atlas(ParsedItemData data,ColoredObject item)
    {
        var original=data.GetTexture();
        if(!Applies(item)||Failed.Contains(item.QualifiedItemId))return original;
        var mask=data.GetSourceRect(item.ColorSameIndexAsParentSheetIndex?0:1,item.ParentSheetIndex);
        var key=new Key(original,mask,item.QualifiedItemId=="(O)447");
        if(Cache.TryGetValue(key,out var texture))return texture;
        try
        {
            if(mask.Width<=0||mask.Height<=0||mask.X<0||mask.Y<0||mask.Right>original.Width||mask.Bottom>original.Height)
                throw new InvalidOperationException("Roe color mask is outside its loaded atlas.");
            var pixels=new Color[original.Width*original.Height];original.GetData(pixels);
            for(int y=0;y<mask.Height;y++)for(int x=0;x<mask.Width;x++)
            { int i=(mask.Y+y)*original.Width+mask.X+x;pixels[i]=Rainbow(pixels[i],x,y,mask.Width,mask.Height,key.Aged); }
            texture=new Texture2D(Game1.graphics.GraphicsDevice,original.Width,original.Height);
            try { texture.SetData(pixels); } catch {texture.Dispose();throw;}
            Cache.Add(key,texture);return texture;
        }
        catch(Exception e)
        { Failed.Add(item.QualifiedItemId);Monitor.Log("Spirit roe art uses native fallback: "+e,LogLevel.Warn);return original; }
    }
    internal static Color Tint(Netcode.NetColor original,ColoredObject item) => Applies(item)&&!Failed.Contains(item.QualifiedItemId)?Color.White:original.Value;

    internal static IEnumerable<CodeInstruction> RenderIL(IEnumerable<CodeInstruction> input,MethodBase __originalMethod)
    {
        var code=input.Select(i=>new CodeInstruction(i)).ToList();
        var getTexture=DrawPatchTargets.Require(typeof(ParsedItemData),"GetTexture",typeof(Texture2D));
        var field=typeof(ColoredObject).GetField("color")!;
        int textures=code.Count(i=>i.Calls(getTexture));
        int colors=code.Count(i=>i.opcode==OpCodes.Ldfld&&Equals(i.operand,field));
        (int textureCount,int colorCount)=__originalMethod.Name switch {"drawInMenu"=>(1,2),"drawWhenHeld"=>(2,2),"draw"=>(2,4),_=>throw new InvalidOperationException("Unexpected roe target")};
        if(textures!=textureCount||colors!=colorCount)throw new InvalidOperationException($"Spirit roe target mismatch {__originalMethod}: texture {textures}/{textureCount}, color {colors}/{colorCount}.");
        var output=new List<CodeInstruction>();
        for(int i=0;i<code.Count;i++)
        {
            var ins=code[i];
            if(ins.Calls(getTexture))
            {
                var load=new CodeInstruction(OpCodes.Ldarg_0);load.labels.AddRange(ins.labels);load.blocks.AddRange(ins.blocks);
                output.Add(load);output.Add(new CodeInstruction(OpCodes.Call,typeof(SpiritRoeArt).GetMethod(nameof(Atlas),BindingFlags.Static|BindingFlags.NonPublic)));continue;
            }
            output.Add(ins);
            if(ins.opcode==OpCodes.Ldfld&&Equals(ins.operand,field))
            {
                if(i+1>=code.Count||code[i+1].operand is not MethodInfo getter||getter.Name!="get_Value"||getter.ReturnType!=typeof(Color))
                    throw new InvalidOperationException("Spirit roe NetColor getter pattern mismatch.");
                var old=code[++i];var load=new CodeInstruction(OpCodes.Ldarg_0);load.labels.AddRange(old.labels);load.blocks.AddRange(old.blocks);
                output.Add(load);output.Add(new CodeInstruction(OpCodes.Call,typeof(SpiritRoeArt).GetMethod(nameof(Tint),BindingFlags.Static|BindingFlags.NonPublic)));
            }
        }
        return output;
    }
}

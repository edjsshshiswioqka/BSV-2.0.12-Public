using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace BetterStardewValley;

/// <summary>
/// Higher density BSV-owned textures with native logical dimensions. Only exact
/// registered texture instances are remapped; item data, IDs and native overlays
/// remain intact. Covers menu, held, debris, crafting and other mods' item draws.
/// </summary>
internal static class HdSprites
{
    private sealed record Entry(Texture2D Texture,float Factor)
    {
        internal bool Filter;
        internal Rectangle? VisibleBounds;
        internal readonly List<Texture2D> Mips=new();
    }
    private static readonly Dictionary<Texture2D,Entry> Replacements=new();
    private static readonly Dictionary<string,Texture2D> Prepared=new();
    private static IModHelper? Helper;
    private static IMonitor? Monitor;
    private static bool Enabled,Rebind;
    internal static bool Initialized => Enabled;
    private static readonly (string Id,string File)[] Items={
        // Grape Wine and Ruby Reserve (id BloodOfJesus, kept for saves) now use their own vanilla-style 16x16 sprites (no HD replacement).
        // A Cup of Green Tea and Tea Cake are hand-drawn 16x16; the Golden Apple is the game's apple recoloured at runtime.
        };
    private static readonly HashSet<Texture2D> ItemSources=new();
    internal static void Install(IModHelper helper,IMonitor monitor,string id)
    {
        Helper=helper;Monitor=monitor;
        string owner=id+".HdSprites";var harmony=new Harmony(owner);
        try
        {
            void Patch(string name,params Type[] signature)
            {
                var method=AccessTools.Method(typeof(SpriteBatch),"Draw",signature)
                    ??throw new MissingMethodException("SpriteBatch.Draw: "+name);
                harmony.Patch(method,prefix:new HarmonyMethod(typeof(HdSprites),name));
            }
            var t=typeof(Texture2D);var v=typeof(Vector2);var r=typeof(Rectangle);var rn=typeof(Rectangle?);
            var c=typeof(Color);var f=typeof(float);var e=typeof(SpriteEffects);
            Patch(nameof(VectorScalar),t,v,rn,c,f,v,f,e,f);
            Patch(nameof(VectorScale),t,v,rn,c,f,v,v,e,f);
            Patch(nameof(Destination),t,r,rn,c,f,v,e,f);
            Patch(nameof(VectorSource),t,v,rn,c);
            Patch(nameof(VectorSimple),t,v,c);
            Patch(nameof(RectSource),t,r,rn,c);
            Patch(nameof(RectSimple),t,r,c);
            Enabled=true;
            helper.Events.GameLoop.SaveLoaded+=(_,_)=>BindItems();
            helper.Events.GameLoop.DayStarted+=(_,_)=>BindItems();
            helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>ClearItems();
            helper.Events.Content.AssetsInvalidated+=(_,_)=>Rebind=true;
            helper.Events.GameLoop.UpdateTicked+=(_,_)=>{if(Rebind&&Context.IsWorldReady){Rebind=false;BindItems();}};
        }
        catch(Exception ex)
        {
            harmony.UnpatchAll(owner);Enabled=false;
            monitor.Log("BSV HD sprite adapter could not start; native icons remain available.\n"+ex,LogLevel.Error);
        }
    }
    internal static void RegisterPrepared(Texture2D original,Texture2D replacement)
    {
        if(!Enabled)return;
        float factor=(float)replacement.Width/original.Width;
        if(Math.Abs(factor-(float)replacement.Height/original.Height)>.001f)throw new ArgumentException("Non-uniform prepared sprite scale.");
        Unregister(original);Replacements[original]=new(replacement,factor){Filter=false};
    }
    /// <summary>A texture handed to SMAPI from AssetRequested.LoadFrom belongs to SMAPI: it may dispose it at any time (cache
    /// invalidation, a language change, a temporary content manager being unloaded). Each load therefore gets its own copy;
    /// the mod's cached original is never given out, so one disposal can't break an icon that is still drawn elsewhere.</summary>
    internal static Texture2D Handoff(Texture2D cached)
    {
        var data=new Color[checked(cached.Width*cached.Height)];cached.GetData(data);
        var copy=new Texture2D(Game1.graphics.GraphicsDevice,cached.Width,cached.Height);copy.SetData(data);
        if(Replacements.TryGetValue(cached,out var entry))Replacements[copy]=new(entry.Texture,entry.Factor){Filter=entry.Filter};
        return copy;
    }
    internal static void Unregister(Texture2D original)
    {
        if(Replacements.Remove(original,out var entry))foreach(var mip in entry.Mips)mip.Dispose();
    }
    // Keep every source texel. The native 16px texture is a logical handle only;
    // the registered replacement supplies the actual pixels for every Draw overload.
    // Never downsample/recolour the author's seafood art or dispose ModContent's source.
    internal static Texture2D CreateSourceResolution(Texture2D source,out Texture2D full)
    {
        if(!Enabled)throw new InvalidOperationException("Source-resolution artwork requires the BSV HD drawing adapter; no low-resolution substitute was generated.");
        var input=new Color[checked(source.Width*source.Height)];source.GetData(input);
        var pixels=PadSource(input,source.Width,source.Height,out int side);
        full=new Texture2D(Game1.graphics.GraphicsDevice,side,side);
        Texture2D? logical=null;
        try
        {
            full.SetData(pixels);
            logical=new Texture2D(Game1.graphics.GraphicsDevice,16,16);
            logical.SetData(new Color[256]);
            RegisterPrepared(logical,full);
            return logical;
        }
        catch{logical?.Dispose();full.Dispose();throw;}
    }
    internal static Color[] PadSource(Color[] input,int width,int height,out int side)
    {
        if(width<=0||height<=0||input.Length!=checked(width*height))throw new ArgumentException("Invalid source image dimensions.");
        // Whole-pixel centering and an integral 16px mapping, no resampling or alpha cutoff.
        side=checked((Math.Max(width,height)+129)/128*128);
        var output=new Color[checked(side*side)];
        int left=(side-width)/2,top=(side-height)/2;
        for(int y=0;y<height;y++)Array.Copy(input,y*width,output,(top+y)*side+left,width);
        return output;
    }
    private static void ClearItems(){foreach(var t in ItemSources)Unregister(t);ItemSources.Clear();}
    private static void BindItems()
    {
        if(!Enabled)return;
        ClearItems();
        foreach(var (id,file) in Items)
        {
            try
            {
                var data=ItemRegistry.GetDataOrErrorItem(id);
                if(data.IsErrorItem)continue;
                var texture=data.GetTexture();
                // Refuse another mod's atlas: this adapter owns standalone BSV icons only.
                if(texture.Width!=16||texture.Height!=16)continue;
                Register(texture,file,64,64);ItemSources.Add(texture);
            }
            catch(Exception ex){Monitor?.Log("BSV HD icon unavailable: "+id+" / "+ex.Message,LogLevel.Warn);}
        }
        foreach(var (id,path) in new[]{
            ("(BC)Eddie.BetterStardewValley_OrderTerminal","assets/orders/terminal-world.png"),
            ("(BC)Eddie.BetterStardewValley_GrowthTotem","assets/totems/growth-world.png"),
            ("(BC)Eddie.BetterStardewValley_HarvestTotem","assets/totems/harvest-world.png"),
            ("(BC)Eddie.BetterStardewValley_ProcessingTotem","assets/totems/processing-world.png"),
            ("(BC)Eddie.BetterStardewValley_ThunderstormTotem","assets/totems/thunderstorm-world.png")})
        {
            var data=ItemRegistry.GetData(id);if(data==null||data.IsErrorItem)continue;
            var native=data.GetTexture();if(native.Width!=16||native.Height!=32)continue;
            RegisterPrepared(native,Helper!.ModContent.Load<Texture2D>(path));ItemSources.Add(native);
        }
    }

    /// <summary>
    /// A native 16x32 big-craftable handle for a wide 32x16 machine icon. The handle itself holds a
    /// half-size copy (used only if the adapter is off); the adapter draws the icon at full detail,
    /// vertically centred in the cell, so other mods' item draws show the same icon as BSV's menus.
    /// </summary>
    internal static Texture2D WideIconHandle(Texture2D icon)
    {
        var input=new Color[icon.Width*icon.Height];icon.GetData(input);
        var small=new Color[16*32];var full=new Color[32*64];
        int top=(64-icon.Height)/2;
        for(int y=0;y<icon.Height;y++)for(int x=0;x<icon.Width;x++)
        {
            full[(top+y)*32+x]=input[y*icon.Width+x];
            if(x%2==0&&y%2==0&&x/2<16)small[(top/2+y/2)*16+x/2]=input[y*icon.Width+x];
        }
        var logical=new Texture2D(Game1.graphics.GraphicsDevice,16,32);logical.SetData(small);
        if(Enabled){var detail=new Texture2D(Game1.graphics.GraphicsDevice,32,64);detail.SetData(full);RegisterPrepared(logical,detail);}
        return logical;
    }
    internal static void Register(Texture2D original,string file,int width,int height,int level=1)
    {
        if(!Enabled||Helper==null)return;
        try
        {
            string key=$"{file}/{width}/{height}/{level}";
            if(!Prepared.TryGetValue(key,out var texture))
            {
                var source=Helper.ModContent.Load<Texture2D>("assets/hd/"+file+".png");
                var input=new Color[source.Width*source.Height];source.GetData(input);
                // Preserve every source texel and alpha value. Fit by transparent padding only.
                int factor=Math.Max(8,((int)Math.Ceiling(Math.Max((source.Width+2d)/original.Width,(source.Height+2d)/original.Height))+7)/8*8);
                int w=original.Width*factor,h=original.Height*factor;
                var pixels=new Color[checked(w*h)];
                int left=(w-source.Width)/2,top=(h-source.Height)/2;
                for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++)
                {
                    var color=input[y*source.Width+x];
                    // Retain the established level palette, without destroying translucent edges.
                    if(level>1 && color.R>color.B*1.2f && color.R>color.G*1.03f)
                    {
                        float light=(color.R*.30f+color.G*.59f+color.B*.11f)/255f;
                        color=level==2?new Color((byte)Math.Clamp(22+light*268,0,255),(byte)Math.Clamp(9+light*220,0,255),(byte)Math.Clamp(2+light*78,0,255),color.A)
                            :new Color((byte)(49+light*186),(byte)(28+light*162),(byte)(78+light*172),color.A);
                    }
                    pixels[(y+top)*w+x+left]=color;
                }
                texture=new Texture2D(Game1.graphics.GraphicsDevice,w,h);texture.SetData(pixels);Prepared[key]=texture;
            }
            RegisterPrepared(original,texture);
        }
        catch(Exception ex){Monitor?.Log("BSV HD import failed: "+file+" / "+ex.Message,LogLevel.Warn);}
    }
    // Dedicated held-machine mapping. Inventory sizing must not be reused for a
    // two-cell machine above the farmer. Sample the source without modifying its bytes.
    internal static void DrawHeldMachine(SpriteBatch batch, Texture2D logical, Vector2 position, float depth)
    {
        if (!Replacements.TryGetValue(logical, out var entry)) return;
        var texture = entry.Texture;
        // Cache visible bounds per prepared entry; never scan during subsequent frames.
        if (entry.VisibleBounds == null)
        {
            var pixels = new Color[texture.Width * texture.Height];
            texture.GetData(pixels);
            entry.VisibleBounds = Totems.TotemArt.OpaqueBounds(pixels, texture.Width, texture.Height);
        }
        var bounds = entry.VisibleBounds.Value;
        float scale = Math.Min(128f / bounds.Width, 128f / bounds.Height);
        batch.Draw(texture, position + new Vector2(32, 128), bounds, Color.White, 0,
            new Vector2(bounds.Width / 2f, bounds.Height), scale, SpriteEffects.None, depth);
    }

    // Area averaging at runtime prevents point-sampling a 1500px illustration into 32–64px.
    // Derived levels are private to exact BSV texture instances; SpriteBatch state is untouched.
    internal static Color[] Downsample(Color[] source,int width,int height,out int w,out int h)
    {
        w=Math.Max(1,(width+1)/2);h=Math.Max(1,(height+1)/2);var output=new Color[w*h];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            int r=0,g=0,b=0,a=0,n=0;
            for(int yy=y*2;yy<Math.Min(height,y*2+2);yy++)for(int xx=x*2;xx<Math.Min(width,x*2+2);xx++)
            {var p=source[yy*width+xx];r+=p.R;g+=p.G;b+=p.B;a+=p.A;n++;}
            output[y*w+x]=new Color((r+n/2)/n,(g+n/2)/n,(b+n/2)/n,(a+n/2)/n);
        }
        return output;
    }
    private static bool Remap(ref Texture2D texture,ref Rectangle? source,out float factor,float desired=0)
    {
        factor=1;if(!Replacements.TryGetValue(texture,out var entry))return false;
        var r=source??new Rectangle(0,0,texture.Width,texture.Height);
        var full=entry.Texture;var selected=full;factor=entry.Factor;
        if(entry.Filter&&desired>0)
        {
            int level=0;
            while(factor/2>=desired&&selected.Width%2==0&&selected.Height%2==0&&Math.Min(selected.Width,selected.Height)>2)
            {
                if(entry.Mips.Count<=level)
                {
                    var data=new Color[selected.Width*selected.Height];selected.GetData(data);
                    var reduced=Downsample(data,selected.Width,selected.Height,out int w,out int h);
                    var mip=new Texture2D(Game1.graphics.GraphicsDevice,w,h);mip.SetData(reduced);entry.Mips.Add(mip);
                }
                selected=entry.Mips[level++];factor/=2;
            }
        }
        source=new Rectangle((int)(r.X*factor),(int)(r.Y*factor),(int)(r.Width*factor),(int)(r.Height*factor));
        texture=selected;return true;
    }
    private static void VectorScalar(ref Texture2D __0,ref Rectangle? __2,ref Vector2 __5,ref float __6)
    {if(Remap(ref __0,ref __2,out float factor,Math.Abs(__6))){__5*=factor;__6/=factor;}}
    private static void VectorScale(ref Texture2D __0,ref Rectangle? __2,ref Vector2 __5,ref Vector2 __6)
    {if(Remap(ref __0,ref __2,out float factor,Math.Max(Math.Abs(__6.X),Math.Abs(__6.Y)))){__5*=factor;__6/=factor;}}
    private static void Destination(ref Texture2D __0,Rectangle __1,ref Rectangle? __2,ref Vector2 __5)
    {
        var rect=__2??new Rectangle(0,0,__0.Width,__0.Height);
        if(Remap(ref __0,ref __2,out float factor,Math.Max(Math.Abs(__1.Width/(float)rect.Width),Math.Abs(__1.Height/(float)rect.Height))))__5*=factor;
    }
    private static void RectSource(ref Texture2D __0,Rectangle __1,ref Rectangle? __2)
    {var r=__2??new Rectangle(0,0,__0.Width,__0.Height);Remap(ref __0,ref __2,out _,Math.Max(Math.Abs(__1.Width/(float)r.Width),Math.Abs(__1.Height/(float)r.Height)));}
    private static bool VectorSource(SpriteBatch __instance,Texture2D __0,Vector2 __1,Rectangle? __2,Color __3)
    {
        if(!Remap(ref __0,ref __2,out float factor,1))return true;
        __instance.Draw(__0,__1,__2,__3,0,Vector2.Zero,1/factor,SpriteEffects.None,0);return false;
    }
    private static bool VectorSimple(SpriteBatch __instance,Texture2D __0,Vector2 __1,Color __2)
        =>VectorSource(__instance,__0,__1,null,__2);
    private static bool RectSimple(SpriteBatch __instance,Texture2D __0,Rectangle __1,Color __2)
    {
        Rectangle? source=null;float desired=Math.Max(Math.Abs(__1.Width/(float)__0.Width),Math.Abs(__1.Height/(float)__0.Height));
        if(!Remap(ref __0,ref source,out _,desired))return true;
        __instance.Draw(__0,__1,source,__2);return false;
    }
}

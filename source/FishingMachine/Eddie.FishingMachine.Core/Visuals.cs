namespace Eddie.FishingMachine.Core;

public sealed class VisualOptions
{
    public bool EnableBobbing { get; set; } = true;
    public double BobbingAmplitude { get; set; } = 2;
    public double BobbingPeriodSeconds { get; set; } = 4;
    public bool EnableLineSway { get; set; } = true;
    public double LineSwayAmplitude { get; set; } = 1.5;
    public double LineSwayPeriodSeconds { get; set; } = 3.2;
    public void Validate()
    {
        if(!double.IsFinite(BobbingAmplitude) || BobbingAmplitude<0 || BobbingAmplitude>4)
            throw new ArgumentException("BobbingAmplitude must be between 0 and 4 world pixels.");
        if(!double.IsFinite(BobbingPeriodSeconds) || BobbingPeriodSeconds<2 || BobbingPeriodSeconds>12)
            throw new ArgumentException("BobbingPeriodSeconds must be between 2 and 12.");
        if(!double.IsFinite(LineSwayAmplitude) || LineSwayAmplitude<0 || LineSwayAmplitude>3)
            throw new ArgumentException("LineSwayAmplitude must be between 0 and 3 world pixels.");
        if(!double.IsFinite(LineSwayPeriodSeconds) || LineSwayPeriodSeconds<1.5 || LineSwayPeriodSeconds>10)
            throw new ArgumentException("LineSwayPeriodSeconds must be between 1.5 and 10.");
    }
}

public static class FloatMotion
{
    // Purely visual. Tile coordinates, collisions, production and network state never move.
    public static int Offset(double elapsedSeconds, GridTile anchor, VisualOptions options)
    {
        if(!options.EnableBobbing || !double.IsFinite(elapsedSeconds))return 0;
        uint seed=unchecked((uint)anchor.X*73856093u ^ (uint)anchor.Y*19349663u);
        double angle=(elapsedSeconds%options.BobbingPeriodSeconds)/options.BobbingPeriodSeconds*Math.PI*2
            +(seed%1024)/1024d*Math.PI*2;
        return (int)Math.Round(Math.Sin(angle)*options.BobbingAmplitude);
    }
}

/// <summary>Convert generated art to a small game texture once at load time.</summary>
public static class PixelArt
{
    // Shared 32-colour palette. Packed format is MonoGame Color.PackedValue: AABBGGRR.
    public static readonly uint[] Palette=new[]{
        0x241b1a,0x3b2927,0x54382c,0x745038,0x99613b,0xb8804b,0xd5a164,0xe8c18b,
        0x773d2c,0xa65532,0xcb7843,0xe59e60,0xf1c583,
        0x233747,0x294e68,0x346d8c,0x4996b0,0x75b7c4,
        0x253127,0x3c4b2d,0x58683c,0x7b8851,0xa9ad6b,
        0x76403a,0xb64339,0xe46a4c,0xbcae94,0xebe0c6,0xfff2d5,
        0xb58c34,0xe3be59,0x9bd9d5
    }.Select(rgb=>0xff000000u | (uint)(rgb>>16) | (uint)(rgb&0x00ff00) | (uint)(rgb&255)<<16).ToArray();

    public static uint[] Bake(uint[] source,int imageWidth,int imageHeight,int x,int y,int width,int height,
        int pixelWidth,int pixelHeight,bool premultiplied)
    {
        if(imageWidth<=0 || imageHeight<=0 || source.LongLength!=(long)imageWidth*imageHeight
            || x<0 || y<0 || width<=0 || height<=0 || (long)x+width>imageWidth || (long)y+height>imageHeight
            || pixelWidth<1 || pixelWidth>96 || pixelHeight<1 || pixelHeight>96)
            throw new ArgumentException("Invalid pixel sprite dimensions or source rectangle.");
        var result=new uint[pixelWidth*pixelHeight];
        for(int py=0;py<pixelHeight;py++)for(int px=0;px<pixelWidth;px++)
        {
            int x0=x+px*width/pixelWidth,x1=x+Math.Max(px*width/pixelWidth+1,(px+1)*width/pixelWidth);
            int y0=y+py*height/pixelHeight,y1=y+Math.Max(py*height/pixelHeight+1,(py+1)*height/pixelHeight);
            long aa=0,rr=0,gg=0,bb=0;
            for(int sy=y0;sy<y1;sy++)for(int sx=x0;sx<x1;sx++)
            {
                uint c=source[sy*imageWidth+sx];int a=(int)(c>>24);
                if(a<64)continue;
                int factor=premultiplied?255:a;
                aa+=a;rr+=(c&255)*factor;gg+=((c>>8)&255)*factor;bb+=((c>>16)&255)*factor;
            }
            // Hard alpha keeps a clean grid and discards faint glow/fringing.
            if(aa<84L*(x1-x0)*(y1-y0))continue;
            result[py*pixelWidth+px]=Nearest((int)Math.Min(255,rr/aa),(int)Math.Min(255,gg/aa),(int)Math.Min(255,bb/aa));
        }
        return result;
    }
    private static uint Nearest(int r,int g,int b)
    {
        uint best=Palette[0];int distance=int.MaxValue;
        foreach(uint c in Palette)
        {
            int dr=r-(int)(c&255),dg=g-(int)((c>>8)&255),db=b-(int)((c>>16)&255);
            int d=2*dr*dr+4*dg*dg+3*db*db;
            if(d<distance){distance=d;best=c;}
        }
        return best;
    }
}

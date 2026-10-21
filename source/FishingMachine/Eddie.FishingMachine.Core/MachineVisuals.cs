namespace Eddie.FishingMachine.Core;

public readonly record struct PixelPoint(double X,double Y);
public sealed record RodRig(PixelPoint Tip,PixelPoint Landing);
public sealed class MachineLayout
{
    public string File {get;set;}="machine.png";
    public string IconFile {get;set;}="icon.png";
    public int Width {get;set;}=72;
    public int Height {get;set;}=36;
    public int OriginX {get;set;}=36;
    public int OriginY {get;set;}=32;
    public PixelPoint BaitOrigin {get;set;}=new(34,18);
    public PixelPoint ReadyOrigin {get;set;}=new(30,5);
    public RodRig[] Rods {get;set;}=new RodRig[]
    {
        new(new(10.5,1.5),new(2,32)),new(new(5.5,4.5),new(-3,28)),new(new(4.5,10.5),new(4,23)),
        new(new(61.5,1.5),new(70,32)),new(new(66.5,4.5),new(75,28)),new(new(67.5,10.5),new(68,23))
    };
    public void Validate()
    {
        if(Width<16 || Width>96 || Height<16 || Height>96 || OriginX<0 || OriginX>=Width || OriginY<0 || OriginY>=Height
            || Rods is null || Rods.Length!=6 || Rods.Any(r=>r is null || !Finite(r.Tip) || !Finite(r.Landing)
                || r.Tip.X<0 || r.Tip.X>=Width || r.Tip.Y<0 || r.Tip.Y>=Height)
            || !Finite(BaitOrigin) || !Finite(ReadyOrigin))throw new ArgumentException("Invalid six-rod sprite layout.");
    }
    private static bool Finite(PixelPoint p)=>double.IsFinite(p.X) && double.IsFinite(p.Y);
}

public readonly record struct VisualState(bool Bait,bool Lines,bool Ready)
{
    public static VisualState For(string phase,bool storedBait)
        =>new(storedBait || phase=="working",phase=="working",phase=="ready");
}

public static class LineMotion
{
    public const int Segments=8;
    // Input tips/landings and returned points are relative to the sprite canvas, in WORLD pixels.
    // Only the rod tip inherits platform bobbing; the float stays on the water surface.
    public static void Pose(PixelPoint tip,PixelPoint landing,double time,GridTile anchor,int index,int bob,
        VisualOptions options,Span<PixelPoint> result)
    {
        if(result.Length<Segments+1)throw new ArgumentException("Line pose buffer is too small.");
        if(!double.IsFinite(time))time=0;
        uint seed=unchecked((uint)anchor.X*73856093u ^ (uint)anchor.Y*19349663u);
        double a=(time%options.LineSwayPeriodSeconds)/options.LineSwayPeriodSeconds*Math.PI*2
            +(seed%1024)/1024d*Math.PI*2+index*1.17;
        double sway=options.EnableLineSway?Math.Sin(a)*options.LineSwayAmplitude:0;
        double drift=options.EnableLineSway?Math.Sin(a+.8)*options.LineSwayAmplitude*.5:0;
        var start=new PixelPoint(tip.X,tip.Y+bob);
        var end=new PixelPoint(landing.X+sway*.5,landing.Y+drift);
        for(int i=0;i<=Segments;i++)
        {
            double t=i/(double)Segments;
            result[i]=new(start.X+(end.X-start.X)*t+Math.Sin(Math.PI*t)*sway,
                start.Y+(end.Y-start.Y)*t+Math.Sin(Math.PI*t)*2);
        }
        result[0]=start;result[Segments]=end;
    }
}

public sealed record PixelGlyph(int Width,int Height,uint[] Pixels);
public static class MachineGlyphs
{
    // New code-native HUD/particle sprites, shared by the game renderer and the preview tool.
    public static readonly PixelGlyph Bait=Make(new[]{".gyg.","gywyg",".yyy."});
    public static readonly PixelGlyph Float=Make(new[]{".k.","rrr","www",".k."});
    public static readonly PixelGlyph Ripple=Make(new[]{"..ttt..","t.....t","..ttt.."});
    public static readonly PixelGlyph Ready=Make(new[]{
        "..kkkkkkkk..",".kwwwwwwwwk.","kwwwwwwwwwwk","kwwwbbwwbwwk","kwwbbbbbbwwk",
        "kwwbkbbbbwwk","kwwwbbwwbwwk",".kwwwwwwwwk.","..kkkkkkkk..",".....kk.....",".....k......"});
    private static PixelGlyph Make(string[] rows)
    {
        if(rows.Any(r=>r.Length!=rows[0].Length))throw new InvalidOperationException("Uneven glyph.");
        uint Color(char c)=>c switch
        {
            'k'=>PixelArt.Palette[0],'w'=>PixelArt.Palette[28],'g'=>PixelArt.Palette[29],
            'y'=>PixelArt.Palette[30],'r'=>PixelArt.Palette[24],'t'=>PixelArt.Palette[17],
            'b'=>PixelArt.Palette[16],_=>0
        };
        return new(rows[0].Length,rows.Length,rows.SelectMany(r=>r.Select(Color)).ToArray());
    }
}

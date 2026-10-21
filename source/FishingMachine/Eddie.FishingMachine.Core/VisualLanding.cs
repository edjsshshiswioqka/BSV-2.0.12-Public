namespace Eddie.FishingMachine.Core;

public static class VisualLanding
{
    // Preserve the approved silhouette on open water. At irregular banks move only the floats,
    // never the saved footprint, production sample tile or catch probabilities.
    public static PixelPoint Resolve(PixelPoint desired,GridTile anchor,int index,Func<GridTile,bool> water)
    {
        var tile=new GridTile((int)Math.Floor(desired.X/64),(int)Math.Floor(desired.Y/64));
        if(water(tile))return desired;
        var candidates=new HashSet<GridTile>(PreparationRules.Footprint(anchor));
        for(int y=tile.Y-3;y<=tile.Y+3;y++)for(int x=tile.X-3;x<=tile.X+3;x++)candidates.Add(new(x,y));
        var found=candidates.Where(water).OrderBy(p=>Math.Pow(p.X*64+32-desired.X,2)+Math.Pow(p.Y*64+32-desired.Y,2))
            .ThenBy(p=>p.Y).ThenBy(p=>p.X).Select(p=>(GridTile?)p).FirstOrDefault();
        if(found is not GridTile wet)return desired;
        return new(wet.X*64+16+(index%3)*16,wet.Y*64+22+(index/3)*20);
    }
}

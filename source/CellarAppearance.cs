using xTile;
using xTile.Layers;
using xTile.Tiles;

namespace BetterStardewValley;

// Visual retile of BSV's INSERTED cellar band only. Uses the installed game's own tile sheet,
// not bundled pixels. Entrance, stairs, native floor, warps, collision and placed objects are
// outside this edit: every replaced tile behaves exactly like the tile it replaces.
//
// Before (2.0.12): the band alternated two source rows, so the same crack pattern repeated every
// second row. Now each inserted row takes a clean native floor row (no pillar shadows, no dirt
// patch or barrel fragments) and a different horizontal offset, so cracks are scattered naturally.
internal static class CellarAppearance
{
    internal const string Key=WineRules.Prefix+"/CellarAppearance";
    internal static bool Apply(Map map)
    {
        if(map.Properties.ContainsKey(Key))return true;
        if(!map.Properties.TryGetValue(CellarMap.LayoutKey,out var value))return false;
        var fields=value.ToString().Split('/');
        if(fields.Length!=7||fields[0]!="1"||fields[1]!="20"||fields[2]!="18"
            ||!int.TryParse(fields[3],out int cut)||!int.TryParse(fields[4],out int added)||added<1)return false;
        var back=map.GetLayer("Back");var buildings=map.GetLayer("Buildings");
        if(back==null||buildings==null||back.LayerHeight!=18+added)return false;
        int width=back.LayerWidth,last=cut+added-1;
        // Light sources inside the band belong to a map edit BSV does not know: leave the band untouched.
        foreach(var (key,stride,index) in new[]{("Light",3,1),("DayTiles",4,2),("NightTiles",4,2)})
            if(Records(map,key,stride).Any(r=>int.TryParse(r[index],out int y)&&y>=cut&&y<=last))return false;
        var floors=CellarMap.FloorTiles(map);
        var rows=floors.Where(p=>p.Y<cut).GroupBy(p=>p.Y).ToDictionary(g=>g.Key,g=>g.Count());
        if(rows.Count==0)return false;
        int rowWidth=rows.Values.Max();
        var candidates=rows.Where(p=>p.Value==rowWidth&&p.Key>=7).Select(p=>p.Key).OrderBy(y=>y).Where(y=>CellarMap.SafeRow(map,y)).ToArray();
        if(candidates.Length<2)return false;
        var drawing=map.Layers.Where(l=>l.Id is "Front" or "AlwaysFront").ToArray();
        // Clean rows: full floor rows between full floor rows (no wall shadow above, no decoration below)
        // with nothing drawn over the interior.
        var clean=candidates.Where(y=>rows.GetValueOrDefault(y-1)==rowWidth&&rows.GetValueOrDefault(y+1)==rowWidth
            &&drawing.All(l=>Enumerable.Range(1,width-2).All(x=>l.Tiles[x,y]==null))).ToArray();
        if(clean.Length<2)return false;
        // Another mod edited the band after BSV expanded it: keep its edit.
        for(int y=cut;y<=last;y++)
        {
            int source=candidates[(y-cut)%candidates.Length];
            for(int x=0;x<width;x++)
                if(!Same(back.Tiles[x,y],back.Tiles[x,source]))return false;
            if(y!=last)foreach(var layer in drawing)
                for(int x=0;x<width;x++)
                    if(!Same(layer.Tiles[x,y],layer.Tiles[x,source]))return false;
        }
        var edits=new List<(Layer Layer,int X,int Y,Tile? Tile)>();
        for(int i=0;i<added;i++)
        {
            int y=cut+i,source=clean[i%clean.Length],shift=i*7%(width-2);
            for(int x=1;x<width-1;x++)
            {
                int sx=1+(x-1+shift)%(width-2);
                Tile? current=back.Tiles[x,y],floor=back.Tiles[sx,source];
                if(current==null||floor==null||buildings.Tiles[x,y]!=null||buildings.Tiles[sx,source]!=null)continue;
                if(!GreenhouseMap.SameBehaviour(current,floor))continue;
                edits.Add((back,x,y,CellarMap.Clone(floor,back)));
            }
            // Fragments of decorations copied into the band (barrel tops, dirt-patch edges) are removed.
            // The last band row keeps its drawing: it is the cellar's bottom wall.
            if(y==last)continue;
            foreach(var layer in drawing)
                for(int x=1;x<width-1;x++)
                    if(layer.Tiles[x,y]!=null&&!Interactive(layer.Tiles[x,y]))edits.Add((layer,x,y,null));
        }
        foreach(var e in edits)e.Layer.Tiles[e.X,e.Y]=e.Tile;
        map.Properties[Key]="2";return true;
    }
    private static bool Interactive(Tile tile)=>new[]{"Action","TouchAction","Water","WaterSource","Seat"}.Any(k=>GreenhouseMap.Property(tile,k)!=null);
    private static List<string[]> Records(Map map,string key,int stride)
    {
        if(!map.Properties.TryGetValue(key,out var value))return new();
        var parts=value.ToString().Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length%stride!=0)throw new InvalidOperationException("Unrecognized cellar visual property: "+key);
        return Enumerable.Range(0,parts.Length/stride).Select(i=>parts.Skip(i*stride).Take(stride).ToArray()).ToList();
    }
    private static bool SameProperties(Tile? a,Tile? b)=>a==null?b==null:b!=null
        && a.Properties.Count==b.Properties.Count&&a.Properties.All(p=>b.Properties.TryGetValue(p.Key,out var v)&&v.ToString()==p.Value.ToString())
        && a.TileIndexProperties.Count==b.TileIndexProperties.Count&&a.TileIndexProperties.All(p=>b.TileIndexProperties.TryGetValue(p.Key,out var v)&&v.ToString()==p.Value.ToString());
    private static bool Same(Tile? a,Tile? b)=>a==null?b==null:b!=null&&a.GetType()==b.GetType()
        &&a.TileSheet==b.TileSheet&&a.TileIndex==b.TileIndex&&SameProperties(a,b);
}

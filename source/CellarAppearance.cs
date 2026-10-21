using xTile;
using xTile.Tiles;

namespace BetterStardewValley;

// Local visual retile of BSV's INSERTED cellar band. Uses the installed game's own
// tile sheet, not bundled vanilla pixels. Entrance, stairs, native floor, warps,
// collision layer and placed objects are outside this edit.
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
        var floors=CellarMap.FloorTiles(map);int width=floors.GroupBy(p=>p.Y).Max(g=>g.Count());
        var rows=floors.Where(p=>p.Y>=7&&p.Y<cut).GroupBy(p=>p.Y).Where(g=>g.Count()==width).Select(g=>g.Key).OrderBy(y=>y).Where(y=>CellarMap.SafeRow(map,y)).ToArray();
        if(rows.Length<2)return false;
        var lights=Records(map,"Light",3);var day=Records(map,"DayTiles",4);var night=Records(map,"NightTiles",4);
        // Use a consecutive, undecorated pair, not unrelated strips containing sconce
        // tops/bottoms. Lamps are reintroduced as complete original native rows below.
        bool LampRow(int y)=>lights.Any(r=>int.TryParse(r[1],out int py)&&Math.Abs(py-y)<=1)
            || day.Concat(night).Any(r=>int.TryParse(r[2],out int py)&&py==y);
        int pair=rows.FirstOrDefault(y=>rows.Contains(y+1)&&!LampRow(y)&&!LampRow(y+1),-1);
        if(pair<0)return false; // unfamiliar wall composition: leave the original intact
        var lampRows=lights.Where(r=>int.TryParse(r[1],out int y)&&y>=7&&y<cut)
            .Select(r=>int.Parse(r[1])).Distinct().Where(y=>rows.Contains(y)&&rows.Contains(y-1)&&rows.Contains(y+1)).ToArray();
        int Source(int y)
        {
            int rel=y-cut;
            if(lampRows.Length>0&&rel%6 is >=1 and <=3)return lampRows[0]+rel%6-2;
            return pair+rel%2;
        }
        var edits=new List<(xTile.Layers.Layer Layer,int X,int Y,Tile? Tile)>();
        foreach(var layer in map.Layers.Where(l=>l.Id is "Back" or "Front" or "AlwaysFront"))
        for(int y=cut;y<cut+added;y++)for(int x=0;x<back.LayerWidth;x++)
        {
            var current=layer.Tiles[x,y];var old=layer.Tiles[x,rows[(y-cut)%rows.Length]];
            if(!Same(current,old))return false; // another mod edited the band: retain all of it
            var source=layer.Tiles[x,Source(y)];
            if(layer.Id=="Back"&&!SameProperties(current,source))continue;
            edits.Add((layer,x,y,CellarMap.Clone(source,layer)));
        }
        foreach(var (records,index) in new[]{(lights,1),(day,2),(night,2)})
        foreach(var record in records.Where(r=>int.TryParse(r[index],out int y)&&y>=cut&&y<cut+added))
        {
            int y=int.Parse(record[index]);var expected=(string[])record.Clone();expected[index]=rows[(y-cut)%rows.Length].ToString();
            if(!records.Any(r=>r.SequenceEqual(expected)))return false;
        }
        // Keep the non-BSV/original light records and align ONLY the generated copies
        // with the retiled native sconces. No additional map-wide ambient tint.
        string Rewrite(List<string[]> records,int yIndex)
        {
            var result=records.Where(r=>!int.TryParse(r[yIndex],out int y)||y<cut||y>=cut+added).Select(r=>(string[])r.Clone()).ToList();
            for(int y=cut;y<cut+added;y++)foreach(var r in records.Where(r=>int.TryParse(r[yIndex],out int py)&&py==Source(y)))
            {
                var copy=(string[])r.Clone();copy[yIndex]=y.ToString(System.Globalization.CultureInfo.InvariantCulture);result.Add(copy);
            }
            return string.Join(' ',result.SelectMany(r=>r));
        }
        var newLights=Rewrite(lights,1);var newDay=Rewrite(day,2);var newNight=Rewrite(night,2);
        foreach(var e in edits)e.Layer.Tiles[e.X,e.Y]=e.Tile;
        if(map.Properties.ContainsKey("Light"))map.Properties["Light"]=newLights;
        if(map.Properties.ContainsKey("DayTiles"))map.Properties["DayTiles"]=newDay;
        if(map.Properties.ContainsKey("NightTiles"))map.Properties["NightTiles"]=newNight;
        map.Properties[Key]="1";return true;
    }
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

namespace Eddie.FishingMachine.Core;

public static class SingleViewPlacement
{
    public const int Reach = 3;
    public const Facing Orientation = Facing.Right;
    public const string Schema = "6";
    public static bool InReach(IReadOnlyList<GridTile> tiles,GridTile player)
        => !tiles.Contains(player) && player.X>=tiles.Min(p=>p.X)-Reach && player.X<=tiles.Max(p=>p.X)+Reach
            && player.Y>=tiles.Min(p=>p.Y)-Reach && player.Y<=tiles.Max(p=>p.Y)+Reach;
    public static bool CanPlace(GridTile anchor,Func<GridTile,bool> water,Func<GridTile,bool> usable)
    {
        var tiles=PreparationRules.Footprint(anchor);
        return tiles.All(usable) && tiles.Any(water);
    }
    public static GridTile? Find(GridTile clicked,GridTile player,Func<GridTile,bool> water,Func<GridTile,bool> usable,Func<GridTile,bool>? extraValid=null)
    {
        var waterSource=water;var usableSource=usable;
        var w=new Dictionary<GridTile,bool>();var u=new Dictionary<GridTile,bool>();
        water=p=>w.TryGetValue(p,out bool v)?v:w[p]=waterSource(p);
        usable=p=>u.TryGetValue(p,out bool v)?v:u[p]=usableSource(p);
        GridTile? best=null;int bestScore=int.MinValue;
        foreach(var a in PreparationRules.Footprint(new(0,0)).Select(p=>new GridTile(clicked.X-p.X,clicked.Y-p.Y)))
        {
            var cells=PreparationRules.Footprint(a);
            if(!InReach(cells,player) || !CanPlace(a,water,usable) || extraValid!=null&&!extraValid(a))continue;
            int score=100*cells.Count(water)+10*DirectionRules.BankTiles(a,Facing.Left).Concat(DirectionRules.BankTiles(a,Facing.Right)).Count(water);
            if(score>bestScore){bestScore=score;best=a;}
        }
        return best;
    }
}

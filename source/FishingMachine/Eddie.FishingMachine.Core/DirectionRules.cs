namespace Eddie.FishingMachine.Core;

public enum Facing { Up = 0, Right = 1, Down = 2, Left = 3 }
public readonly record struct PlacementTarget(GridTile Anchor, Facing Facing);

public static class DirectionRules
{
    public static Facing Parse(string? value) => Enum.TryParse<Facing>(value, true, out var facing)
        && Enum.IsDefined(typeof(Facing), facing) ? facing : Facing.Right;
    public static Facing Next(Facing facing) => (Facing)(((int)facing + 1) % 4);
    public static GridTile AnchorFromNearWater(GridTile tile, Facing facing) => facing switch
    {
        Facing.Up => new(tile.X,tile.Y-1),
        Facing.Left => new(tile.X-1,tile.Y),
        _ => tile
    };
    public static GridTile[] BankTiles(GridTile a, Facing f) => f switch
    {
        Facing.Up => new[] { new GridTile(a.X, a.Y + 2), new GridTile(a.X + 1, a.Y + 2) },
        Facing.Right => new[] { new GridTile(a.X - 1, a.Y), new GridTile(a.X - 1, a.Y + 1) },
        Facing.Down => new[] { new GridTile(a.X, a.Y - 1), new GridTile(a.X + 1, a.Y - 1) },
        Facing.Left => new[] { new GridTile(a.X + 2, a.Y), new GridTile(a.X + 2, a.Y + 1) },
        _ => throw new ArgumentOutOfRangeException(nameof(f))
    };
    public static GridTile Bobber(GridTile a, Facing f) => f switch
    {
        Facing.Up => a,
        Facing.Right => new(a.X + 1, a.Y + 1),
        Facing.Down => new(a.X + 1, a.Y + 1),
        Facing.Left => a,
        _ => throw new ArgumentOutOfRangeException(nameof(f))
    };
    public static GridTile[] FrontTiles(GridTile a, Facing f) => f switch
    {
        Facing.Up => new[] { a, new GridTile(a.X+1,a.Y) },
        Facing.Right => new[] { new GridTile(a.X+1,a.Y), new GridTile(a.X+1,a.Y+1) },
        Facing.Down => new[] { new GridTile(a.X,a.Y+1), new GridTile(a.X+1,a.Y+1) },
        Facing.Left => new[] { a, new GridTile(a.X,a.Y+1) },
        _ => throw new ArgumentOutOfRangeException(nameof(f))
    };
    // Four cells are still reserved. Clear, walkable bank cells may support the base;
    // only one front cell needs fishable water. Occupancy is checked independently.
    public static bool CanPlace(GridTile a, Facing f, Func<GridTile, bool> water, Func<GridTile, bool> usable)
        => Enum.IsDefined(typeof(Facing), f) && PreparationRules.Footprint(a).All(usable) && FrontTiles(a,f).Any(water);
    public static bool InReach(GridTile a, Facing f, GridTile player)
        => Enum.IsDefined(typeof(Facing), f)
            && player.X >= a.X-2 && player.X <= a.X+3 && player.Y >= a.Y-2 && player.Y <= a.Y+3
            && !PreparationRules.Footprint(a).Contains(player);
    public static IEnumerable<GridTile> AnchorCandidates(GridTile clicked, Facing facing)
        => new[] { AnchorFromNearWater(clicked,facing), clicked, new GridTile(clicked.X-1,clicked.Y),
            new GridTile(clicked.X,clicked.Y-1), new GridTile(clicked.X-1,clicked.Y-1) }.Distinct();
    public static PlacementTarget? FindPlacement(GridTile clicked, Facing preferred, bool directionLocked, GridTile player,
        Func<GridTile,bool> water, Func<GridTile,bool> usable)
    {
        // Only share map queries within this search. Never reuse an answer across frames or placement transactions.
        var waterSource=water;var usableSource=usable;
        var waterCache=new Dictionary<GridTile,bool>();var usableCache=new Dictionary<GridTile,bool>();
        water=p=>waterCache.TryGetValue(p,out bool value)?value:waterCache[p]=waterSource(p);
        usable=p=>usableCache.TryGetValue(p,out bool value)?value:usableCache[p]=usableSource(p);
        if(!Enum.IsDefined(typeof(Facing),preferred))preferred=Facing.Right;
        var directions=directionLocked?new[]{preferred}:new[]{preferred,Facing.Up,Facing.Right,Facing.Down,Facing.Left}.Distinct();
        PlacementTarget? best=null;
        int bestScore=int.MinValue;
        foreach(var f in directions)
            foreach(var a in AnchorCandidates(clicked,f))
            {
                if(!InReach(a,f,player) || !CanPlace(a,f,water,usable))continue;
                // Prefer water under the entire base, then rods pointing towards open water.
                // Stable iteration breaks ties in favour of the player's chosen direction.
                int score=100*PreparationRules.Footprint(a).Count(water)
                    +10*BankTiles(a,Next(Next(f))).Count(water)+BankTiles(a,f).Count(p=>!water(p));
                if(score>bestScore){best=new(a,f);bestScore=score;}
            }
        return best;
    }
    public static GridTile? WaterBobber(GridTile a, Facing f, Func<GridTile,bool> water)
        => new[]{Bobber(a,f)}.Concat(FrontTiles(a,f)).Concat(PreparationRules.Footprint(a))
            .Distinct().Where(water).Select(p=>(GridTile?)p).FirstOrDefault();
}

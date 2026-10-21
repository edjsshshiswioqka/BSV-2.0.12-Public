namespace Eddie.FishingMachine.Core;

/// <summary>Read existing 1.3.x eight-tile saves without converting or moving their anchors.</summary>
public static class SavedFootprint
{
    public static GridTile[] Tiles(GridTile anchor, Facing facing, bool extended)
    {
        if (!extended) return PreparationRules.Footprint(anchor);
        var offset = facing switch
        {
            Facing.Up => new GridTile(0, -2), Facing.Right => new(2, 0),
            Facing.Down => new(0, 2), Facing.Left => new(-2, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(facing))
        };
        return PreparationRules.Footprint(anchor).Concat(PreparationRules.Footprint(
            new(anchor.X + offset.X, anchor.Y + offset.Y))).ToArray();
    }
    public static bool InReach(GridTile anchor, Facing facing, GridTile player, bool extended)
    {
        if (!extended) return DirectionRules.InReach(anchor, facing, player);
        var tiles = Tiles(anchor, facing, true);
        return !tiles.Contains(player) && player.X >= tiles.Min(p => p.X) - 2 && player.X <= tiles.Max(p => p.X) + 2
            && player.Y >= tiles.Min(p => p.Y) - 2 && player.Y <= tiles.Max(p => p.Y) + 2;
    }
    public static GridTile? WaterBobber(GridTile anchor, Facing facing, Func<GridTile, bool> water, bool extended)
    {
        if (!extended) return DirectionRules.WaterBobber(anchor, facing, water);
        var frontAnchor = facing switch
        {
            Facing.Up => new GridTile(anchor.X, anchor.Y - 2), Facing.Right => new(anchor.X + 2, anchor.Y),
            Facing.Down => new(anchor.X, anchor.Y + 2), _ => new(anchor.X - 2, anchor.Y)
        };
        return new[] { DirectionRules.Bobber(frontAnchor, facing) }.Concat(DirectionRules.FrontTiles(frontAnchor, facing))
            .Concat(Tiles(anchor, facing, true).OrderBy(p => p.Y).ThenBy(p => p.X))
            .Distinct().Where(water).Select(p => (GridTile?)p).FirstOrDefault();
    }
}

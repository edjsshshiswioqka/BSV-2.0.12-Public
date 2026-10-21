namespace Eddie.FishingMachine.Core;
/// <summary>Shared final Fishing Machine drawing conventions: 4x world, native-size held, 64px menu cell.</summary>
public static class SpriteGeometry
{
    public static PixelPoint World(GridTile tile,int originX,int originY)=>new(tile.X*64+64-originX*4,tile.Y*64+96-originY*4);
    public static PixelPoint HeldOffset(int width,int height)=>new(32-width/2d,128-height);
    public static PixelPoint MenuOffset(int width,int height,double scale)=>new((64-width*2*scale)/2,(64-height*2*scale)/2);
    public static GridTile[] CrabWaterTiles(GridTile a)=>PreparationRules.Footprint(a).Concat(new[]{new GridTile(a.X-1,a.Y+1),new GridTile(a.X+2,a.Y+1)}).ToArray();
    public static bool CrabWaterValid(GridTile a,Func<GridTile,bool> water)=>CrabWaterTiles(a).All(water);
}

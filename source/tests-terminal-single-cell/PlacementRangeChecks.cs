using BetterStardewValley.Orders;
using Microsoft.Xna.Framework;
using StardewValley;
using xTile.Tiles;
partial class Program
{
 static void PlacementRangeChecks()
 {
  Reset(); TerminalEntity.PlacementDistance=3;
  Check(TerminalEntity.CanPlace(Farm,new(4,4),Host),"near placement retained");
  Check(TerminalEntity.CanPlace(Farm,new(6,4),Host),"three-tile boundary accepted");
  Check(!TerminalEntity.CanPlace(Farm,new(7,4),Host),"four-tile distance refused");
  Check(!TerminalEntity.InReach(Host,new(6,4)),"interaction/pickup range not extended");
  Host.Items[0]=Item();World.Network.Request(Farm,new(6,4));World.Network.Tick();
  Check(Farm.objects.ContainsKey(new(6,4))&&!Host.Items.Any(OrderTerminal.IsTerminal),"actual distant placement consumes one item");
  Reset(); Host.Items[0]=Item(); World.Network.Request(Farm,new(7,4));World.Network.Tick();
  Check(!Farm.objects.Any()&&Host.Items.Count(OrderTerminal.IsTerminal)==1,"out-of-range attempt preserves item");
  Reset(); Farm.objects[new(5,4)]=new StardewValley.Object("390",1);
  Check(!TerminalEntity.CanPlace(Farm,new(6,4),Host),"intervening object blocks");
  Reset();var layer=Farm.map.GetLayer("Buildings");layer.Tiles[5,4]=new StaticTile(layer, Farm.map.TileSheets[0],BlendMode.Alpha,0);
  Check(!TerminalEntity.CanPlace(Farm,new(6,4),Host),"intervening map wall blocks");
  Reset();Farm.map.GetLayer("Back").Tiles[5,4].Properties["Water"]=true;
  Check(!TerminalEntity.CanPlace(Farm,new(6,4),Host),"intervening water blocks");
  Reset();Farm.objects[new(4,4)]=new StardewValley.Object("390",1);
  Check(!TerminalEntity.CanPlace(Farm,new(5,6),Host),"diagonal corner cannot bypass occupied cell");
  Reset();TerminalEntity.PlacementDistance=5;
  Check(TerminalEntity.CanPlace(Farm,new(8,4),Host),"configured five-tile range accepted");
  TerminalEntity.PlacementDistance=1;Check(!TerminalEntity.CanPlace(Farm,new(6,4),Host),"configured lower range respected");
  TerminalEntity.PlacementDistance=3;Put(Host,Farm,9,4);
  Check(TerminalEntity.CanPlace(Farm,new(5,4),Host),"range measured to nearest artwork cell");
  Check(!TerminalEntity.CanPlace(Home,new(5,4),Host),"different location refused");
  Reset();
 }
}

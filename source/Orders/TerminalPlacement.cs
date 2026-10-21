namespace BetterStardewValley.Orders;
internal readonly record struct Cell(int X,int Y);
// A — map-independent placement safety. Never sacrifice a connected walking tile other than the terminal tile.
internal static class TerminalPlacement
{
 internal static IEnumerable<Cell> Neighbors(Cell p){yield return new(p.X-1,p.Y);yield return new(p.X+1,p.Y);yield return new(p.X,p.Y-1);yield return new(p.X,p.Y+1);}
 internal static HashSet<Cell> Reach(HashSet<Cell> floor,Cell start,Cell? blocked=null)
 {
  var seen=new HashSet<Cell>();if(!floor.Contains(start)||start==blocked)return seen;var queue=new Queue<Cell>();queue.Enqueue(start);seen.Add(start);
  while(queue.Count>0)foreach(var n in Neighbors(queue.Dequeue()))if(n!=blocked&&floor.Contains(n)&&seen.Add(n))queue.Enqueue(n);return seen;
 }
 internal static HashSet<Cell> Paths(HashSet<Cell> floor,Cell start,IEnumerable<Cell> targets)
 {
  var parents=new Dictionary<Cell,Cell>();var queue=new Queue<Cell>();queue.Enqueue(start);parents[start]=start;
  while(queue.Count>0){var p=queue.Dequeue();foreach(var n in Neighbors(p))if(floor.Contains(n)&&!parents.ContainsKey(n)){parents[n]=p;queue.Enqueue(n);}}
  var result=new HashSet<Cell>{start};foreach(var target in targets)
  {
   var near=parents.Keys.OrderBy(p=>Math.Abs(p.X-target.X)+Math.Abs(p.Y-target.Y)).ThenBy(p=>p.Y).ThenBy(p=>p.X).FirstOrDefault();
   if(!parents.ContainsKey(near))continue;for(var p=near;;p=parents[p]){result.Add(p);if(p==start)break;}
  }return result;
 }
 internal static Cell? Choose(HashSet<Cell> floor,HashSet<Cell> eligible,Cell entry,HashSet<Cell> protectedTiles)
 {
  if(!floor.Contains(entry))return null;var before=Reach(floor,entry);
  foreach(var p in eligible.Where(p=>before.Contains(p)&&!protectedTiles.Contains(p)&&Math.Abs(p.X-entry.X)+Math.Abs(p.Y-entry.Y)>2)
   .OrderBy(p=>Neighbors(p).Any(n=>!floor.Contains(n))?0:1).ThenBy(p=>p.X).ThenBy(p=>p.Y))
  {
   var after=Reach(floor,entry,p);
   if(after.Count==before.Count-1&&Neighbors(p).Any(after.Contains))return p;
  }return null;
 }
}

using BetterStardewValley.AfkFishing;
using System.Text.Json;
int checks=0;var cases=new HashSet<string>();
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);cases.Add(name);}
TileNode N(int x,int y,string map="Farm")=>new(map,x,y);
var blocked=new HashSet<TileNode>();var actors=new HashSet<TileNode>();var gates=new HashSet<TileNode>();
IEnumerable<RouteStep> Grid(TileNode n,int width=9,int height=7)=>AfkPath.Neighbors(n)
 .Where(p=>p.X>=0&&p.Y>=0&&p.X<width&&p.Y<height&&(!blocked.Contains(p)||gates.Contains(p)))
 .Select(p=>new RouteStep(n,p,Gate:gates.Contains(p)));
AfkRouteSearch Search(TileNode start,TileNode goal,Func<TileNode,IEnumerable<RouteStep>>? edges=null,AfkNavigationMemory? memory=null,double now=0)
 =>new(start,new Dictionary<TileNode,int>{{goal,0}},edges??(n=>Grid(n)),e=>new(actors.Contains(e.To)?1:0,1+(memory?.Penalty(e.To,now)??0)));
List<RouteStep>? Finish(AfkRouteSearch s){int frames=0;while(s.Advance(3,double.MaxValue)==RouteSearchStatus.Searching)if(++frames>100000)throw new Exception("search did not terminate");return s.Route;}
foreach(string kind in new[]{"Tree","FruitTree","Stump","Log","Rock","Weed","Chest","Machine","Totem","OrderTerminal","Furniture","Building","Fence","Water","Cliff","Wall","NonPassableTerrain"})
{
 blocked.Clear();blocked.Add(N(4,3));var route=Finish(Search(N(1,3),N(7,3)));
 Check(route?.Count==8&&!route.Any(e=>blocked.Contains(e.To)),kind+" collision detour uses the shortest available side path (synthetic occupancy)");
}
blocked.UnionWith(new[]{N(4,2),N(4,4),N(3,2)});
var multi=Finish(Search(N(1,3),N(7,3)));Check(multi!=null&&!multi.Any(e=>blocked.Contains(e.To)),"multiple tree/chest/machine obstacles all bypassed");
blocked.Clear();for(int y=0;y<7;y++)blocked.Add(N(4,y));gates.Add(N(4,3));
var gateRoute=Finish(Search(N(1,3),N(7,3)));
Check(gateRoute?.Count==6&&gateRoute.Any(e=>e.Gate&&e.To==N(4,3)),"closed valid gate is an explicit traversable interaction edge");
gates.Clear();var sealedSearch=Search(N(1,3),N(7,3));Check(Finish(sealedSearch)==null&&sealedSearch.Status==RouteSearchStatus.NoPath,"sealed fence without gate exhausts frontier and reports no legal path");
blocked.Remove(N(4,6));Check(Finish(Search(N(1,3),N(7,3)))?.Count==12,"periodic retry finds newly opened physical route");
blocked.Clear();
foreach(string actor in new[]{"NPC","Cow","Pet","OtherPlayer","TemporaryCharacter"})
{
 actors.Clear();actors.Add(N(4,3));var path=Finish(Search(N(1,3),N(7,3)));
 Check(path?.Count==8&&!path.Any(e=>actors.Contains(e.To)),actor+" gets a dominant temporary cost and is bypassed");
}
// A sole corridor is structurally reachable even while an actor is in it. The executor
// waits before that node; a new search is possible without disabling or clipping.
for(int y=0;y<7;y++)for(int x=0;x<9;x++)if(y!=3)blocked.Add(N(x,y));
Check(Finish(Search(N(1,3),N(7,3)))?.Count==6,"actor in sole corridor does not sever the static world graph");
var memory=new AfkNavigationMemory();Check(memory.WaitForActor(1)&&memory.WaitForActor(1.5)&&!memory.WaitForActor(1.7),"dynamic blocker initially waits, then requests replan");
actors.Clear();Check(Finish(Search(N(1,3),N(7,3)))?.Count==6,"moving NPC leaves corridor and route resumes");
blocked.Clear();var cached=Finish(Search(N(1,3),N(7,3)))!;blocked.Add(cached[2].To);
Check(Finish(Search(cached[0].To,N(7,3)))!.All(e=>!blocked.Contains(e.To)),"chest placed during travel invalidates the next segment and new search detours from current tile");
blocked.Clear();Check(Finish(Search(N(1,3),N(7,3)))!.Count==6,"object removal permits shorter route on next replan");
IEnumerable<RouteStep> World(TileNode n)
{
 foreach(var e in Grid(n))yield return e;
 if(n==N(8,3,"Beach"))yield return new(n,N(0,3,"Town"),9,3);
 if(n==N(8,3,"Town"))yield return new(n,N(0,3,"Farm"),9,3);
 if(n==N(4,6,"Beach"))yield return new(n,N(0,3,"Backwoods"),4,7);
 if(n==N(8,3,"Backwoods"))yield return new(n,N(0,3,"Farm"),9,3);
 if(n==N(8,3,"Farm"))yield return new(n,N(0,3,"Home"),9,3);
}
var worldPath=Finish(Search(N(1,3,"Beach"),N(3,3,"Home"),World));
Check(worldPath!=null&&worldPath.Count(e=>e.Transition)==3,"world graph resolves actual local walking plus cross-location edges");
for(int y=0;y<7;y++)blocked.Add(N(4,y,"Town"));
worldPath=Finish(Search(N(1,3,"Beach"),N(3,3,"Home"),World));
Check(worldPath!=null&&worldPath.Any(e=>e.To.Map=="Backwoods"),"blocked preferred intermediate map chooses another world route and exit");
blocked.Clear();blocked.Add(N(7,3,"Town"));worldPath=Finish(Search(N(1,3,"Beach"),N(3,3,"Home"),World));
Check(worldPath!=null&&!worldPath.Any(e=>blocked.Contains(e.To)),"cross-map exit approach dynamically detours without changing transition semantics");
blocked.Clear();var incremental=Search(N(0,0),N(8,6));
Check(incremental.Advance(1,double.MaxValue)==RouteSearchStatus.Searching,"per-frame search budget exhaustion is SEARCHING, not no path");
Check(Finish(incremental)?.Count==14,"saved search frontier resumes and produces shortest path");
var candidates=AfkNavigationCandidates.Around(N(4,3)).ToDictionary(x=>x.Key,x=>x.Value);
Check(candidates.Count==81&&candidates.ContainsKey(N(8,3))&&!candidates.ContainsKey(N(9,3)),"fallback searches original and rings one through four, never five");
blocked.Add(N(4,3));candidates.Remove(N(4,3));
var fallback=new AfkRouteSearch(N(0,3),candidates.Where(p=>p.Key.X>=0&&p.Key.Y>=0&&p.Key.X<9&&p.Key.Y<7).ToDictionary(p=>p.Key,p=>p.Value),n=>Grid(n));
Check(Finish(fallback)!=null&&Math.Max(Math.Abs(fallback.Goal!.Value.X-4),Math.Abs(fallback.Goal.Value.Y-3))==1,"blocked original fishing tile selects reachable nearest ring");
blocked.Clear();memory.Clear();memory.Watch(N(4,3),64,0);memory.Watch(N(4,3),65,1);
Check(memory.Stuck(1.7),"stuck detector uses decreasing waypoint distance, not raw position jitter");
memory.ResetWaypoint();memory.Watch(N(4,3),64,0);memory.Watch(N(4,3),50,1.4);
Check(!memory.Stuck(2),"real progress resets stuck deadline");
memory.Clear();bool loop=false;for(int i=0;i<6;i++)loop|=memory.Visit(N(3+i%2,3),i*.2);
Check(loop&&memory.Penalty(N(4,3),2)>0,"A-B-A-B loop detected and recent nodes penalized");
Check(Finish(Search(N(1,3),N(7,3),memory:memory,now:2))!.All(e=>e.To!=N(4,3)),"anti-loop temporary cost selects alternative route");
Check(memory.Penalty(N(4,3),20)==0&&Finish(Search(N(1,3),N(7,3),memory:memory,now:20))!.Count==6,"loop penalties expire and original shortest route returns");
Check((int)AfkPhase.ResumeCountdown==15&&(int)AfkPhase.PausedNoRoute==12,"old numeric save phases stay unchanged");
Check(AfkRules.GoHome(2600,100,270),"unreachable-at-2AM remains in return intent, not an artificial time reset");
// Regression: old lexicographic occupancy chose a 102-step edge detour around a
// single actor. The finite cost chooses the short corridor, whose executor waits.
blocked.Clear(); actors.Clear(); actors.Add(N(2,50));
IEnumerable<RouteStep> Tall(TileNode n) => Grid(n,5,103).Where(e => e.To.X != 2 || e.To.Y is 0 or 50 or 102);
var finite=Finish(Search(N(1,50),N(3,50),Tall));
Check(finite?.Count==2&&finite.Any(e=>actors.Contains(e.To)),"temporary actor does not force a 102-step map-edge detour; short route waits at actor");
actors.Clear();
IEnumerable<RouteStep> Endless(TileNode n) { yield return new(n,n with{X=n.X+1}); }
var bounded=new AfkRouteSearch(N(0,0),new Dictionary<TileNode,int>{{N(0,1),0}},Endless);
Finish(bounded);Check(bounded.Status==RouteSearchStatus.BudgetExceeded&&bounded.Expanded<=AfkRouteSearch.MaximumNodes+1,
 "unbounded malformed topology hits explicit total search budget rather than looping forever or claiming NO PATH");
// Compare incremental production shortest paths to a separately implemented unweighted oracle.
var rng=new Random(117);
for(int sample=0;sample<600;sample++)
{
 blocked.Clear();actors.Clear();for(int y=0;y<7;y++)for(int x=0;x<9;x++)if(rng.NextDouble()<.24)blocked.Add(N(x,y));
 var start=N(0,0);var end=N(8,6);blocked.Remove(start);blocked.Remove(end);
 var q=new Queue<(TileNode Node,int Distance)>();var seen=new HashSet<TileNode>{start};q.Enqueue((start,0));int? expected=null;
 while(q.TryDequeue(out var current)){if(current.Node==end){expected=current.Distance;break;}foreach(var e in Grid(current.Node))if(seen.Add(e.To))q.Enqueue((e.To,current.Distance+1));}
 var actual=Finish(Search(start,end));Check(actual?.Count==expected,"random-grid shortest path agrees with independent BFS oracle");
 if(actual!=null)Check(actual.All(e=>!blocked.Contains(e.To)),"random-grid route never enters blocked tiles");
}
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks,scenarios=cases,randomizedMaps=600,real_game=false,real_multiplayer=false,scope="production incremental search, cost, candidate and recovery-memory rules; synthetic tile/world graphs"}));

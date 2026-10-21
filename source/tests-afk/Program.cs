using BetterStardewValley.AfkFishing;
using Newtonsoft.Json;
int checks=0;var scenarios=new HashSet<string>();
void Check(bool v,string name){checks++;if(!v)throw new Exception(name);scenarios.Add(name);}
TileNode N(int x,int y,string map="pond")=>new(map,x,y);
IEnumerable<RouteStep> Grid(TileNode n,int w,int h,HashSet<TileNode>? blocks=null)=>AfkPath.Neighbors(n).Where(p=>p.X>=0&&p.X<w&&p.Y>=0&&p.Y<h&&blocks?.Contains(p)!=true).Select(p=>new RouteStep(n,p));
var wall=Enumerable.Range(0,9).Select(y=>N(3,y)).ToHashSet();
Check(AfkPath.Find(N(0,0),N(5,0),n=>Grid(n,8,10,wall))?.Count==23,"wall detour uses shortest legal path");
Check(AfkPath.Find(N(0,0),N(5,0),n=>Grid(n,8,9,wall))==null,"closed wall fails without clipping");
Check(AfkPath.Find(N(0,0),N(5,0),n=>Grid(n,8,10,wall),3)==null,"search budget fails closed");
Check(AfkPath.Find(N(0,0),N(0,0),n=>Grid(n,8,10))?.Count==0,"already at target");
var chest=AfkPath.NearestChest(N(0,0),new[]{N(1,0),N(0,2)},n=>n.X>=-1&&n.Y>=-1&&n.X<20&&n.Y<20);
Check(chest==N(1,0),"adjacent tile accepted; distance two rejected under current 3x3 rule");
Check(AfkPath.NearestChest(N(0,0),new[]{N(10,0)},n=>n.X>=0&&n.Y>=0)==null,"ten-tile chest out of range");
Check(AfkPath.NearestChest(N(0,0),new[]{N(4,0)},n=>n.X>=0&&n.X<8&&n.Y>=0&&n.Y<10&&!wall.Contains(n))==null,"nearby chest behind long wall not linked");
Check(AfkPath.NearestChest(N(3,3),new[]{N(4,3),N(2,3),N(3,2)},n=>true)==N(3,2),"chest tie stable Y then X");
Check(AfkPath.NearestChest(N(3,3),new[]{N(4,3),N(2,3)},n=>true)==N(2,3),"chest X tie stable");
Check(AfkPath.NearestChest(N(0,0),new[]{N(0,1,"elsewhere")},n=>true)==null,"no cross-location chest");
var a=N(0,0,"A");var goal=N(2,0,"Home");
IEnumerable<RouteStep> Graph(TileNode n)
{
 foreach(var edge in Grid(n,8,2))yield return edge;
 if(n==N(2,0,"A"))yield return new(n,N(0,0,"Home"),3,0);
 if(n==N(7,0,"A"))yield return new(n,N(0,0,"B"),8,0);
 if(n==N(1,0,"B"))yield return new(n,N(2,0,"Home"),2,0);
 if(n==N(0,0,"Home"))yield return new(n,N(2,0,"A"),-1,0);
}
var route=AfkPath.Find(a,goal,Graph)!;
Check(route.Count==5&&route.Count(s=>s.Transition)==1,"cross-map shortest route includes walking cost, not just hop count");
Check(AfkPath.Find(goal,a,Graph)?.Count==5,"legal reverse route computed independently");
var blocker=new HashSet<TileNode>{N(2,0)};var detour=AfkPath.Find(N(0,0),N(4,0),n=>Grid(n,5,2,blocker));
Check(detour?.Count==6&&!detour.Any(s=>s.To==N(2,0)),"dynamic actor blocked: recompute detour");
blocker.Clear();Check(AfkPath.Find(N(0,0),N(4,0),n=>Grid(n,5,2,blocker))?.Count==4,"actor moved: shortest path restored");
Check(!AfkRules.GoHome(1230,200,270)&&!AfkRules.GoHome(2420,200,270)&&AfkRules.GoHome(2430,200,270),"12:30 AM uses 2430, never noon");
Check(AfkRules.GoHome(2500,200,270)&&AfkRules.GoHome(2600,200,270),"late time always latches home even with food");
Check(AfkRules.Hungry(81,270)&&!AfkRules.Hungry(81.01f,270),"inclusive 30% food threshold");
Check(AfkRules.GoHome(600,0,270)&&AfkRules.GoHome(600,13.5f,270)&&!AfkRules.GoHome(600,14,270),"five percent reserve independent of available food");
Check(AfkRules.GoHome(600,0.5f,270),"native rod refuses <=1 stamina; no permanent recast stall");
Check(AfkRules.AcceptFood(true,10,false,false)&&!AfkRules.AcceptFood(true,0,false,false)&&!AfkRules.AcceptFood(true,-5,false,false)
 &&!AfkRules.AcceptFood(true,10,true,false)&&!AfkRules.AcceptFood(true,10,false,true)&&!AfkRules.AcceptFood(false,10,false,false),"food excludes quest, nonpositive and unsafe effects");
var state=new AfkSave{Enabled=true,Phase=AfkPhase.Sleeping,Spot=new(){Location="Beach",X=11,Y=21,Facing=2,WaterX=11,WaterY=25,CastPower=.8f,RodId="(T)IridiumRod",RodSlot=2},ChestId="stable",ChestMap="Beach",ChestX=3,ChestY=21,OutputInventory="owner-only"};
var clone=JsonConvert.DeserializeObject<AfkSave>(JsonConvert.SerializeObject(state))!;
Check(clone.Enabled&&clone.Phase==AfkPhase.Sleeping&&clone.Spot!.Tile==state.Spot.Tile&&clone.ChestId==state.ChestId,"overnight save restores spot and linked chest identity");
Check(!JsonConvert.DeserializeObject<AfkSave>("{}")!.Enabled,"old save defaults disabled");
clone.Enabled=false;clone.Phase=AfkPhase.Disabled;
Check(!JsonConvert.DeserializeObject<AfkSave>(JsonConvert.SerializeObject(clone))!.Enabled,"manual cancellation survives next load");
int Move(List<Stack> s,List<Stack> d,int cap,Action? fail=null)=>AfkTransfer.Move(s,d,cap,x=>x.Count,(x,n)=>x.Count=n,(x,y)=>x.Id==y.Id&&x.Quality==y.Quality&&x.Max>1,x=>x.Max,x=>new(x.Id,1,x.Quality,x.Max),fail);
string Snapshot(IEnumerable<Stack> x)=>string.Join("|",x.Select(s=>s==null?"null":$"{s.Id}:{s.Count}:{s.Quality}:{s.Max}"));
var source=new List<Stack>{new("fish",4)};var target=new List<Stack>{new("fish",997)};
Check(Move(source,target,1)==2&&source.Single().Count==2&&target.Single().Count==999,"zero empty slots still fills partial existing stack");
Check(Move(source,target,1)==0&&source.Single().Count==2,"full chest keeps output intact");
target.Add(null!);Check(Move(source,target,2)==2&&source.Count==0,"null chest slot is available");
source=new(){new("rod",1,0,1)};target=new();Check(Move(source,target,1)==1&&source.Count==0&&target.Single().Id=="rod","nonstackable treasure transfers once");
source=new(){new("fish",5,2)};target=new(){new("fish",100,0)};
Check(Move(source,target,1)==0&&source.Count==1,"qualities do not merge");
for(int fault=1;fault<=12;fault++)
{
 source=new(){new("fish",5),new("gold",4)};target=new(){new("fish",997),null!};
 string beforeS=Snapshot(source),beforeT=Snapshot(target);int mutation=0;bool threw=false;
 try{Move(source,target,3,()=>{if(++mutation==fault)throw new Exception("injected");});}catch{threw=true;}
 if(threw)Check(beforeS==Snapshot(source)&&beforeT==Snapshot(target),"fault rollback restores both inventories and all original stack counts");
 else Check(source.Sum(x=>x.Count)+target.Where(x=>x!=null).Sum(x=>x.Count)==1006,"successful transfer conserves all items");
}
var rng=new Random(7501);
for(int sample=0;sample<1000;sample++)
{
 source=Enumerable.Range(0,rng.Next(1,8)).Select(_=>new Stack("i"+rng.Next(4),rng.Next(1,999),rng.Next(3))).ToList();
 target=Enumerable.Range(0,rng.Next(1,8)).Select(_=>new Stack("i"+rng.Next(4),rng.Next(1,999),rng.Next(3))).ToList();
 Dictionary<string,int> Counts()=>source.Concat(target).GroupBy(x=>x.Id+"/"+x.Quality).ToDictionary(g=>g.Key,g=>g.Sum(x=>x.Count));
 var before=Counts();Move(source,target,rng.Next(1,12));var after=Counts();
 Check(before.Count==after.Count&&before.All(p=>after[p.Key]==p.Value),"randomized per-item/per-quality conservation");
 Check(target.All(x=>x.Count>0&&x.Count<=999)&&source.All(x=>x.Count>0&&x.Count<=999),"no invalid stacks after transfer");
}
foreach(string lang in new[]{"default","zh"})
{
 var d=JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText(Path.Combine(args[0],lang+".json")))!;
 foreach(var phase in Enum.GetNames<AfkPhase>())Check(d.ContainsKey("afk.state."+phase),"every AFK state localized in "+lang);
 Check(d.ContainsKey("afk.storage-full")&&d.ContainsKey("afk.resume")&&d.ContainsKey("afk.no-chest"),"required player messages localized in "+lang);
}
Console.WriteLine(JsonConvert.SerializeObject(new{passed=true,checks,scenarios,randomizedInventoryCases=1000,live_game=false,real_multiplayer=false}));
sealed class Stack{public string Id;public int Count,Quality,Max;public Stack(string id,int count,int quality=0,int max=999){Id=id;Count=count;Quality=quality;Max=max;}}

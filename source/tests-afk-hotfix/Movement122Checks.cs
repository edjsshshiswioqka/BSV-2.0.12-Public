using BetterStardewValley.AfkFishing;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

partial class Program
{
 static bool MotionRecording;
 static int MotionStops,MotionCommands,MotionInvalidations;
 static readonly List<string> MotionReasons=new();
 static void CountMotionStop(){if(MotionRecording)MotionStops++;}
 static void CountMotionCommand(){if(MotionRecording)MotionCommands++;}
 static void CountInvalidation()
 {
  if(!MotionRecording)return;MotionInvalidations++;
  MotionReasons.Add(string.Join("/",new StackTrace().GetFrames().Select(f=>f.GetMethod()?.Name)
   .Where(n=>n is "RecoverRoute" or "ActorBlocked" or "TopologyChanged" or "InvalidatePath" or "StartHome" or "Stop" or "Travel").Distinct()));
 }
 static void Movement122Checks(Harmony h)
 {
  string dir=Environment.GetEnvironmentVariable("BSV_MOVEMENT_TRACE_DIR")??Path.Combine(AppContext.BaseDirectory,"movement-traces");
  Directory.CreateDirectory(dir);bool baseline=Environment.GetEnvironmentVariable("BSV_MOVEMENT_BASELINE")=="1";
  Game1.objectData["322"]=new(){Name="Fence",DisplayName="Fence",Type="Crafting",Texture="fixture",SpriteIndex=0};
  typeof(Fence).GetField("_FenceLookup",Any)!.SetValue(null,new Dictionary<string,StardewValley.GameData.Fences.FenceData>{{"322",new(){Health=100}}});
  h.Patch(typeof(AfkFishingService).GetMethod("StopWalking",Any)!,prefix:new(typeof(Program),nameof(CountMotionStop)));
  h.Patch(typeof(AfkFishingService).GetMethod("InvalidatePath",Any)!,prefix:new(typeof(Program),nameof(CountInvalidation)));
  h.Patch(typeof(Farmer).GetMethod("setMoving",Any)!,prefix:new(typeof(Program),nameof(CountMotionCommand)));
  var originalMap=Map;var summaries=new List<object>();
  foreach(string scenario in new[]{"long_straight","fence_turns","narrow_stairs","oblique"})
  {
   Map=new GameLocation();Map.name.Value="Motion_"+scenario;Map.map=Fixture();Game1.locations.Add(Map);
   var back=Map.Map.GetLayer("Back");var walls=Map.Map.GetLayer("Buildings");
   for(int y=0;y<24;y++)for(int x=0;x<28;x++)back.Tiles[x,y].Properties.Remove("Water");
   // Synthetic walkable stair/path tiles use normal Back data; side walls and native
   // fences remain real collision. This is not the user's actual map or game video.
   var points=scenario switch {
    "long_straight"=>Enumerable.Range(2,24).Select(x=>new Point(x,5)).ToArray(),
    "fence_turns"=>new[]{new Point(2,4),new(9,4),new(9,12),new(18,12),new(18,6),new(24,6)},
    "narrow_stairs"=>new[]{new Point(3,2),new(3,17),new(9,17),new(9,8),new(18,8),new(18,16),new(24,16)},
    _=>new[]{new Point(2,2),new(24,17)}
   };
   var corridor=new HashSet<Point>();
   for(int i=1;i<points.Length;i++)
   {
    var a=points[i-1];var b=points[i];
    if(a.X==b.X)for(int y=Math.Min(a.Y,b.Y);y<=Math.Max(a.Y,b.Y);y++)corridor.Add(new(a.X,y));
    else if(a.Y==b.Y)for(int x=Math.Min(a.X,b.X);x<=Math.Max(a.X,b.X);x++)corridor.Add(new(x,a.Y));
   }
   if(scenario=="narrow_stairs")for(int y=0;y<24;y++)for(int x=0;x<28;x++)
    if(!corridor.Contains(new(x,y)))walls.Tiles[x,y]=new xTile.Tiles.StaticTile(walls,Map.Map.TileSheets[0],xTile.Tiles.BlendMode.Alpha,1);
   if(scenario=="fence_turns")foreach(var p in corridor)
    foreach(var d in new[]{new Point(0,1),new Point(0,-1),new Point(1,0),new Point(-1,0)})
    {var at=p+d;if(!corridor.Contains(at)&&at.X>=0&&at.Y>=0)Map.objects[new(at.X,at.Y)]=new Fence(new(at.X,at.Y),"322",false);}
   // Reset clears objects, so preserve fixture fences across per-driver resets.
   var objects=Map.objects.Pairs.ToArray();
   foreach(bool manual in new[]{false,true})
   {
    Reset();foreach(var pair in objects)Map.objects[pair.Key]=pair.Value;
    Who.setRunning(true,false);float nativeBudget=Who.getMovementSpeed();Who.maxStamina.Value=270;Who.Stamina=270;Who.FarmerSprite.SetOwner(Who);Who.FarmerSprite.PauseForSingleAnimation=false;
    Put(Who,Map,points[0].X,points[0].Y);Data.Enabled=!manual;Data.Phase=AfkPhase.ReturningToSpot;
    var end=points[^1];Data.Spot=new(){Location=Map.Name,X=end.X,Y=end.Y,Facing=1,WaterX=28,WaterY=end.Y,RodId=Rod.QualifiedItemId,RodSlot=0};
    var steps=new List<RouteStep>();
    for(int i=1;i<points.Length;i++)steps.Add(new(new(Map.Name,points[i-1].X,points[i-1].Y),new(Map.Name,points[i].X,points[i].Y)));
    Field("Route",steps);Field("RouteIndex",0);Field("Goal",(TileNode?)steps[^1].To);
    int frames=0,idle=0,faceChanges=0,manualIndex=0,overlaps=0;double pathLength=0,maxStep=0;
    MotionStops=MotionCommands=MotionInvalidations=0;MotionReasons.Clear();MotionRecording=true;
    var trace=new List<object>();int priorFacing=Who.FacingDirection;
    while(frames++<2200)
    {
     Point stand=Who.StandingPixel;if(Math.Abs(stand.X-(end.X*64+32))<=3&&Math.Abs(stand.Y-(end.Y*64+32))<=3)break;
     Clock(10+frames*.016);var before=Who.Position;MotionReasons.Clear();int prevStops=MotionStops,prevCommands=MotionCommands;
     if(!manual)
     {
      Field("WalkRefreshed",false);Run("Travel");if(!Field<bool>("WalkRefreshed"))Run("StopWalking");Who.Halt();
     }
     else
     {
      while(manualIndex<steps.Count-1&&Math.Abs(stand.X-(steps[manualIndex].To.X*64+32))<=3&&Math.Abs(stand.Y-(steps[manualIndex].To.Y*64+32))<=3)manualIndex++;
      var to=steps[manualIndex].To;float dx=to.X*64+32-stand.X,dy=to.Y*64+32-stand.Y;
      var dirs=new List<int>();if(Math.Abs(dx)>3)dirs.Add(dx>0?1:3);if(Math.Abs(dy)>3)dirs.Add(dy>0?2:0);
      foreach(var n in Who.movementDirections.ToArray())if(!dirs.Contains(n))Who.setMoving((byte)((1<<n)+32));
      foreach(var n in dirs)if(!Who.movementDirections.Contains(n))Who.setMoving((byte)(1<<n));
     }
     Who.MovePosition(Game1.currentGameTime,Game1.viewport,Who.currentLocation);Who.updateMovementAnimation(Game1.currentGameTime);
     double distance=Vector2.Distance(before,Who.Position);if(distance<.001)idle++;if(Who.FacingDirection!=priorFacing)faceChanges++;
     if(Map.objects.Values.Any(o=>!o.isPassable()&&o.GetBoundingBox().Intersects(Who.GetBoundingBox())))overlaps++;
     priorFacing=Who.FacingDirection;pathLength+=distance;maxStep=Math.Max(maxStep,distance);
     var target=manual?steps[manualIndex].To:Field<TileNode?>("WalkTarget");
     trace.Add(new {frame=frames,x=Who.Position.X,y=Who.Position.Y,dx=Who.Position.X-before.X,dy=Who.Position.Y-before.Y,distance,
      running=Who.running,facing=Who.FacingDirection,sprite=Who.FarmerSprite.CurrentFrame,directions=Who.movementDirections.ToArray(),
      phase=Data.Phase.ToString(),node=manual?manualIndex:Field<int>("RouteIndex"),target=target?.ToString(),
      active=Field<bool>("WalkActive"),nativeController=Who.controller?.GetType().Name??"none",move=Who.CanMove,
      objectCount=Map.objects.Length,overlaps,stops=MotionStops-prevStops,commands=MotionCommands-prevCommands,replans=MotionInvalidations,reasons=MotionReasons.ToArray()});
    }
    MotionRecording=false;string name=scenario+(manual?"-native-input":"-afk");
    File.WriteAllText(Path.Combine(dir,name+".json"),JsonSerializer.Serialize(trace,new JsonSerializerOptions{WriteIndented=true}));
    var summary=new {name,frames=trace.Count,reached=frames<2200,idle,faceChanges,pathLength,maxStep,overlaps,stops=MotionStops,commands=MotionCommands,replans=MotionInvalidations,live_game=false};
    summaries.Add(summary);Console.Error.WriteLine("MOTION122 "+JsonSerializer.Serialize(summary));
    Check(frames<2200,name+" reaches target with native collision");
    if(!baseline)Check(overlaps==0,name+" never overlaps a fence or player object");
    if(!baseline&&!manual)Check(maxStep<=nativeBudget+.025,"AFK total displacement stays within native 16ms movement budget: "+name);
    if(!baseline&&!manual)Check(idle==0,name+" has no unexpected stopped update on unobstructed route");
    Run("StopWalking");
   }
   Game1.locations.Remove(Map);
  }
  Map=originalMap;Reset();
  if(!baseline)MovementOwnershipAndTraceChecks();
  File.WriteAllText(Path.Combine(dir,"summary.json"),JsonSerializer.Serialize(summaries,new JsonSerializerOptions{WriteIndented=true}));
  h.Unpatch(typeof(AfkFishingService).GetMethod("StopWalking",Any)!,HarmonyPatchType.Prefix,h.Id);
  h.Unpatch(typeof(AfkFishingService).GetMethod("InvalidatePath",Any)!,HarmonyPatchType.Prefix,h.Id);
  h.Unpatch(typeof(Farmer).GetMethod("setMoving",Any)!,HarmonyPatchType.Prefix,h.Id);
 }
 static void MovementOwnershipAndTraceChecks()
 {
  Reset();Who.speed=4;Data.Enabled=true;Data.Phase=AfkPhase.ReturningToSpot;
  var target=new TileNode(Map.Name,6,5);Run("RequestWalk",target,false);
  Field("WalkActive",true);Who.setMoving(2);int generation=Field<int>("Generation");
  Field("Generation",generation+1);var before=Who.Position;
  Who.MovePosition(Game1.currentGameTime,Game1.viewport,Map);
  Check(Who.Position==before&&!Field<bool>("WalkActive")&&!Field<TileNode?>("WalkTarget").HasValue,
   "obsolete movement generation cannot move farmer after task handoff");
  Run("RequestWalk",target,false);Field("WalkActive",true);Who.setMoving(2);
  Who.controller=(StardewValley.Pathfinding.PathFindController)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(StardewValley.Pathfinding.PathFindController));
  Run("StopWalking");Check(Who.movementDirections.Contains(1)&&Who.controller!=null,
   "relinquishing AFK ownership does not Halt an external native controller");Who.controller=null;Who.Halt();
  Run("Stop");Run("RequestWalk",target,false);
  Check(!Field<TileNode?>("WalkTarget").HasValue,"disabled session cannot accept delayed travel intent");
  Reset();var command=Recorder.Commands["bsv_afk_walk_trace"];int writes=Recorder.JsonWrites.Count;
  Run("CaptureWalkTrace");Check(Field<object?>("Trace")==null&&Recorder.JsonWrites.Count==writes,"disabled tracing creates no capture or output");
  command("bsv_afk_walk_trace",new[]{"999"});Check(Field<object?>("Trace")==null,"trace duration is bounded, invalid request does not start");
  command("bsv_afk_walk_trace",new[]{"5"});Check(Field<object?>("Trace")!=null&&!Data.Enabled,"manual input comparison trace does not enable or control AFK");
  before=Who.Position;Who.setMoving(2);Who.MovePosition(Game1.currentGameTime,Game1.viewport,Map);Run("CaptureWalkTrace");
  Check(Who.Position!=before,"trace leaves ordinary native input movement enabled");
  Clock(16);Run("CaptureWalkTrace");Check(Field<object?>("Trace")==null&&Recorder.JsonWrites.Count==writes+1,"bounded trace exports once and stops after duration");
  var doc=JsonSerializer.Serialize(Recorder.JsonWrites[^1].Data);
  Check(doc.Contains("spriteFrame")&&doc.Contains("directions")&&doc.Contains("node")&&doc.Contains("controller")&&doc.Contains("searches")&&doc.Contains("reason"),
   "trace exports positions, direction, animation, route/controller and reason counters");
  command("bsv_afk_walk_trace",Array.Empty<string>());Run("Load");
  Check(Field<object?>("Trace")==null&&Recorder.JsonWrites.Count==writes+2,"load flushes trace and does not retain old recording session");
  Reset();command("bsv_afk_walk_trace",new[]{"5"});
  var title=Recorder.Events.Single(e=>e.Name=="add_ReturnedToTitle"&&e.Handler.Method.DeclaringType!.FullName!.StartsWith(typeof(AfkFishingService).FullName!)).Handler;
  title.DynamicInvoke(null,null);
  Check(Field<object?>("Trace")==null&&Recorder.JsonWrites.Count==writes+3,"returning to title flushes recording and clears per-screen diagnostics");
  foreach(int millis in new[]{5,8,16,33,50})
  {
   Reset();Who.speed=4;Data.Enabled=true;Data.Phase=AfkPhase.ReturningToSpot;
   Game1.currentGameTime=new GameTime(TimeSpan.FromSeconds(20),TimeSpan.FromMilliseconds(millis));
   Who.movementDirections.Clear();Who.setMoving(2);Who.setRunning(true,false);float budget=Who.getMovementSpeed();
   before=Who.Position;Run("RequestWalk",target,false);
   Who.MovePosition(Game1.currentGameTime,Game1.viewport,Map);
   double moved=Vector2.Distance(before,Who.Position);
   Check(moved>0&&moved<=budget+.001,"normalized AFK step respects native elapsed-time budget at "+millis+"ms");
   Run("StopWalking");
  }
  Reset();
 }
}

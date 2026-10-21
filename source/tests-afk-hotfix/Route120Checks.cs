using BetterStardewValley.AfkFishing;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

partial class Program
{
    static void Route120Checks()
    {
        Reset(); var warps = Map.warps.ToArray(); Map.warps.Clear();
        Put(Who, Map, 1, 2); Data.Enabled = true; Data.Phase = AfkPhase.ReturningToSpot;
        Data.Spot = new() { Location = Map.Name, X = 5, Y = 10, Facing = 1, WaterX = 9, WaterY = 10,
            RodSlot = 0, RodId = Rod.QualifiedItemId };
        Who.speed = 4; // normal native walking speed; the minimal Farmer fixture defaults to zero.
        var start = Who.StandingPixel; var goal = new Point(5 * 64 + 32, 10 * 64 + 32);
        Check(AfkWorld.SegmentClear(Map, start, goal, Who), "whole native farmer-footprint sweep accepts an open oblique line");
        Run("Travel");
        Check(Field<List<RouteStep>>("Route").Count == 1 && Field<TileNode?>("WalkTarget") == new TileNode(Map.Name, 5, 10),
            "actual travel selects direct same-map line instead of a cardinal L or map-edge route");
        Console.Error.WriteLine($"walk preflight: CanWalk={typeof(AfkFishingService).GetMethod("CanWalk", Any)!.Invoke(Service, new object[]{Who})}, move={Who.CanMove}, using={Who.UsingTool}, eating={Who.isEating}, freeze={Game1.freezeControls}, locreq={Game1.locationRequest != null}, controller={Who.controller != null}, local={Who.IsLocalPlayer}, pause={Who.freezePause}, static={AfkWorld.StaticClear(Map,new(5,10),Who)}, bbox={Who.GetBoundingBox()}, speed={Who.getMovementSpeed()}");
        double deviation = 0; int frames = 0;
        while (Vector2.Distance(new(Who.StandingPixel.X, Who.StandingPixel.Y), new(goal.X, goal.Y)) > 4 && frames++ < 400)
        {
            NativeFrame(new(Map.Name, 5, 10)); var pos = Who.StandingPixel;
            deviation = Math.Max(deviation, Math.Abs((goal.X - start.X) * (pos.Y - start.Y) - (goal.Y - start.Y) * (pos.X - start.X))
                / Math.Sqrt(Math.Pow(goal.X - start.X, 2) + Math.Pow(goal.Y - start.Y, 2)));
        }
        Console.Error.WriteLine($"straight trace: frames={frames}, deviation={deviation}, start={start}, end={Who.StandingPixel}, goal={goal}, phase={Data.Phase}, target={Field<TileNode?>("WalkTarget")}");
        Check(frames < 400 && deviation < 5, "actual native collision/movement/animation reaches oblique target within five pixels of straight line");
        Run("StopWalking");
        Put(Who, Map, 2, 5); var chest = AddChest(4, 5);
        Check(!AfkWorld.SegmentClear(Map, Who.StandingPixel, new(6 * 64 + 32, 5 * 64 + 32), Who),
            "visible destination with chest in the middle fails entire-segment sweep");
        Map.objects.Clear(); AddChest(5, 4); AddChest(4, 5); Put(Who, Map, 4, 4);
        Check(!AfkWorld.SegmentClear(Map, Who.StandingPixel, new(5 * 64 + 32, 5 * 64 + 32), Who),
            "two corner-adjacent blockers cannot be diagonally squeezed through");
        Map.objects.Clear(); Put(Who, Map, 6, 5);
        Check(!AfkWorld.SegmentClear(Map, Who.StandingPixel, new(9 * 64 + 32, 5 * 64 + 32), Who), "line sweep rejects water even without Back Passable property");
        Check(!AfkWorld.SegmentClear(Map, Who.StandingPixel, new(-32, 5 * 64 + 32), Who), "line sweep rejects leaving map bounds");
        var npc = new NPC(); npc.Sprite = new AnimatedSprite("fixture"); npc.Position = new(7 * 64, 5 * 64 + 16); Map.characters.Add(npc);
        Check(!AfkWorld.SegmentClear(Map, Who.StandingPixel, new(7 * 64 + 32, 5 * 64 + 32), Who), "live NPC bounding box blocks shortcut"); Map.characters.Clear();
        Check(AfkWorld.SegmentClear(Map, Who.StandingPixel, new(7 * 64 + 32, 5 * 64 + 32), Who), "moving NPC away restores shortcut without stale actor cache");

        // Shoreline return candidates are judged by legal standing/fishing, not surrounding land.
        Data.Spot!.X = 7; Data.Spot.Y = 5; Put(Who, Map, 2, 5);
        var candidates = (Dictionary<TileNode, int>)typeof(AfkFishingService).GetMethod("ReturnSpots", Any)!
            .Invoke(Service, new object[] { new AfkWorld(Who) })!;
        Check(candidates.ContainsKey(new(Map.Name, 7, 5)) && candidates.Keys.All(n => AfkWorld.CanStand(Map, new(n.X, n.Y), Who)),
            "automatic return accepts reachable shoreline position without requiring all nine cells to be land");

        Field("HomeIntent", false); Data.Phase = AfkPhase.WaitingForRoute; Field("RouteFailures", 5); Field("ActorWaitTile", null);
        Run("InvalidatePath", 0d); Run("Travel");
        Check(!Data.Enabled && Data.Phase==AfkPhase.Disabled && Field<AfkRouteSearch?>("Search")==null && Who.movementDirections.Count==0,
            "five exhausted route attempts disable AFK and release control without another search");
        Run("TopologyChanged", Map);
        Check(!Data.Enabled,"topology event cannot silently restart stopped AFK");
        foreach (var warp in warps) Map.warps.Add(warp);
        Reset();
    }
}

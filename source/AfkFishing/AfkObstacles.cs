using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Objects;
namespace BetterStardewValley.AfkFishing;

internal sealed partial class AfkFishingService
{
    private const double NativePetAttemptSeconds=3, OverlapWaitSeconds=.65;
    // This is permission to KEEP issuing native movement, never a collision override.
    private bool BedPetAttempt(Point target)
    {
        var s=Current;var who=Game1.player;var map=who.currentLocation;
        if(!s.HomeIntent||!CanWalk(who)||s.RecoveryCount<1)return false;
        var home=Utility.getHomeOfFarmer(who);var bed=home?.GetPlayerBed();
        if(home!=map||bed==null)return false;
        var area=bed.GetBoundingBox();area.Inflate(64,64);
        if(!area.Contains(who.StandingPixel)||!area.Contains(new Point(target.X*64+32,target.Y*64+32)))return false;
        var sweep=Rectangle.Union(who.GetBoundingBox(),AfkWorld.FootBox(target));
        var blockers=map.characters.Where(n=>!n.IsInvisible&&AfkWorld.CharacterBox(n).Intersects(sweep)).ToArray();
        if(blockers.Length!=1||blockers[0] is not Pet pet)return false;
        if(s.NativePet!=pet){s.NativePet=pet;s.NativePetSince=Now;WalkReason("verified bedside pet: native continuous push");}
        if(Now-s.NativePetSince>NativePetAttemptSeconds)return false;
        return true;
    }
    private void ClearObstacleIntent(){Current.NativePetPermission=false;Current.NativePet=null;Current.NativePetSince=0;Current.OverlapSince=-1;}
    private bool StepActorsClear(Point target)
    {
        var who=Game1.player;
        Current.NativePetPermission=BedPetAttempt(target);
        NPC? native=Current.NativePetPermission?Current.NativePet:null;
        return AfkWorld.DynamicStepClear(who.currentLocation,who.GetBoundingBox(),AfkWorld.FootBox(target),who,native);
    }
    private bool TryAvoidOverlap()
    {
        var s=Current;var who=Game1.player;
        if(!AfkWorld.DynamicBlocked(who.currentLocation,who.GetBoundingBox(),who)){s.OverlapSince=-1;return false;}
        if(s.OverlapSince<0)s.OverlapSince=Now;
        if(Now-s.OverlapSince<OverlapWaitSeconds)return true;
        if(CommittedCatch(s.Rod)||who.UsingTool||who.isEating||s.LockPending)return true;
        s.Generation++;ClearChest();s.Area=null;s.AreaAt=0;s.Nearby.Clear();s.Standby.Clear();
        s.Data.Phase=AfkPhase.ReturningToSpot;s.HomeIntent=false;InvalidatePath();Persist();
        WalkReason("occupied fishing stand: retaining original spot, finding reachable local stand");return true;
    }
}

using BetterStardewValley.AfkFishing;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
partial class Program
{
 static void ObstacleR12Checks()
 {
  Reset();Data.Enabled=true;Data.Phase=AfkPhase.ReturningToSpot;Who.speed=4;Who.setRunning(true,false);Put(Who,Map,4,5);
  var npc=new NPC{Sprite=new AnimatedSprite("fixture"),Position=Who.Position};Map.characters.Add(npc);
  var start=Who.GetBoundingBox();var blocker=AfkWorld.CharacterBox(npc);var outside=start;outside.X+=64;
  Check(start.Intersects(blocker),"R12 fixture starts with overlapping real NPC bounding boxes");
  Check(AfkWorld.DynamicStepClear(Map,start,outside,Who),"R12 overlapping NPC permits monotonic outward segment");
  var deeper=start;deeper.X-=8;
  // Opposite direction from an already partly exited point must not re-enter.
  var partial=start;partial.X+=16;
  Check(!AfkWorld.DynamicStepClear(Map,partial,start,Who),"R12 re-entering NPC is not an overlap escape");
  var npc2=new NPC{Sprite=new AnimatedSprite("fixture"),Position=Who.Position+new Vector2(64,0)};Map.characters.Add(npc2);
  Check(!AfkWorld.DynamicStepClear(Map,start,outside,Who),"R12 escape cannot cross a different NPC");Map.characters.Remove(npc2);
  var target=new TileNode(Map.Name,6,5);int frames=0;var original=npc.Position;
  while(Who.GetBoundingBox().Intersects(blocker)&&frames++<80)
  {Clock(50+frames*.016);Run("RequestWalk",target,false);Who.MovePosition(Game1.currentGameTime,Game1.viewport,Map);}
  Check(frames<80&&!Who.GetBoundingBox().Intersects(blocker),"R12 actual native Farmer.MovePosition exits NPC overlap through production movement patches");
  Check(npc.Position==original&&!npc.farmerPassesThrough,"R12 NPC exit does not relocate NPC or set pass-through flag");
  Put(Who,Map,4,5);AddChest(5,5);
  Check(!AfkWorld.SegmentClear(Map,Who.StandingPixel,new(6*64+32,5*64+32),Who),"R12 overlap escape retains solid object collision");Map.objects.Clear();
  Field("NativePet",npc);Field("NativePetPermission",true);Run("Stop");
  Check(Field<NPC?>("NativePet")==null&&!Field<bool>("NativePetPermission")&&!Data.Enabled,"R12 cancellation clears transient actor reference and movement permission");
  Map.characters.Clear();Reset();
  HostTransactions=true;Who.homeLocation.Value=Home.Name;Put(Who,Home,1,3);
  Data.Enabled=true;Data.Phase=AfkPhase.ReturningHome;Field("HomeIntent",true);Field("RecoveryCount",1);Clock(100);
  var pet=new Pet{Sprite=new AnimatedSprite("fixture"),Position=new Vector2(2*64,3*64+16),currentLocation=Home};Home.characters.Add(pet);
  Check(Utility.getHomeOfFarmer(Who)==Home&&Home.GetPlayerBed()!=null,"R12 pet fixture uses owner home and actual bed");
  bool Attempt()=> (bool)typeof(AfkFishingService).GetMethod("StepActorsClear",Any)!.Invoke(Service,new object[]{new Point(2,3)})!;
  Check(Attempt()&&Field<bool>("NativePetPermission"),"R12 verified single bedside pet permits native movement after alternate-route attempt");
  Field("RecoveryCount",0);Check(!Attempt(),"R12 native pet attempt requires prior ordinary recovery");Field("RecoveryCount",1);
  Check(pet.getTimeFarmerMustPushBeforePassingThrough()==750,"R12 actual Pet method requires 750ms continuous push");
  var petStart=pet.Position;int moving=0;
  // Full Farmer.Update/Pet animation requires game Content. Simulate blocked native
  // displacement here, exercising the real production prefix/finalizer unchanged.
  var pre=typeof(AfkFishingService).GetMethod("BeforeNativeMove",Any)!;
  var post=typeof(AfkFishingService).GetMethod("AfterNativeMove",Any)!;
  for(int frame=0;frame<65;frame++)
  {Clock(100+frame*.016);Run("RequestWalk",new TileNode(Home.Name,2,3),false);var args=new object?[]{Who,false};pre.Invoke(null,args);post.Invoke(null,new object?[]{Who,args[1],null});if(Who.movementDirections.Count>0)moving++;}
  Check(moving>=60,"R12 simulated blocked displacement retains production movement intent beyond native 750ms pet threshold");
  Check(pet.Position==petStart&&!pet.farmerPassesThrough,"R12 production never relocates pet or writes pass-through flag");
  Clock(104);Check(!Attempt()&&!Field<bool>("NativePetPermission"),"R12 native pet attempt expires after three seconds");
  Run("Stop");Home.characters.Remove(pet);HostTransactions=false;Reset();
 }
}

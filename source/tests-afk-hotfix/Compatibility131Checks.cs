using BetterStardewValley.AfkFishing;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;
using System.Runtime.Serialization;
partial class Program
{
 static void Compatibility131Checks()
 {
  // Called after Morning127Checks removed the broad SafeWorld test shim.
  foreach (string flag in new[]{"paused","isTimePaused","freezeControls","pauseTime","controller"})
  {
   Reset();F8();Ready();var chest=AddChest(4,5);
   var output=ItemRegistry.Create("(O)128",3,2);Capture(output);
   int cast=BeginCalls;var position=Who.Position;var spot=Data.Spot;
   void Set(bool value)
   {
    if(flag=="controller")Who.controller=value?(PathFindController)FormatterServices.GetUninitializedObject(typeof(PathFindController)):null;
    else if(flag=="pauseTime")Game1.pauseTime=value?500:0;
    else typeof(Game1).GetField(flag,Any)!.SetValue(null,value);
   }
   Set(true);var owner=Who.controller;
   for(int i=0;i<8;i++){Clock(20+i*.1);Run("Tick");}
   Check(BeginCalls==cast&&Who.Position==position&&Data.Enabled&&ReferenceEquals(Data.Spot,spot),flag+": no new cast, movement or session cancellation");
   Check(Who.Items.Where(i=>i?.QualifiedItemId=="(O)128").Sum(i=>i.Stack)==3&&!chest.Items.Any(i=>i!=null),flag+": no inventory transaction while external owner holds control");
   Check(flag=="controller"?ReferenceEquals(owner,Who.controller):flag=="pauseTime"?Game1.pauseTime==500:(bool)typeof(Game1).GetField(flag,Any)!.GetValue(null)!,flag+": BSV does not clear another owner's flag");
   Set(false);Clock(25);Run("Tick");Clock(26);Run("Tick");
   Check(chest.Items.Where(i=>i?.QualifiedItemId=="(O)128").Sum(i=>i.Stack)==3&&Who.Items.All(i=>i?.QualifiedItemId!="(O)128"),flag+": release resumes exact once output transport");
   Run("Stop");
  }
  Reset();F8();Ready();var bound=AddChest(4,5);Run("Reconnect");
  int generation=Field<int>("Generation"),token=Field<int>("LockToken");var savedSpot=Data.Spot;
  Field("Route",new List<RouteStep>{new(new(Map.Name,5,5),new(Map.Name,6,5))});
  Run("InvalidateWorldAssets");Check(Field<bool>("WorldAssetsDirty"),"asset reload marks the session without using another screen's player");
  Check(!(bool)typeof(AfkFishingService).GetMethod("SafeTransfer",Any)!.Invoke(Service,null)!,"invalidated map immediately refuses delayed inventory work before the next update");
  Run("RefreshWorldAssets");
  Check(Field<List<RouteStep>?>("Route")==null&&Field<StardewValley.Objects.Chest?>("Chest")==null&&Field<int>("Generation")==generation+1&&Field<int>("LockToken")==token+1,
   "asset reload cancels stale path, chest and delayed transaction generation");
  Check(Data.Enabled&&ReferenceEquals(savedSpot,Data.Spot)&&Map.objects.Values.Contains(bound),"asset invalidation retains saved fishing identity and actual world inventories");
  Run("RefreshWorldAssets");Check(Field<int>("Generation")==generation+1,"unchanged next frame cannot rebuild route again");
  Run("Stop");
 }
}

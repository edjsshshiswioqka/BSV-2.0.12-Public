using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
namespace BetterStardewValley;
internal static partial class Program
{
 const string Marker="Pathoschild.TractorMod";
 static string Snapshot(World w)=>JsonSerializer.Serialize(new {
  horse=w.HA.HorseId,name=w.HA.Name,display=w.HA.displayName,owner=w.HA.ownerId.Value,
  x=w.HA.Position.X,y=w.HA.Position.Y,w.HA.Frozen,frame=w.HA.Sprite.CurrentFrame,w.HA.flip,
  dx=w.HA.drawOffset.X,dy=w.HA.drawOffset.Y,horseData=w.HA.modData.OrderBy(p=>p.Key),
  stableOwner=w.SA.owner.Value,stableData=w.SA.modData.OrderBy(p=>p.Key),rider=w.HA.rider?.UniqueMultiplayerID});
 static void TractorCases()
 {
  Case("R14 official marker only; names, appearances and empty marker values",()=>{
   var w=new World();Check(!HorseFeature.Real(null)&&!HorseFeature.Tractor(null),"null excluded");
   foreach(string name in new[]{"Tractor","tractor/old-id","Maple","拖拉机",""}){
    w.HA.Name=name;Check(HorseFeature.Real(w.HA),"unmarked real horse name accepted");
    foreach(string value in new[]{"","0","1","anything"}){w.HA.modData[Marker]=value;Check(!HorseFeature.Real(w.HA),"marker presence excludes independent of value/name");}
    w.HA.modData.Remove(Marker);
   }
  });
  Case("R14 only tractors and mixed construction order",()=>{
   foreach(bool first in new[]{false,true}){
    var w=new World();w.HA.modData[Marker]="1";w.SB.owner.Value=w.HB.ownerId.Value=101;
    if(first)w.Farm.buildings.Reverse();Check(w.Mod.Candidates(101).SequenceEqual(new[]{w.HB}),"only real horse candidate");
    var old=Snapshot(w);w.Helper.Events.Input.Raise(SButton.H);w.Helper.Events.GameLoop.Tick();Check(w.Mod.Running==1&&Snapshot(w)==old,"H selects real horse without touching tractor");
    w.Helper.Events.GameLoop.Save();w.HB.modData[Marker]="1";w.Mod.ResetInput();Game1.Trace.Clear();w.Helper.Events.Input.Raise(SButton.H);
    Check(Game1.Trace.Last()=="Message:horse.call.no_horse"&&w.Mod.Running==0,"tractor-only no horse feedback");
   }
  });
  Case("R14 tractor actions, ownership, empty garage and demolition pass through",()=>{
   var w=new World();w.HA.modData[Marker]="";w.HA.modData[HorseFeature.RunningKey]="legacy";w.SA.modData[HorseFeature.PersistentNameKey]="old pollution";w.HA.rider=w.B;w.B.mount=w.HA;
   var old=Snapshot(w);Check(HorseFeature.ActionUntouched(w.HA,w.A),"checkAction original allowed without changing result");
   foreach(bool host in new[]{false,true}){
    Context.IsMainPlayer=host;Check(HorseFeature.OwnershipPrefix(w.SA)&&HorseFeature.GrabPrefix(w.SA),"tractor native lifecycle allowed host/client");
    HorseFeature.Recovery(w.SA);HorseFeature.OwnershipPostfix(w.SA);HorseFeature.DemolishPrefix(w.SA);w.Mod.RemoveStable(w.SA);w.Mod.DirectName(w.SA,w.HA,w.A);
    Check(Snapshot(w)==old,"all lifecycle hooks read-only for tractor");
   }
   Context.IsMainPlayer=true;w.Farm.characters.Remove(w.HA);w.SA.buildingType.Value="Pathoschild.TractorMod_Stable";
   HorseFeature.Recovery(w.SA);Check(HorseFeature.GrabPrefix(w.SA)&&HorseFeature.OwnershipPrefix(w.SA),"empty official garage native lifecycle allowed");
   Check(Snapshot(w)==old,"empty garage not mutated");
   w.SA.buildingType.Value="Stable";HorseFeature.Recovery(w.SA);Check(Snapshot(w)==old,"unknown empty stable not rewritten");
   Context.IsMainPlayer=false;Check(!HorseFeature.GrabPrefix(w.SA)&&!HorseFeature.OwnershipPrefix(w.SA),"ordinary empty stable still host-authoritative; no client-spawn regression");Context.IsMainPlayer=true;
  });
  Case("R14 host denies tractor Call Name Claim Select and forged ownership",()=>{
   foreach(string action in new[]{"Call","Name","Claim","Select"}){
    var w=new World();w.HA.modData[Marker]="1";w.HA.Position=w.A.Position;var old=Snapshot(w);
    w.Mod.Handle(w.Request(w.A,w.HA,action,"bad"));Check(Snapshot(w)==old&&w.Mod.Running==0&&!w.A.modData.ContainsKey(HorseFeature.SelectedNameKey),action+" tractor host rejected");
    w=new World();w.Mod.Handle(w.Request(w.B,w.HA,action,"bad"));Check(w.HA.ownerId.Value==101&&!w.B.modData.ContainsKey(HorseFeature.SelectedNameKey),action+" non-owner rejected");
   }
  });
  Case("R14 select persists before summon and across day load map reconnect",()=>{
   var w=new World();w.SB.owner.Value=w.HB.ownerId.Value=101;w.HB.Position=w.A.Position;
   w.Mod.Handle(w.Request(w.A,w.HB));string chosen=w.HB.HorseId.ToString("N");
   Check(w.Mod.Running==0&&w.A.modData[HorseFeature.SelectedNameKey]==chosen,"already-here still saves authorized preference");
   w.Cross();w.Helper.Events.GameLoop.Day();w.Helper.Events.GameLoop.Load();w.Helper.Events.Multiplayer.Disconnect(101);w.Helper.Events.GameLoop.Load();
   Check(w.A.modData[HorseFeature.SelectedNameKey]==chosen,"preference survives lifecycle");
   w.Farm.characters.Remove(w.HB);w.Helper.Events.GameLoop.Day();Check(w.A.modData[HorseFeature.SelectedNameKey]==chosen,"temporary horse absence does not reset valid stable selection");
   w.Farm.characters.Add(w.HB);w.Farm.buildings.Remove(w.SB);w.Helper.Events.GameLoop.Load();Check(!w.A.modData.ContainsKey(HorseFeature.SelectedNameKey),"deleted stable selection cleared");
  });
  Case("R14 explicit choose works indoors/mounted and host validates menu staleness",()=>{
   var w=new World();w.SB.owner.Value=w.HB.ownerId.Value=101;w.Farm.IsOutdoors=false;w.A.mount=w.HA;
   w.Helper.ConsoleCommands.Items["bsv_horse_choose"]("",Array.Empty<string>());
   Check(w.Farm.Question!.Value.Responses.Length==3,"two real horses plus cancel");
   w.Farm.Question.Value.Callback(w.A,w.HB.HorseId.ToString("N"));w.Helper.Events.GameLoop.Tick();
   Check(w.Mod.Running==0&&w.A.modData[HorseFeature.SelectedNameKey]==w.HB.HorseId.ToString("N"),"choice saved without movement");
   w=new World();w.SB.owner.Value=w.HB.ownerId.Value=101;w.Helper.ConsoleCommands.Items["bsv_horse_choose"]("",Array.Empty<string>());
   w.Farm.Question!.Value.Callback(w.A,w.HB.HorseId.ToString("N"));w.HB.modData[Marker]="1";w.Helper.Events.GameLoop.Tick();
   Check(!w.A.modData.ContainsKey(HorseFeature.SelectedNameKey)&&w.Mod.Running==0,"queued target revalidated after marker arrives");
  });
  Case("R14 invalid selection cleanup and audit never repair tractor metadata",()=>{
   var w=new World();w.HA.modData[Marker]="1";w.HA.modData[HorseFeature.RunningKey]="old";w.SA.modData[HorseFeature.PersistentNameKey]="legacy";
   w.A.modData[HorseFeature.SelectedNameKey]=w.HA.HorseId.ToString("N");w.A.modData["other"]="keep";var old=Snapshot(w);
   w.Helper.Events.GameLoop.Load();w.Helper.Events.GameLoop.Day();w.Helper.ConsoleCommands.Items["bsv_horse_status"]("",Array.Empty<string>());
   Check(Snapshot(w)==old&&!w.A.modData.ContainsKey(HorseFeature.SelectedNameKey)&&w.A.modData["other"]=="keep","only invalid selection changed, idempotent");
  });
  Case("R14 animation rendering and speed leave tractors untouched",()=>{
   var w=new World();w.HA.modData[Marker]="1";w.HA.modData[HorseFeature.RunningKey]="legacy";w.HA.Position=w.A.Position;var old=Snapshot(w);
   HorseFeature.Animate(w.HA);var draw=new RenderedWorldEventArgs();w.Helper.Events.Display.Draw(draw);
   Check(draw.SpriteBatch.Draws==0&&Snapshot(w)==old,"no tractor UI or animation edits");
   w.A.mount=w.HA;object[] args={w.A,17f};typeof(HorseFeature).GetMethod("AfterMovementSpeed",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
   Check((float)args[1]==17f,"native tractor speed preserved");
  });
  Case("R14 constructor and late-marker controller termination are mutation-free",()=>{
   var w=new World();w.HA.modData[Marker]="1";var old=Snapshot(w);bool rejected=false;
   try{w.Mod.Construct(w.HA,w.SA,w.A);}catch(InvalidOperationException){rejected=true;}
   Check(rejected&&Snapshot(w)==old,"constructor rejects before freeze Halt or RunKey write");
   foreach(bool save in new[]{false,true}){
    w=new World();w.Mod.Handle(w.Request(w.A,w.HA));Check(w.Mod.Running==1,"real horse starts");w.HA.modData[Marker]="1";old=Snapshot(w);
    if(save)w.Helper.Events.GameLoop.Save();else w.Mod.MovementStep();
    Check(w.Mod.Running==0&&Snapshot(w)==old&&Game1.Warps.Count==0,"late marker drops only in-memory state");
   }
  });
 }
}

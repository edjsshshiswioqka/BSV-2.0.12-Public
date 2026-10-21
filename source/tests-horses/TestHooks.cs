// Hooks exist only in the test compilation. Production logic is compiled verbatim.
using System;using System.Linq;using Microsoft.Xna.Framework;using StardewModdingAPI;using StardewValley;using StardewValley.Buildings;using StardewValley.Characters;
namespace BetterStardewValley;
internal sealed partial class HorseFeature {
 internal static bool Tractor(Horse? h)=>IsTractorHorse(h);
 internal static bool Real(Horse? h)=>IsRealManagedHorse(h);
 internal static bool ActionUntouched(Horse h,Farmer f){bool result=false;bool run=BeforeAction(h,f,ref result);return run&&!result;}
 internal static bool OwnershipPrefix(Stable s)=>BeforeOwnership(s);
 internal static bool GrabPrefix(Stable s)=>BeforeGrab(s);
 internal static void DemolishPrefix(Stable s)=>BeforeDemolition(s);
 internal static void OwnershipPostfix(Stable s)=>AfterOwnership(s);
 internal void DirectName(Stable s,Horse h,Farmer f)=>SaveName(s,h,f,"changed");
 internal void Construct(Horse h,Stable s,Farmer f)=>_ = new Summon(new HorseRequest(),h,f.currentLocation,s,Now);
 internal List<Horse> Candidates(long id)=>OwnedHorses(id);
 internal static bool ActionCheck(Horse h,Farmer f){bool result=false;return BeforeAction(h,f,ref result);}
 internal static void Recovery(Stable s){Recover(s);ApplyName(s);}
 internal void InspectView(Farmer p,int x,int y,int w=640,int h=480)=>RememberView(p.UniqueMultiplayerID,new(){Actor=p.UniqueMultiplayerID,Location=p.currentLocation.NameOrUniqueName,X=x,Y=y,Width=w,Height=h});
 internal void Handle(HorseRequest r)=>Process(r);
 internal int Running=>Active.Count;
 internal HorseReceipt? Last(long actor)=>Replies.GetValueOrDefault(actor);
 internal void MovementStep(){foreach(var s in Active.Values.ToArray()){s.LastTick=Now-1.0/60;if(!s.Started)s.Start=Now-.5;Advance(s,Now);}}
 internal void Replan(){foreach(var s in Active.Values){s.NextPlan=0;s.LastTick=Now-1.0/60;}}
 internal void Expire(){foreach(var s in Active.Values.ToArray()){s.Start=Now-41;Advance(s,Now);}}
 internal void TimeoutBlock(){foreach(var s in Active.Values){s.BlockedSince=Now-3;s.NextPlan=Now+1;}}
 internal void ResetInput()=>ResetLocal();
 internal static void Animate(Horse h)=>AfterHorseUpdate(h,new(){ElapsedGameTime=TimeSpan.FromMilliseconds(70)});
 internal static bool CheckSafe(GameLocation l,Horse h,Vector2 p)=>Safe(l,h,p);
 internal static bool CanCall(GameLocation l)=>Allowed(l);
 internal static string PersistentNameKey=>NameKey;
 internal static string SelectedNameKey=>SelectedKey;
 internal static string RunningKey=>RunKey;
 internal void RemoveStable(Stable s)=>CleanupStable(s);
}

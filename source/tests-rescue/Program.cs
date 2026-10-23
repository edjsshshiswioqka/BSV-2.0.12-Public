using BetterStardewValley.MultiplayerRescue;

int count = 0;
var progressLog = new List<string>{"fps,frame,elapsedSeconds,productionProgress"};
void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); count++; Console.WriteLine("PASS " + name); }
string Id() => Guid.NewGuid().ToString("N");
RescueActor Actor(long id, double x=0) => new() { Id=id,Location="Mine/custom-42",X=x,Y=100,Available=true };
var target=Actor(1); var a=Actor(2,64); var b=Actor(3,32);
var actors=new Dictionary<long,RescueActor>{{1,target},{2,a},{3,b}};
RescueActor? Lookup(long id)=>actors.GetValueOrDefault(id);
var m=new RescueModel(); string ev=Id(), attempt=Id();
Check(m.Down(target,ev,100),"fatal event accepted");
Check(!m.Down(target,ev,101),"duplicate lethal event rejected");
Check(m.Find(1)!.Deadline==160,"60 real seconds fixed at first event");
Check(!m.Begin(target,1,ev,Id(),101),"self rescue rejected");
a.Location="OtherMap";Check(!m.Begin(a,1,ev,Id(),101),"cross-map request rejected");a.Location=target.Location;
a.X=64.01;Check(!m.Begin(a,1,ev,Id(),101),"outside 64px rejected");a.X=64;
Check(!m.Begin(a,1,"old",Id(),101),"old event request rejected");
Check(m.Begin(a,1,ev,attempt,101),"64px boundary and single click accepted");
Check(!m.Begin(b,1,ev,Id(),101),"two rescuers cannot combine progress");
Check(!m.Begin(a,1,ev,attempt,101),"duplicate attempt rejected");
m.Tick(105.999,Lookup,false);Check(m.IsDown(1),"4.999 seconds never completes");
m.Tick(106,Lookup,false);Check(m.Find(1)!.Outcome==RescueOutcome.ReviveAuthorized,"5.000 seconds authorizes but does not claim owner applied");
Check(!m.ConfirmRevived(2,ev,attempt,106),"rescuer cannot confirm victim applied");
Check(!m.ConfirmRevived(1,ev,Id(),106),"wrong authorization attempt rejected");
Check(m.ConfirmRevived(1,ev,attempt,106),"target acknowledges exact authorization");
long revision=m.Revision;m.Tick(200,Lookup,false);Check(m.Revision==revision,"terminal state never settles twice");
Check(!m.GiveUp(1,ev,200),"late give-up cannot reverse actually acknowledged revive");
Check(RescueModel.RestoredHealth(101)==11 && RescueModel.RestoredHealth(100)==10 && RescueModel.RestoredHealth(1)==1,"ceil(maxHP*10%), minimum one");
{ // a replicated position that settles a few pixels after the hit, and a key held by the downed player, must not block rescue
 var t0=Actor(1);var r0=Actor(2,32);actors[1]=t0;actors[2]=r0;var m0=new RescueModel();var e0=Id();
 m0.Down(t0,e0,0);t0.X+=6;t0.Available=false;var now0=new RescueActor{Id=1,Location=t0.Location,X=t0.X,Y=t0.Y};
 Check(m0.Begin(r0,1,e0,Id(),1,now0),"rescue measured from the downed player's current position");
 r0.X+=3;m0.Tick(6.5,Lookup,false);Check(m0.Find(1)!.Outcome==RescueOutcome.ReviveAuthorized,"small drift and a held key do not cancel the rescue");
}
foreach (string mode in new[]{"move","creep","damage","menu","warp","disconnect","target-warp","target-move"})
{
 target=Actor(1);a=Actor(2,32);actors[1]=target;actors[2]=a;m=new();ev=Id();attempt=Id();
 m.Down(target,ev,0);m.Begin(a,1,ev,attempt,1);
 switch(mode){case "move":a.X+=20;break;case "creep":for(int n=0;n<25;n++)a.X+=1;break;case "damage":a.DamageSerial++;break;
 case "menu":a.Available=false;break;case "warp":a.Location="Farm";break;case "disconnect":actors.Remove(2);break;
 case "target-warp":target.Location="Farm";break;case "target-move":target.X+=20;break;}
 m.Tick(5.5,Lookup,false);
 Check(m.Find(1)!.Rescuer==0 || m.Find(1)!.Outcome==RescueOutcome.VanillaDeath,mode+" interrupts");
 Check(m.Find(1)!.Deadline==60,mode+" cannot extend deadline");
}
target=Actor(1);a=Actor(2,32);actors[1]=target;actors[2]=a;m=new();ev=Id();attempt=Id();
m.Down(target,ev,0);m.Begin(a,1,ev,attempt,1);m.Cancel(2,ev,attempt,"input");
Check(!m.Begin(a,1,ev,attempt,6),"canceled attempt ID cannot replay");
Check(m.Begin(a,1,ev,Id(),6),"fresh attempt allowed");m.Tick(10.999,Lookup,false);Check(m.IsDown(1),"fresh attempt starts at zero");
m.Tick(11,Lookup,false);Check(m.Find(1)!.Outcome==RescueOutcome.ReviveAuthorized,"fresh attempt requires full five seconds before authorization");
Check(m.GiveUp(1,ev,11.01)&&m.Find(1)!.Outcome==RescueOutcome.VanillaDeath,"give-up wins after host authorization but before target application");
Check(!m.GiveUp(1,ev,11.02),"duplicate termination has no new side effect");
m=new();ev=Id();Check(m.TerminateUnknown(1,ev,0),"give-up before delayed down creates tombstone");
Check(!m.Down(target,ev,1),"delayed down cannot restart terminated event");
Check(!m.TerminateUnknown(1,ev,2),"unknown-event termination replay rejected");
m=new();ev=Id();attempt=Id();m.Down(target,ev,0);m.Begin(a,1,ev,attempt,1);m.Tick(6,Lookup,false);m.Tick(60,Lookup,false);
Check(m.Find(1)!.Outcome==RescueOutcome.VanillaDeath,"unapplied authorization still expires at original host deadline");
Check(m.ConfirmRevived(1,ev,attempt,61),"delayed receipt for already applied exact authorization reconciles host timeout");
Check(!m.GiveUp(1,ev,62),"reconciled applied result cannot kill healthy owner");
foreach(bool night in new[]{false,true}){
 m=new();ev=Id();m.Down(target,ev,0);m.Begin(a,1,ev,Id(),55);m.Tick(60,Lookup,night);
 Check(m.Find(1)!.Outcome==(night?RescueOutcome.Night:RescueOutcome.VanillaDeath),night?"2AM wins over completion":"exact 60s tie expires");
}
m=new();ev=Id();m.Down(target,ev,0);m.Begin(a,1,ev,Id(),1);m.Disconnect(2,3);
Check(m.IsDown(1)&&m.Find(1)!.Rescuer==0,"rescuer disconnect only cancels attempt");m.Disconnect(1,4);
Check(m.Find(1)!.Outcome==RescueOutcome.VanillaDeath,"victim disconnect records death obligation");
m=new();ev=Id();m.Down(target,ev,0);Check(!m.GiveUp(2,ev,1),"rescuer cannot give up victim");Check(m.GiveUp(1,ev,1),"victim can give up");
foreach(int fps in new[]{30,60,120}){
 double last=-1;int changes=0;
 for(int frame=0;frame<=fps*5;frame++){
  double p=RescueModel.Progress(100+(double)frame/fps,100);
  progressLog.Add(FormattableString.Invariant($"{fps},{frame},{(double)frame/fps:F9},{p:F9}"));
  Check(p>=last && p>=0 && p<=1,$"{fps}FPS frame {frame} monotonic/bounded");
  if(p>last)changes++;last=p;
 }
 Check(last==1&&changes==fps*5+1,$"{fps}FPS linear real-time completion");
}
var clock=new RescueClock();clock.Observe(100,20,20.1);double t=clock.Estimate(21,100);
clock.Observe(99,20.99,21);Check(clock.Estimate(21,99)>=t,"clock correction never runs backwards");
File.WriteAllLines(args.Length>0?args[0]:"rescue-model-progress.csv",progressLog);
Console.WriteLine($"TOTAL {count} PASS (pure production model; NOT real multiplayer)");

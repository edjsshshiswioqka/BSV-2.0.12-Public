using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using Microsoft.Xna.Framework;

// Actual BSV service and native Farmer/net fields; graphics, networking and clock
// are explicit fixtures. This is not an actual death/penalty or multiplayer run.
internal static class SafetyChecks
{
    const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    const string Prefix="Eddie.BetterStardewValley/";
    static Farmer Player=null!;
    static double Time;
    static bool Ready,IsHost=true;
    static bool FailSend,TerminationBeforeSend;
    static void Outgoing(object __instance)
    {
        if(!FailSend)return;
        TerminationBeforeSend=Get<bool>(__instance,"Released")&&Get<string>(__instance,"TerminatedEvent")==Get<string>(__instance,"ActiveEvent");
        throw new IOException("injected transport failure");
    }
    static bool Skip()=>false;
    static bool Yes(ref bool __result){__result=true;return false;}
    static bool No(ref bool __result){__result=false;return false;}
    static bool World(ref bool __result){__result=Ready;return false;}
    static bool Host(ref bool __result){__result=IsHost;return false;}
    static bool Clock(ref double __result){__result=Time;return false;}
    static bool Farmer(ref Farmer __result){__result=Player;return false;}
    static bool Date(ref WorldDate __result){__result=new WorldDate(1,"spring",1);return false;}
    static void Set(object o,string field,object value)=>o.GetType().GetField(field,Any)!.SetValue(o,value);
    static T Get<T>(object o,string field)=>(T)o.GetType().GetField(field,Any)!.GetValue(o)!;
    static object? Call(object o,string method,params object[] args)=>o.GetType().GetMethod(method,Any)!.Invoke(o,args);
    static object Blank(Type t){var o=FormatterServices.GetUninitializedObject(t);GC.SuppressFinalize(o);return o;}
    static object Make(Type t,params object[] args)=>Activator.CreateInstance(t,Any,null,args,null)!;
    public static void Prepare(Type service)
    {
        var h=new Harmony("BSV.Rescue.Safety.Fixtures");
        void Patch(Type t,string name,string callback,params Type[] args)=>h.Patch(t.GetMethod(name,Any,null,args,null)!,prefix:new(typeof(SafetyChecks),callback));
        Patch(typeof(Context),"get_IsWorldReady",nameof(World));Patch(typeof(Context),"get_IsMainPlayer",nameof(Host));
        Patch(typeof(Context),"get_IsMultiplayer",nameof(Yes));Patch(typeof(Context),"get_IsSplitScreen",nameof(No));
        Patch(typeof(Farmer),"get_IsLocalPlayer",nameof(Yes));Patch(typeof(Game1),"get_MasterPlayer",nameof(Farmer));
        Patch(typeof(Game1),"GetPlayer",nameof(Farmer),typeof(long),typeof(bool));Patch(typeof(Game1),"get_Date",nameof(Date));
        Patch(typeof(Farmer),"farmerInit",nameof(Skip));Patch(typeof(AnimatedSprite),"LoadTexture",nameof(Skip),typeof(string),typeof(bool));
        Patch(typeof(Game1),"addHUDMessage",nameof(Skip),typeof(HUDMessage));
        Patch(typeof(StardewValley.BellsAndWhistles.PlayerStatusList),"AddSpriteDefinition",nameof(Skip),typeof(string),typeof(string),typeof(int),typeof(int),typeof(int),typeof(int));
        h.Patch(service.GetProperty("Now",Any)!.GetMethod!,prefix:new(typeof(SafetyChecks),nameof(Clock)));
        h.Patch(service.GetMethod("Send",Any)!,prefix:new(typeof(SafetyChecks),nameof(Outgoing)));
        Game1.game1=(Game1)Blank(typeof(Game1));Game1.game1._locations=new List<GameLocation>();Game1.otherFarmers=new();Game1._locationLookup=new();
        Game1.random=new Random(150);Game1.options=(Options)Blank(typeof(Options));Game1.uiViewport=new(0,0,1280,720);
        Game1.netWorldState=new Netcode.NetRoot<StardewValley.Network.NetWorldState>(new StardewValley.Network.NetWorldState());
        Player=new Farmer();Player.UniqueMultiplayerID=42;Player.maxHealth=100;
        typeof(Game1).GetField("_player",Any)!.SetValue(null,Player);
        Player.currentLocation=new GameLocation();Game1.currentLocation=Player.currentLocation;
    }
    public static void Run(object service,Action<bool,string> check)
    {
        var assembly=service.GetType().Assembly;
        var actorType=assembly.GetType("BetterStardewValley.MultiplayerRescue.RescueActor")!;
        var snapshotType=assembly.GetType("BetterStardewValley.MultiplayerRescue.RescueSnapshot")!;
        var commandType=assembly.GetType("BetterStardewValley.MultiplayerRescue.RescueCommand")!;
        string epoch=Guid.NewGuid().ToString("N");
        object Json(Type t,object data)=>Newtonsoft.Json.JsonConvert.DeserializeObject(Newtonsoft.Json.JsonConvert.SerializeObject(data),t)!;
        object Actor(long id)=>Json(actorType,new{Id=id,Location="fixture",X=0,Y=0,Available=true,Alive=true});
        string ev="",attempt="";
        object Begin(bool authorize=false)
        {
            Ready=true;IsHost=true;Time=100;Game1.timeOfDay=1200;Game1.newDay=Game1.killScreen=Game1.eventUp=false;
            Player.health=0;Player.passedOut=false;Player.modData.Clear();
            Call(service,"Reset");Set(service,"Healthy",true);Set(service,"Epoch",epoch);
            ev=Guid.NewGuid().ToString("N");attempt=Guid.NewGuid().ToString("N");
            Set(service,"ActiveEvent",ev);Set(service,"Pending",ev);Set(service,"PendingAt",100d);Set(service,"LocalDeadline",160d);
            Player.modData[Prefix+"RescuePending.v1"]=ev;
            var model=Get<object>(service,"Model");Call(model,"Down",Actor(42),ev,100d);
            Call(service,"Publish",null!,0d);
            check(Get<string>(service,"Pending")=="","host-confirmed down clears only Pending, not local event/deadline");
            if(authorize)
            {
                Call(model,"Begin",Actor(43),42L,ev,attempt,101d);
                var record=Call(model,"Find",42L)!;
                record.GetType().GetProperty("Outcome")!.SetValue(record,Enum.ToObject(record.GetType().GetProperty("Outcome")!.PropertyType,4));
                record.GetType().GetProperty("AuthorizedAt")!.SetValue(record,106d);
                Time=106;
            }
            return model;
        }
        object Snapshot(object model,int sequence)=>Json(snapshotType,new{Protocol=2,Epoch=epoch,Sequence=sequence,HostNow=Time,
            Records=new[]{Call(model,"Find",42L)}});
        void Key()
        {
            var ctor=typeof(ButtonPressedEventArgs).GetConstructors(Any).Single();
            var args=ctor.GetParameters().Select(p=>p.ParameterType==typeof(SButton)?(object)SButton.K:
                p.ParameterType==typeof(Func<SButton,bool>)?new Func<SButton,bool>(_=>false):
                p.ParameterType.IsInterface?Recorder.Make(p.ParameterType):null).ToArray();
            Call(service,"Input",null!,ctor.Invoke(args));
        }
        var model=Begin(true);var late=Snapshot(model,100);
        Key();check(Get<bool>(service,"Released")&&Get<string>(service,"TerminatedEvent")==ev,"actual Input K latches local termination before synchronous host Receive");
        check(Call(model,"Find",42L)!.GetType().GetProperty("Outcome")!.GetValue(Call(model,"Find",42L))!.ToString()=="VanillaDeath","unapplied host authorization becomes native verdict");
        Call(service,"Apply",late);check(Player.health==0&&Get<bool>(service,"Released"),"late authorized snapshot never heals a locally terminated event");
        var guard=service.GetType().GetMethod("DeathHealth",Any)!;
        check((int)guard.Invoke(null,new[]{Player})! == 0,"actual injury guard hands original zero health to native Update");
        Player.health=31;Call(service,"ReleaseToNative","duplicate",false);check(Player.health==31,"duplicate release never kills an already recovered farmer");

        model=Begin(true);Call(service,"Apply",Snapshot(model,100));
        check(Player.health==10&&Player.modData[Prefix+"RescueReceipt.v1"]==ev,"authorized target applies ceil10percent once and stores receipt");
        Player.health=7;Call(service,"Apply",Snapshot(model,101));Key();
        check(Player.health==7,"replayed authorization and late input cannot heal or kill applied target");

        model=Begin();Time=159.999;Call(service,"RecoveryTick",false);
        check(!Get<bool>(service,"Released"),"local deadline 59.999 seconds remains waiting");
        Time=160;Call(service,"RecoveryTick",false);
        check(Get<bool>(service,"Released")&&Player.health==0,"local 60 second deadline releases independently of host progression/heartbeat");
        Call(service,"Apply",Snapshot(model,200));check(Get<bool>(service,"Released"),"fresh-sequence down snapshot cannot renew expired event");

        Begin();Call(service,"Safe",new Action(()=>throw new InvalidOperationException("injected render/send fault")));
        check(!Get<bool>(service,"Healthy")&&Get<bool>(service,"Released"),"production Safe fault opens native guards without deleting debt");
        Game1.killScreen=true;Call(service,"Tick");check(Get<bool>(service,"NativeStarted"),"native-start observation still runs when Healthy=false");
        Game1.killScreen=false;Player.health=20;Call(service,"Tick");
        check(!Player.modData.ContainsKey(Prefix+"RescuePending.v1"),"native-completed observation and receipt still run after module fault (fixture flags, not real penalty)");

        Begin();Player.health=40;Call(service,"Load");
        check(Player.health==40&&!Player.modData.ContainsKey(Prefix+"RescuePending.v1"),"healthy loaded farmer with stale debt is not killed or healed");
        Begin();Time=300;Call(service,"RecoveryTick",false);check(Get<bool>(service,"Released"),"first resumed callback after over60 second stall expires without renewed wait");
        Begin();Game1.timeOfDay=2600;Call(service,"RecoveryTick",false);
        check(Get<bool>(service,"Released")&&Player.health==0,"2AM opens native guard without extra HP or penalty mutation");
        Begin();FailSend=true;try{Key();}finally{FailSend=false;}
        check(TerminationBeforeSend&&!Get<bool>(service,"Healthy")&&Get<bool>(service,"Released")&&Player.health==0,
            "actual failing Send after Give Up cannot roll back termination or retain control");
        Time=102;Call(service,"RecoveryTick",true);
        check(Call(Get<object>(service,"Model"),"Find",42L)!.GetType().GetProperty("Outcome")!.GetValue(Call(Get<object>(service,"Model"),"Find",42L))!.ToString()=="VanillaDeath",
            "minimum recovery retries termination even when full rescue service is unhealthy");
        model=Begin(true);
        var record=Call(model,"Find",42L)!;
        Player.modData[Prefix+"RescueLedger.v1"]=Newtonsoft.Json.JsonConvert.SerializeObject(new Dictionary<long,object>{{42,record}});
        Player.modData.Remove(Prefix+"RescuePending.v1");Player.modData[Prefix+"RescueReceipt.v1"]=ev;
        Player.modData[Prefix+"RescueReceiptOutcome.v2"]="revived:"+attempt;Player.health=23;
        Call(service,"Load");Call(service,"RecoveryTick",true);
        check(Get<System.Collections.IDictionary>(service,"Ledger").Count==0&&Player.health==23,
            "saved matching application receipt reconciles host reload without replaying HP or penalties");
        Ready=false;
    }
}

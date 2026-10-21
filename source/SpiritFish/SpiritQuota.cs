using StardewModdingAPI;
using System.Text.Json;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Tools;
using Eddie.FishingMachine;
namespace BetterStardewValley.SpiritFish;

// A manual/AFK attempt obtains the SAME host ledger entry used by machine batches.
// No input or item is transferred through this protocol. Unacknowledged clients simply cannot roll Spirit.
internal sealed class SpiritQuota
{
    private const string ModId="Eddie.BetterStardewValley", Channel="SpiritFish.Lease", SavedToken=ModId+"/SpiritFish/ManualLease";
    internal sealed class Packet { public string Action {get;set;}=""; public string Token {get;set;}=""; public int Year {get;set;} public bool Caught {get;set;} }
    private const string PendingRelease=ModId+"/SpiritFish/ReleasePending";
    private sealed class Local {internal string Token="";internal int Year;internal bool Granted;internal long NextTick;internal long ReleaseRetry;}
    private readonly PerScreen<Local> State=new(()=>new());
    private readonly IModHelper Helper;private readonly IMonitor Monitor;
    private readonly HashSet<string> LoggedErrors=new();
    internal static SpiritQuota? Instance;
    internal SpiritQuota(IModHelper helper,IMonitor monitor)
    {
        Instance=this;Helper=helper;Monitor=monitor;
        helper.Events.GameLoop.UpdateTicked+=(_,e)=>{if(e.IsMultipleOf(15))Safe(()=>Tick((long)e.Ticks));};
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>Safe(Recover);
        helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>{State.ResetAllScreens();LoggedErrors.Clear();};
        helper.Events.Multiplayer.ModMessageReceived+=(_,e)=>
        {
            if(e.FromModID!=ModId||!Context.IsWorldReady||e.Type!=Channel)return;
            Safe(()=>{var p=e.ReadAs<Packet>();
                if(Context.IsMainPlayer)Receive(e.FromPlayerID,p);
                else if(e.FromPlayerID==Game1.MasterPlayer.UniqueMultiplayerID) {if(p.Action=="grant")Grant(p);else if(p.Action=="ack")Ack(p);}
            });
        };
    }
    private void Safe(Action action){try{action();}catch(Exception ex){if(LoggedErrors.Add(ex.GetType().FullName+":"+ex.Message))Monitor.Log("Spirit Fish quota operation stopped safely: "+ex,LogLevel.Error);}}
    internal static bool CanCatch(Farmer farmer)=>Instance!=null&&farmer.IsLocalPlayer&&Instance.State.Value.Granted&&Instance.State.Value.Year==Game1.year;
    private void Tick(long tick)
    {
        if(!Context.IsWorldReady)return;
        var who=Game1.player;var s=State.Value;
        if(tick>=s.ReleaseRetry && who.modData.TryGetValue(PendingRelease,out var pending))
        {s.ReleaseRetry=tick+120;Send(JsonSerializer.Deserialize<Packet>(pending)!);}
        bool busy=who.CurrentTool is FishingRod r&&(who.UsingTool||r.isFishing||r.isReeling||r.pullingOutOfWater||r.fishCaught||r.hit);
        if(!busy){if(s.Token!="")Release(false);return;}
        // A new attempt must not overwrite an unacknowledged successful settlement.
        if(who.modData.ContainsKey(PendingRelease))return;
        if(s.Granted||tick<s.NextTick||!SpiritService.Eligible(who,who.currentLocation)||!SpiritSelection.HasBait(who))return;
        s.NextTick=tick+120;
        if(s.Token=="")
        {
            s.Token="manual:"+Guid.NewGuid().ToString("N");s.Year=Game1.year;
            // Persist before sending, including the grant-lost/disconnect boundary.
            who.modData[SavedToken]=s.Year+"/"+s.Token;
        }
        Send(new(){Action="request",Token=s.Token,Year=s.Year});
    }
    private void Send(Packet packet)
    {
        if(Context.IsMainPlayer)Receive(Game1.player.UniqueMultiplayerID,packet);
        else Helper.Multiplayer.SendMessage(packet,Channel,new[]{ModId},new[]{Game1.MasterPlayer.UniqueMultiplayerID});
    }
    private void Receive(long sender,Packet p)
    {
        if(!Context.IsMainPlayer||p.Token.Length!=39||!p.Token.StartsWith("manual:",StringComparison.Ordinal)||p.Year<1)return;
        var who=Game1.GetPlayer(sender,onlyOnline:false);if(who==null)return;
        if(p.Action=="request")
        {
            if(p.Year!=Game1.year||!SpiritService.Eligible(who,who.currentLocation)||!SpiritSelection.HasBait(who)||!MachineLegendaryBridge.ReserveManual(who,p.Year,p.Token))return;
            var grant=new Packet{Action="grant",Token=p.Token,Year=p.Year};
            if(who.IsLocalPlayer)Grant(grant);else Helper.Multiplayer.SendMessage(grant,Channel,new[]{ModId},new[]{sender});
        }
        else if(p.Action=="release")
        {
            MachineLegendaryBridge.ReleaseManual(who,p.Year,p.Token,p.Caught);
            var ack=new Packet{Action="ack",Token=p.Token,Year=p.Year};
            if(who.IsLocalPlayer)Ack(ack);else Helper.Multiplayer.SendMessage(ack,Channel,new[]{ModId},new[]{sender});
        }
    }
    private void Grant(Packet p)
    {
        var s=State.Value;
        if(p.Token!=s.Token||p.Year!=s.Year){Send(new(){Action="release",Token=p.Token,Year=p.Year});return;}
        s.Granted=true;Game1.player.modData[SavedToken]=p.Year+"/"+p.Token;
    }
    private void Ack(Packet p)
    {
        if(!Game1.player.modData.TryGetValue(PendingRelease,out var json))return;
        var saved=JsonSerializer.Deserialize<Packet>(json);
        if(saved?.Token==p.Token&&saved.Year==p.Year)Game1.player.modData.Remove(PendingRelease);
    }
    internal static void Caught(Farmer who)
    {if(CanCatch(who))Instance!.Release(true);}
    private void Release(bool caught)
    {
        var s=State.Value;string token=s.Token;int year=s.Year;
        s.Token="";s.Granted=false;
        if(token!="")
        {
            var packet=new Packet{Action="release",Token=token,Year=year,Caught=caught};
            Game1.player.modData[PendingRelease]=JsonSerializer.Serialize(packet);
            Game1.player.modData.Remove(SavedToken);Send(packet);
        }
    }
    private void Recover()
    {
        State.Value=new();
        if(Game1.player.modData.TryGetValue(PendingRelease,out var pending))Send(JsonSerializer.Deserialize<Packet>(pending)!);
        if(!Game1.player.modData.TryGetValue(SavedToken,out var raw))return;
        var parts=raw.Split('/');
        if(parts.Length!=2||!int.TryParse(parts[0],out int year))return;
        // A normal game save is outside active fishing. Successful catches have already persisted
        // their annual record. Reconcile the old lease without rolling another fish or touching items.
        bool caught=!AnnualLegendaryFeature.MachineAvailable(Game1.player,SpiritRules.Fish,year);
        var packet=new Packet{Action="release",Token=parts[1],Year=year,Caught=caught};
        Game1.player.modData[PendingRelease]=JsonSerializer.Serialize(packet);
        Game1.player.modData.Remove(SavedToken);Send(packet);
    }
}

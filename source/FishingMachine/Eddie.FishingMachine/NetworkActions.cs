using Eddie.FishingMachine.Core;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Objects;

namespace Eddie.FishingMachine;

/// <summary>Client-owned inventory escrow, host-owned world mutation, durable replay receipts.</summary>
internal sealed class NetworkActions
{
    private const string PendingKey=ModEntry.Tag+"PendingAction", SequenceKey=ModEntry.Tag+"ActionSequence";
    private const string RequestType="MachineAction.v1", ReceiptType="MachineReceipt.v1";
    private const string ControlRequestKey=ModEntry.Tag+"CrabCommandRequest";
    private readonly ModEntry Mod;
    private readonly Dictionary<long,MachineRequest> Queue=new();
    private readonly HashSet<long> Processing=new();
    private readonly HashSet<string> Logged=new();
    private readonly PerScreen<long> LastSend=new(()=>0);
    private readonly PerScreen<string> LastNotice=new(()=>"");
    internal NetworkActions(ModEntry mod)=>Mod=mod;
    private static string ReceiptKey(long player)=>ModEntry.Tag+"Receipt/"+player.ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal bool HasPending=>Game1.player.modData.ContainsKey(PendingKey);
    private static MachineRequest? Pending(Farmer who)=>who.modData.TryGetValue(PendingKey,out string raw)?MachineRequest.Load(raw):null;
    private static MachineReceipt? LastReceipt(long actor)=>Game1.MasterPlayer.modData.TryGetValue(ReceiptKey(actor),out string raw)?MachineReceipt.Load(raw):null;

    internal void Place(GameLocation location,Vector2 tile,Facing facing)
    {
        if(HasPending){Mod.Message("network.pending");return;}
        if(!HostAvailable()){Mod.Message("network.host-version");return;}
        if(!Mod.World.CanPlace(location,tile,facing,ModEntry.IsCrab(Game1.player.ActiveObject)) || !Mod.World.InReach(Game1.player,tile,facing))
        {Mod.Message("placement.invalid");return;}
        var held=Game1.player.ActiveObject;
        if(!ModEntry.IsMachine(held) || held!.Stack<1)return;
        var request=NewRequest(ActionKind.Place,location,tile,facing,"") with { ItemId=held.ItemId, MachineState=held.modData.TryGetValue(ProductionService.StateKey,out var state)?state:null };
        if(!request.IsValid()){Mod.Message("status.error");return;}
        string raw=request.Save();
        // Keep the original slot as a rollback snapshot. No network wait inside this local transaction.
        int slot=Game1.player.CurrentToolIndex, before=held.Stack;
        try
        {
            Game1.player.reduceActiveItemByOne();
            Game1.player.modData[PendingKey]=raw;
            Game1.player.modData[SequenceKey]=request.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            held.Stack=before;Game1.player.Items[slot]=held;Game1.player.modData.Remove(PendingKey);throw;
        }
        Send(request);
    }
    internal void Remove(Chest chest)
    {
        if(HasPending){Mod.Message("network.pending");return;}
        if(!HostAvailable()){Mod.Message("network.host-version");return;}
        var returned=ItemRegistry.Create(chest.QualifiedItemId);
        returned.modData[ProductionService.StateKey]=Mod.Production.Portable(chest);
        if(!Mod.World.InReach(Game1.player,chest))
        {Mod.Message("access.bank");return;}
        var request=NewRequest(ActionKind.Remove,chest.Location,chest.TileLocation,WorldService.GetFacing(chest),chest.modData[ModEntry.Tag+"MachineId"]) with {ItemId=chest.ItemId};
        Game1.player.modData[PendingKey]=request.Save();
        Game1.player.modData[SequenceKey]=request.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Send(request);
    }
    internal void Control(Chest chest,ActionKind kind)
    {
        if(!chest.GetMutex().IsLockHeld() || HasPending || !HostAvailable() || !ModEntry.IsMachine(chest)
            || kind is not(ActionKind.Start or ActionKind.Upgrade) || kind==ActionKind.Upgrade&&!ModEntry.IsCrab(chest))return;
        var request=NewRequest(kind,chest.Location,chest.TileLocation,WorldService.GetFacing(chest),chest.modData[ModEntry.Tag+"MachineId"]) with{ItemId=chest.ItemId};
        Game1.player.modData[PendingKey]=request.Save();Game1.player.modData[SequenceKey]=request.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        chest.modData[ControlRequestKey]=request.Save();
        chest.modData[ProductionService.CrabCommandKey]=request.Id;Send(request);
    }
    private bool HostAvailable()
    {
        if(Context.IsMainPlayer || Context.IsSplitScreen)return true;
        return Mod.Helper.Multiplayer.GetConnectedPlayer(Game1.MasterPlayer.UniqueMultiplayerID)
            ?.GetMod(Mod.ModManifest.UniqueID)?.Version.ToString()==Mod.ModManifest.Version.ToString();
    }
    private MachineRequest NewRequest(ActionKind kind,GameLocation l,Vector2 a,Facing f,string machine)
    {
        long.TryParse(Game1.player.modData.TryGetValue(SequenceKey,out var value)?value:"0",out long sequence);
        long previous=LastReceipt(Game1.player.UniqueMultiplayerID)?.Sequence??0;
        return new(){Id=Guid.NewGuid().ToString("N"),Sequence=checked(Math.Max(sequence,previous)+1),Actor=Game1.player.UniqueMultiplayerID,
            Kind=kind,Location=l.NameOrUniqueName,X=(int)a.X,Y=(int)a.Y,Facing=f,MachineId=machine};
    }
    private void Send(MachineRequest request)
    {
        LastSend.Value=Environment.TickCount64;
        try
        {
            if(Context.IsMainPlayer)Queue[request.Actor]=request;
            else Mod.Helper.Multiplayer.SendMessage(request,RequestType,new[]{Mod.ModManifest.UniqueID},new[]{Game1.MasterPlayer.UniqueMultiplayerID});
        }
        catch(Exception ex){LogOnce("send:"+request.Id,ex);}
        // A send timeout must never refund escrow: the host may already have placed the machine.
    }
    internal void OnMessage(object? sender,ModMessageReceivedEventArgs e)
    {
        if(!Context.IsWorldReady || e.FromModID!=Mod.ModManifest.UniqueID)return;
        try
        {
            if(Context.IsMainPlayer && e.Type==RequestType)
            {
                var request=e.ReadAs<MachineRequest>();
                if(!request.IsValid() || request.Actor!=e.FromPlayerID)return;
                if(!Queue.TryGetValue(request.Actor,out var old) || request.Sequence>=old.Sequence)Queue[request.Actor]=request;
            }
            else if(e.Type==ReceiptType && e.FromPlayerID==Game1.MasterPlayer.UniqueMultiplayerID)
                Receive(e.ReadAs<MachineReceipt>());
        }
        catch(Exception ex){LogOnce("message:"+e.FromPlayerID,ex);}
    }
    internal void Tick()
    {
        if(!Context.IsWorldReady || Mod.Saving)return;
        try
        {
            var pending=Pending(Game1.player);
            if(pending!=null)
            {
                // The host receipt is also a replicated save field, so lost replies are recoverable.
                var receipt=LastReceipt(pending.Actor);
                if(receipt!=null)Receive(receipt);
                if(HasPending && Environment.TickCount64-LastSend.Value>=2000)Send(pending);
            }
        }
        catch(Exception ex){LogOnce("pending:"+Game1.player.UniqueMultiplayerID,ex);Notice("error","network.error");}
        if(!Context.IsMainPlayer)return;
        RecoverControls();
        foreach(var request in Queue.Values.ToArray())
        {
            if(Processing.Contains(request.Actor))continue;
            try{Process(request);}catch(Exception ex){Processing.Remove(request.Actor);LogOnce("host:"+request.Id,ex);}
        }
    }

    private void Process(MachineRequest request)
    {
        var last=LastReceipt(request.Actor);
        switch(ActionProtocol.Order(request,last))
        {
            case RequestOrder.Replay:
                if(!ReleaseControl(request))return;
                Queue.Remove(request.Actor);Reply(last!);return;
            case RequestOrder.Stale:
                if(!ReleaseControl(request))return;
                Queue.Remove(request.Actor);Reply(MachineReceipt.For(request,ReceiptResult.Stale,"network.error"));return;
        }
        var who=Game1.GetPlayer(request.Actor,onlyOnline:true);
        if(who==null)
        {
            // An offline starter cannot complete a reach-sensitive UI action. Settle
            // only authenticated control intents; placement/removal escrow still waits.
            if(request.Kind is ActionKind.Start or ActionKind.Upgrade
                && Game1.GetPlayer(request.Actor,onlyOnline:false) is Farmer offline && Pending(offline)==request)
                Finish(request,"access.bank");
            return;
        }
        var escrow=Pending(who);
        // Mod messages may arrive before vanilla farmer-field updates. Wait for the same persisted intent.
        if(escrow==null || escrow!=request)return;
        var location=who.currentLocation;
        if(location==null || location.NameOrUniqueName!=request.Location
            || request.Kind==ActionKind.Place && !Mod.World.InReach(who,new(request.X,request.Y),request.Facing))
        {Finish(request,"access.bank");return;}
        if(request.Kind==ActionKind.Place)
        {
            string? reason=Mod.World.PlaceHost(location,new(request.X,request.Y),request.Facing,who,request.Id,request.MachineState,
                ()=>Store(MachineReceipt.For(request,ReceiptResult.Accepted)),request.ItemId);
            Finish(request,reason,alreadyStored:reason==null);
        }
        else if(request.Kind is ActionKind.Start or ActionKind.Upgrade)
        {
            if(!Mod.World.TryAnchor(location,new(request.X,request.Y),out var c) || !ModEntry.IsMachine(c) || c!.ItemId!=request.ItemId
                || c.modData[ModEntry.Tag+"MachineId"]!=request.MachineId || !Mod.World.InReach(who,c)){Finish(request,"access.bank");return;}
            if(!c.modData.TryGetValue(ProductionService.CrabCommandKey,out var intent))return; // replicated after farmer intent
            if(intent!=request.Id){Finish(request,"network.pending");return;}
            c.GetMutex().Update(location);if(c.GetMutex().IsLocked())return;
            Processing.Add(request.Actor);
            c.GetMutex().RequestLock(()=>
            {
                try
                {
                    string? error=!Context.IsWorldReady || Mod.Saving || who.currentLocation!=location || !Mod.World.InReach(who,c)
                        || !Mod.Production.MachineLive(c)?"access.bank":Mod.Production.CrabControl(c,who,request.Kind);
                    Finish(request,error,lockedControl:c);
                }
                finally{c.GetMutex().ReleaseLock();Processing.Remove(request.Actor);}
            },()=>Processing.Remove(request.Actor));
            c.GetMutex().Update(location);
        }
        else
        {
            if(!Mod.World.TryAnchor(location,new(request.X,request.Y),out var current) || current!.ItemId!=request.ItemId){Finish(request,"remove.gone");return;}
            if(who.CurrentTool is not (StardewValley.Tools.Axe or StardewValley.Tools.Pickaxe))
            {Finish(request,"remove.tool");return;}
            Processing.Add(request.Actor);
            Mod.World.RemoveHost(location,new(request.X,request.Y),request.MachineId,who,
                state=>Store(MachineReceipt.For(request,ReceiptResult.Accepted,machineState:state)),
                reason=>{Processing.Remove(request.Actor);Finish(request,reason,alreadyStored:reason==null);});
        }
    }
    private static void Store(MachineReceipt receipt)=>Game1.MasterPlayer.modData[ReceiptKey(receipt.Actor)]=receipt.Save();
    private void Finish(MachineRequest request,string? reason,bool alreadyStored=false,Chest? lockedControl=null)
    {
        var receipt=alreadyStored ? LastReceipt(request.Actor)! : MachineReceipt.For(request,reason==null?ReceiptResult.Accepted:ReceiptResult.Rejected,reason??"");
        if(!alreadyStored)Store(receipt);
        // Persist the outcome before clearing the handoff. Never acknowledge a
        // terminal control while a stale marker can still block Automate forever.
        if(!ReleaseControl(request,lockedControl))return;
        Queue.Remove(request.Actor);Reply(receipt);
    }

    private bool ReleaseControl(MachineRequest request,Chest? lockedControl=null)
    {
        if(request.Kind is not(ActionKind.Start or ActionKind.Upgrade))return true;
        var chest=lockedControl ?? Mod.Machines.All.Select(p=>p.Key).FirstOrDefault(c=>ModEntry.IsCrab(c)
            && c.ItemId==request.ItemId
            && c.modData.TryGetValue(ModEntry.Tag+"MachineId",out var id) && id==request.MachineId
            && c.modData.TryGetValue(ProductionService.CrabCommandKey,out var intent) && intent==request.Id);
        // Identity, not its old map coordinates: cave recovery may relocate the
        // same machine while retaining a durable request that must be rejected.
        if(chest==null)return true;
        void Clear()
        {
            if(chest.modData.TryGetValue(ProductionService.CrabCommandKey,out var marker)&&marker==request.Id)
            {
                chest.modData.Remove(ProductionService.CrabCommandKey);
                chest.modData.Remove(ControlRequestKey);
            }
        }
        if(lockedControl!=null){Clear();return true;}
        if(!chest.modData.TryGetValue(ProductionService.CrabCommandKey,out var current)||current!=request.Id)return true;
        var mutex=chest.GetMutex();mutex.Update(chest.Location);
        if(mutex.IsLocked())return false;
        bool cleared=false;
        mutex.RequestLock(()=>
        {
            try
            {
                if(Context.IsWorldReady&&Context.IsMainPlayer&&!Mod.Saving&&mutex.IsLockHeld()) {Clear();cleared=true;}
            }
            finally{if(mutex.IsLockHeld())mutex.ReleaseLock();}
        });
        mutex.Update(chest.Location);
        return cleared;
    }

    private void RecoverControls()
    {
        // Iterate the maintained machine index, not every world object. The saved
        // request also recovers a receipt written just before a save/disconnect.
        foreach(var pair in Mod.Machines.All)
        {
            var c=pair.Key;
            if(!ModEntry.IsMachine(c)||!c.modData.TryGetValue(ProductionService.CrabCommandKey,out var marker))continue;
            try
            {
                MachineRequest? request=null;
                if(c.modData.TryGetValue(ControlRequestKey,out var raw))request=MachineRequest.Load(raw);
                else foreach(var farmer in Game1.getAllFarmers())
                {
                    var pending=Pending(farmer);
                    if(pending?.Id==marker){request=pending;break;}
                    var receipt=LastReceipt(farmer.UniqueMultiplayerID);
                    if(receipt?.Id!=marker||receipt.Kind is not(ActionKind.Start or ActionKind.Upgrade))continue;
                    request=new(){Id=receipt.Id,Sequence=receipt.Sequence,Actor=receipt.Actor,Kind=receipt.Kind,ItemId=receipt.ItemId,
                        Location=c.Location.NameOrUniqueName,X=(int)c.TileLocation.X,Y=(int)c.TileLocation.Y,Facing=WorldService.GetFacing(c),MachineId=c.modData[ModEntry.Tag+"MachineId"]};
                    break;
                }
                if(request==null||!request.IsValid()||request.Id!=marker||request.ItemId!=c.ItemId
                    || request.MachineId!=c.modData[ModEntry.Tag+"MachineId"]||request.Kind is not(ActionKind.Start or ActionKind.Upgrade))continue;
                if(!Queue.TryGetValue(request.Actor,out var old)||request.Sequence>=old.Sequence)Queue[request.Actor]=request;
            }
            catch(Exception ex){LogOnce("control-recovery:"+marker,ex);}
        }
    }
    private void Reply(MachineReceipt receipt)
    {
        if(receipt.Actor==Game1.player.UniqueMultiplayerID){Receive(receipt);return;}
        try{Mod.Helper.Multiplayer.SendMessage(receipt,ReceiptType,new[]{Mod.ModManifest.UniqueID},new[]{receipt.Actor});}
        catch(Exception ex){LogOnce("reply:"+receipt.Id,ex);}
    }
    private void Receive(MachineReceipt receipt)
    {
        if(receipt.Actor!=Game1.player.UniqueMultiplayerID)return;
        var pending=Pending(Game1.player);
        var effect=ActionProtocol.Effect(pending,receipt);
        if(effect==ReceiptEffect.Ignore)return;
        if(effect==ReceiptEffect.Unresolved){Notice(receipt.Id,"network.error");return;}
        if(effect==ReceiptEffect.ReturnMachine)
        {
            string? state=pending!.Kind==ActionKind.Place?pending.MachineState:receipt.MachineState;
            if(!MachineUpgrade.ValidPortable(state)){Notice(receipt.Id,"network.error");return;}
            var item=ItemRegistry.Create("(BC)"+receipt.ItemId);
            if(state!=null)item.modData[ProductionService.StateKey]=state;
            if(pending!.Kind==ActionKind.Remove)
            {
                // Like a vanilla machine knocked loose: the item pops out as a small icon at the player's
                // feet (never over the water the machine stood in) and is picked up by walking over it.
                Game1.player.modData.Remove(PendingKey);
                try{Game1.createItemDebris(item,Game1.player.getStandingPosition(),(Game1.player.FacingDirection+2)%4,Game1.player.currentLocation);}
                catch{Game1.player.modData[PendingKey]=pending.Save();throw;}
                LastNotice.Value="";Game1.playSound("axchop");return;
            }
            if(!Game1.player.couldInventoryAcceptThisItem(item)){Notice(receipt.Id,"network.return-space");return;}
            // Inventory and pending marker change synchronously on their owning client.
            var before=Game1.player.Items.Select(i=>{if(i==null)return null!;var copy=i.getOne();copy.Stack=i.Stack;return copy;}).ToArray();
            try
            {
                if(!Game1.player.addItemToInventoryBool(item))return;
                Game1.player.modData.Remove(PendingKey);
            }
            catch
            {
                Game1.player.Items.Clear();foreach(var i in before)Game1.player.Items.Add(i);
                if(pending!=null)Game1.player.modData[PendingKey]=pending.Save();throw;
            }
        }
        else Game1.player.modData.Remove(PendingKey);
        LastNotice.Value="";
        if(receipt.Result==ReceiptResult.Rejected && receipt.Reason?.StartsWith("dialog:",StringComparison.Ordinal)==true)
            Game1.drawObjectDialogue(Mod.T(receipt.Reason["dialog:".Length..]));
        else if(receipt.Result==ReceiptResult.Rejected && !string.IsNullOrEmpty(receipt.Reason))Mod.Message(receipt.Reason);
        else if(receipt.Result==ReceiptResult.Accepted)Game1.playSound(receipt.Kind==ActionKind.Place?"waterSlosh":"axchop");
    }
    private void Notice(string id,string key)
    {if(LastNotice.Value==id+key)return;LastNotice.Value=id+key;Mod.Message(key);}
    private void LogOnce(string key,Exception ex)
    {if(Logged.Add(key))Mod.Monitor.Log($"Machine transaction preserved for retry ({key}): {ex}",LogLevel.Error);}
    internal void Reset(){Queue.Clear();Processing.Clear();Logged.Clear();LastSend.Value=0;LastNotice.Value="";}
}

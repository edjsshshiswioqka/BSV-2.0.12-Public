using System.Globalization;
using System.Text.Json;
using BetterStardewValley.Transactions;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using SObject=StardewValley.Object;

namespace BetterStardewValley.Farming;

internal sealed record FertilizerRequest(int Version,string Id,long Sequence,long Actor,string Location,int X,int Y,string Soil,string Item,int Slot,string Phase);
internal sealed record FertilizerReceipt(string Id,long Sequence,long Actor,string Phase,string Soil,string Reason);

// A request does not spend. The host reserves/approves a concrete soil before the owner
// may escrow one plain fertilizer. Paid requests either commit or receive a durable refund.
internal sealed class FertilizerNetwork
{
    internal const string Pending=FertilizerService.Prefix+"Apply.v1",Sequence=FertilizerService.Prefix+"ApplySequence",
        Reservation=FertilizerService.Prefix+"Reservation",ReceiptPrefix=FertilizerService.Prefix+"ApplyReceipt/";
    private readonly FertilizerService Service;
    internal FertilizerNetwork(FertilizerService service)=>Service=service;
    private static string ReceiptKey(long who)=>ReceiptPrefix+who.ToString(CultureInfo.InvariantCulture);
    internal static FertilizerRequest? Read(Farmer who)
    {
        if(!who.modData.TryGetValue(Pending,out var raw))return null;
        if(raw.Length>4096)throw new InvalidDataException("Oversized fertilizer request.");
        var r=JsonSerializer.Deserialize<FertilizerRequest>(raw);
        if(r==null||r.Version!=1||r.Actor!=who.UniqueMultiplayerID||r.Sequence<=0||!Guid.TryParseExact(r.Id,"N",out _)
            ||r.Phase is not("Requested" or "Paid" or "Cancelled")||!FertilizerRules.Known(r.Item)||r.Slot<0
            ||r.Location.Length>256||r.X is <0 or >32767||r.Y is <0 or >32767)throw new InvalidDataException("Invalid fertilizer request; retained.");
        return r;
    }
    internal static FertilizerReceipt? Receipt(Farmer who)=>Game1.MasterPlayer.modData.TryGetValue(ReceiptKey(who.UniqueMultiplayerID),out var raw)
        ?JsonSerializer.Deserialize<FertilizerReceipt>(raw):null;
    private static void Store(FertilizerReceipt r)=>Game1.MasterPlayer.modData[ReceiptKey(r.Actor)]=JsonSerializer.Serialize(r);
    private bool PeerMatches(long actor)=>actor==Game1.MasterPlayer.UniqueMultiplayerID&&Context.IsMainPlayer||Context.IsSplitScreen
        ||Service.Helper.Multiplayer.GetConnectedPlayer(actor)?.GetMod("Eddie.BetterStardewValley")?.Version.ToString()==Service.Helper.ModRegistry.Get("Eddie.BetterStardewValley")?.Manifest.Version.ToString();
    internal static bool Plain(SObject item)=>FertilizerRules.Known(item.QualifiedItemId)&&item.GetType()==typeof(SObject)&&!item.questItem.Value
        &&item.Stack>0&&item.Quality==0&&!item.modData.Any()&&item.heldObject.Value==null;
    internal void Request(HoeDirt soil,Farmer who,SObject item)
    {
        if(!Context.IsWorldReady||IntegrationHost.Saving||who.modData.ContainsKey(Pending)||!who.IsLocalPlayer)return;
        if(!PeerMatches(Game1.MasterPlayer.UniqueMultiplayerID)){Service.Notice("farming.version");return;}
        if(!Plain(item)||!FertilizerService.Healthy(soil)||!FertilizerRules.CanAdd(FertilizerService.Read(soil),item.QualifiedItemId,soil.crop?.currentPhase.Value))
        {Service.Notice("farming.rejected");return;}
        int slot=who.Items.IndexOf(item);if(slot<0||soil.Location!=who.currentLocation||Vector2.Distance(who.Tile,soil.Tile)>2.5f)return;
        long.TryParse(who.modData.GetValueOrDefault(Sequence),out long prior);
        long next=checked(Math.Max(prior,Receipt(who)?.Sequence??0)+1);
        var r=new FertilizerRequest(1,Guid.NewGuid().ToString("N"),next,who.UniqueMultiplayerID,soil.Location.NameOrUniqueName,
            (int)soil.Tile.X,(int)soil.Tile.Y,soil.modData.GetValueOrDefault(FertilizerService.SoilKey)??"",item.QualifiedItemId,slot,"Requested");
        who.modData[Pending]=JsonSerializer.Serialize(r);who.modData[Sequence]=next.ToString(CultureInfo.InvariantCulture);
        Tick();
    }
    internal void Tick()
    {
        if(!Context.IsWorldReady||IntegrationHost.Saving)return;
        if(Context.IsMainPlayer)
            foreach(var who in Game1.getAllFarmers().ToArray())
                try{Process(who);}catch(Exception ex){Service.Error(ex);}
        try{Receive(Game1.player);}catch(Exception ex){Service.Error(ex);}
    }
    internal void Process(Farmer who)
    {
        if(!Context.IsMainPlayer)return;
        var r=Read(who);if(r==null)return;
        var receipt=Receipt(who);
        if(receipt!=null&&(r.Sequence<receipt.Sequence||r.Sequence==receipt.Sequence&&(receipt.Id!=r.Id||receipt.Phase is "Committed" or "Rejected")))return;
        var location=who.currentLocation;
        var soil=location?.NameOrUniqueName==r.Location?location.GetHoeDirtAtTile(new(r.X,r.Y)):null;
        bool valid=PeerMatches(who.UniqueMultiplayerID)&&!Game1.eventUp&&soil!=null&&FertilizerService.Healthy(soil)
            &&Vector2.Distance(who.Tile,new(r.X,r.Y))<=2.5f&&FertilizerRules.CanAdd(FertilizerService.Read(soil),r.Item,soil.crop?.currentPhase.Value)
            &&(r.Soil==""||soil.modData.GetValueOrDefault(FertilizerService.SoilKey)==r.Soil);
        if(r.Phase=="Cancelled"||!valid)
        {Reject(who,r,soil);return;}
        FertilizerService.Initialize(soil!);
        string soilId=FertilizerService.Identity(soil!);
        string? reserved=soil!.modData.GetValueOrDefault(Reservation);
        if(reserved!=null&&reserved!=r.Id)
        {
            // No item has been charged to a waiting request. Remove only orphaned leases.
            bool active=Game1.getAllFarmers().Any(f=>f.modData.TryGetValue(Pending,out var raw)&&raw.Contains(reserved,StringComparison.Ordinal));
            if(active)return;
            soil.modData.Remove(Reservation);
        }
        if(r.Phase=="Requested")
        {
            if(receipt?.Id==r.Id&&receipt.Phase=="Approved")return;
            soil.modData[Reservation]=r.Id;
            Store(new(r.Id,r.Sequence,r.Actor,"Approved",soilId,""));return;
        }
        if(receipt?.Id!=r.Id||receipt.Phase!="Approved"||reserved!=r.Id||receipt.Soil!=soilId){Reject(who,r,soil);return;}
        var next=FertilizerRules.Add(FertilizerService.Read(soil),r.Item,soil.crop?.currentPhase.Value);
        string? oldNative=soil.fertilizer.Value,oldSlots=soil.modData.GetValueOrDefault(FertilizerService.SlotKey);
        var crop=soil.crop;var phases=crop?.phaseDays.ToArray();
        string oldReceipt=Game1.MasterPlayer.modData[ReceiptKey(who.UniqueMultiplayerID)];
        AtomicChange.Execute(()=>soil==location!.GetHoeDirtAtTile(new(r.X,r.Y))&&soil.modData.GetValueOrDefault(Reservation)==r.Id,
            new AtomicStep[]{new(()=>
            {
                FertilizerService.Write(soil,next);
                if(FertilizerRules.Speed(r.Item)>0&&crop!=null&&!crop.fullyGrown.Value)soil.applySpeedIncreases(who);
            },()=>
            {
                soil.fertilizer.Value=oldNative;
                if(oldSlots==null)soil.modData.Remove(FertilizerService.SlotKey);else soil.modData[FertilizerService.SlotKey]=oldSlots;
                if(phases!=null&&crop!=null){crop.phaseDays.Clear();foreach(int day in phases)crop.phaseDays.Add(day);}
            }),new(()=>Store(new(r.Id,r.Sequence,r.Actor,"Committed",soilId,"")),()=>Game1.MasterPlayer.modData[ReceiptKey(who.UniqueMultiplayerID)]=oldReceipt)});
        soil.modData.Remove(Reservation);
    }
    private static void Reject(Farmer who,FertilizerRequest r,HoeDirt? soil)
    {
        if(soil?.modData.GetValueOrDefault(Reservation)==r.Id)soil.modData.Remove(Reservation);
        Store(new(r.Id,r.Sequence,r.Actor,"Rejected",r.Soil,"farming.rejected"));
    }
    internal void Receive(Farmer who)
    {
        if(!who.IsLocalPlayer)return;
        var r=Read(who);var receipt=Receipt(who);
        if(r==null||receipt?.Id!=r.Id||receipt.Sequence!=r.Sequence)return;
        if(receipt.Phase=="Approved"&&r.Phase=="Requested")
        {
            var item=r.Slot<who.Items.Count?who.Items[r.Slot] as SObject:null;
            if(item==null||item.QualifiedItemId!=r.Item||!Plain(item))
            {who.modData[Pending]=JsonSerializer.Serialize(r with{Phase="Cancelled"});return;}
            int count=item.Stack;string old=who.modData[Pending];
            try{if(count==1)who.Items[r.Slot]=null;else item.Stack--;who.modData[Pending]=JsonSerializer.Serialize(r with{Phase="Paid",Soil=receipt.Soil});}
            catch{item.Stack=count;who.Items[r.Slot]=item;who.modData[Pending]=old;throw;}
            return;
        }
        if(receipt.Phase is not("Committed" or "Rejected"))return;
        if(receipt.Phase=="Rejected"&&r.Phase=="Paid")
        {
            var refund=ItemRegistry.Create(r.Item,1);
            if(!who.couldInventoryAcceptThisItem(refund))return; // durable paid request until space exists
            var before=who.Items.ToArray();var stacks=before.Select(i=>i?.Stack??0).ToArray();string old=who.modData[Pending];
            try{if(!who.addItemToInventoryBool(refund))throw new InvalidOperationException("Fertilizer refund failed.");who.modData.Remove(Pending);}
            catch{who.Items.Clear();for(int i=0;i<before.Length;i++){if(before[i]!=null)before[i].Stack=stacks[i];who.Items.Add(before[i]);}who.modData[Pending]=old;throw;}
        }
        else who.modData.Remove(Pending);
        if(receipt.Phase=="Rejected")Service.Notice(receipt.Reason);
    }
}

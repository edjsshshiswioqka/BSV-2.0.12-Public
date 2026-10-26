using System.Text.Json;
namespace Eddie.FishingMachine.Core;

public enum ActionKind { Place, Remove, Start, Upgrade }
public enum ReceiptResult { Accepted, Rejected, Stale }
public enum RequestOrder { New, Replay, Stale }
public enum ReceiptEffect { Ignore, Complete, ReturnMachine, Unresolved }

public sealed record MachineRequest
{
    public int Protocol { get; init; } = 1;
    public string Id { get; init; } = "";
    public long Sequence { get; init; }
    public long Actor { get; init; }
    public ActionKind Kind { get; init; }
    public string Location { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public Facing Facing { get; init; } = Facing.Right;
    public string MachineId { get; init; } = "";
    public string ItemId { get; init; } = MachineKinds.FishingId;
    public string? MachineState { get; init; }
    /// <summary>Start/upgrade sent from a machine window opened through Chests Anywhere (no reach rule).</summary>
    public bool Remote { get; init; }
    public string Save() => JsonSerializer.Serialize(this);
    public static MachineRequest Load(string raw)
    {
        var value = JsonSerializer.Deserialize<MachineRequest>(raw) ?? throw new ArgumentException("Empty operation.");
        if (!value.IsValid()) throw new ArgumentException("Invalid or unsupported operation.");
        return value;
    }
    public bool IsValid() => Protocol == 1 && MachineKinds.Matches(ItemId,MachineState) && Guid.TryParseExact(Id, "N", out _) && Sequence > 0
        && Enum.IsDefined(typeof(ActionKind), Kind) && Enum.IsDefined(typeof(Facing), Facing)
        && Location is { Length: > 0 and <= 256 } && X >= 0 && Y >= 0 && X < 32768 && Y < 32768
        && (Kind == ActionKind.Place || Guid.TryParseExact(MachineId, "N", out _))
        && (Kind != ActionKind.Upgrade || ItemId==MachineKinds.CrabId)
        && (!Remote || Kind is ActionKind.Start or ActionKind.Upgrade);
}

public sealed record MachineReceipt
{
    public int Protocol { get; init; } = 1;
    public string Id { get; init; } = "";
    public long Sequence { get; init; }
    public long Actor { get; init; }
    public ActionKind Kind { get; init; }
    public ReceiptResult Result { get; init; }
    public string Reason { get; init; } = "";
    public string ItemId { get; init; } = MachineKinds.FishingId;
    public string? MachineState { get; init; }
    public static MachineReceipt For(MachineRequest request, ReceiptResult result, string reason = "", string? machineState = null) => new()
    { Id=request.Id, Sequence=request.Sequence, Actor=request.Actor, Kind=request.Kind, ItemId=request.ItemId, Result=result, Reason=reason, MachineState=machineState ?? request.MachineState };
    public string Save() => JsonSerializer.Serialize(this);
    public static MachineReceipt Load(string raw)
    {
        var value=JsonSerializer.Deserialize<MachineReceipt>(raw) ?? throw new ArgumentException("Empty receipt.");
        if(value.Protocol!=1 || !MachineKinds.Matches(value.ItemId,value.MachineState) || value.Sequence<=0 || !Guid.TryParseExact(value.Id,"N",out _)
            || !Enum.IsDefined(typeof(ActionKind),value.Kind) || !Enum.IsDefined(typeof(ReceiptResult),value.Result))
            throw new ArgumentException("Invalid or unsupported receipt.");
        return value;
    }
}

/// <summary>One outstanding operation per player, monotonic sequence, persisted host receipt.</summary>
public static class ActionProtocol
{
    public static RequestOrder Order(MachineRequest request, MachineReceipt? last)
    {
        if(!request.IsValid())throw new ArgumentException("Malformed operation.");
        if(last==null)return RequestOrder.New;
        if(last.Actor!=request.Actor)throw new ArgumentException("Receipt belongs to another player.");
        if(last.Sequence==request.Sequence && last.Id==request.Id && last.Kind==request.Kind && last.ItemId==request.ItemId)return RequestOrder.Replay;
        return request.Sequence<=last.Sequence?RequestOrder.Stale:RequestOrder.New;
    }
    public static ReceiptEffect Effect(MachineRequest? pending, MachineReceipt receipt)
    {
        if(pending==null || receipt.Protocol!=1 || pending.Id!=receipt.Id || pending.Sequence!=receipt.Sequence
            || pending.Actor!=receipt.Actor || pending.Kind!=receipt.Kind || pending.ItemId!=receipt.ItemId)return ReceiptEffect.Ignore;
        if(receipt.Result==ReceiptResult.Stale)return ReceiptEffect.Unresolved;
        if(receipt.Result is not (ReceiptResult.Accepted or ReceiptResult.Rejected))return ReceiptEffect.Ignore;
        bool returnMachine=pending.Kind==ActionKind.Place && receipt.Result==ReceiptResult.Rejected
            || pending.Kind==ActionKind.Remove && receipt.Result==ReceiptResult.Accepted;
        return returnMachine?ReceiptEffect.ReturnMachine:ReceiptEffect.Complete;
    }
}

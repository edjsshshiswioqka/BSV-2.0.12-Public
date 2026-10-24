namespace BetterStardewValley;

// SMAPI transports concrete public DTOs. Ownership/position is never accepted from a receipt.
public sealed class HorseRequest
{
    public string Id { get; set; } = "";
    public long Actor { get; set; }
    public string Horse { get; set; } = "";
    public string Location { get; set; } = "";
    public string Action { get; set; } = "Call";
    public string Name { get; set; } = "";
    /// <summary>"Come" or "Leave", from the caller's press counter (odd presses call, even presses send away).
    /// Empty from an older client: the host then decides from where the horse is, as before.</summary>
    public string Intent { get; set; } = "";
    public HorseViewport View { get; set; } = new();
}
public sealed class HorseViewport
{
    public long Sequence { get; set; }
    public long Actor { get; set; }
    public string Location { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
public sealed class HorseReceipt
{
    public long Actor { get; set; }
    public string Id { get; set; } = "";
    public bool Running { get; set; }
    public string Message { get; set; } = "";
    /// <summary>"call" or "dismiss": which whistle the caller plays (decided by the host).</summary>
    public string Sound { get; set; } = "";
    public string HorseName { get; set; } = "";
}

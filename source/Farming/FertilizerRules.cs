[assembly:System.Runtime.CompilerServices.InternalsVisibleTo("FarmingNativeChecks")]
namespace BetterStardewValley.Farming;

internal sealed record FertilizerSlots(int Version, string Primary, string Quality, string Speed)
{
    internal bool Dual => Quality.Length>0 && Speed.Length>0;
}

internal static class FertilizerRules
{
    internal const int Version=1;
    internal static string Id(string? value) => string.IsNullOrEmpty(value)||value=="0"?"":value.StartsWith("(")?value:"(O)"+value;
    internal static int Quality(string? id) => Id(id) switch { "(O)368"=>1,"(O)369"=>2,"(O)919"=>3,_=>0 };
    internal static int Speed(string? id) => Id(id) switch { "(O)465"=>1,"(O)466"=>2,"(O)918"=>3,_=>0 };
    internal static bool Known(string? id) => Quality(id)>0||Speed(id)>0;
    internal static FertilizerSlots Native(string? id)
    {
        string primary=Id(id);
        return new(Version,primary,Quality(primary)>0?primary:"",Speed(primary)>0?primary:"");
    }
    internal static bool Valid(FertilizerSlots slots,string? native) => slots.Version==Version&&slots.Primary==Id(native)
        && Known(slots.Primary) && (slots.Quality==""||Quality(slots.Quality)>0) && (slots.Speed==""||Speed(slots.Speed)>0)
        && (slots.Primary==slots.Quality||slots.Primary==slots.Speed);
    internal static bool CanAdd(FertilizerSlots slots,string id,int? cropPhase)
    {
        id=Id(id);
        if(!Known(id)||slots.Primary!=""&&!Known(slots.Primary))return false;
        // Native 1.6.15 forbids Basic/Quality after sprouting, but permits Deluxe.
        if(Quality(id)>0)return slots.Quality==""&&(!(cropPhase>0)||Quality(id)==3);
        return slots.Speed=="";
    }
    internal static FertilizerSlots Add(FertilizerSlots slots,string id,int? cropPhase)
    {
        id=Id(id);
        if(!CanAdd(slots,id,cropPhase))throw new InvalidOperationException("Fertilizer slot cannot accept this item.");
        return new(Version,slots.Primary==""?id:slots.Primary,Quality(id)>0?id:slots.Quality,Speed(id)>0?id:slots.Speed);
    }
    internal static int Regrow(int original,int grade,bool enabled)
    {
        if(!enabled||original<=3)return original;
        int reduction=grade switch {1 when original>=7=>1,2 when original>=4=>1,3 when original>=7=>2,3 when original>=4=>1,_=>0};
        return reduction==0?original:Math.Max(2,original-reduction);
    }
}

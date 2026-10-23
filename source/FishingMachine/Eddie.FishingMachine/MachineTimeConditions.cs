using StardewValley;
namespace Eddie.FishingMachine;
/// <summary>Only the audited TIME predicate is simulated. No global clock/player/weather mutation.</summary>
internal static class MachineTimeConditions
{
    internal static bool Historical(string? condition,int clock,int day,GameLocation? location=null)
    {
        if(string.IsNullOrWhiteSpace(condition))return true;
        foreach(string part in condition.Split(','))
        {
            string raw=part.Trim();bool invert=raw.StartsWith('!');var a=(invert?raw[1..]:raw).Split(' ',StringSplitOptions.RemoveEmptyEntries);
            if(a.Length==0)return false;
            bool yes;
            if(a[0]=="TIME")
            {
                int hi=int.MaxValue;
                if(a.Length is not(2 or 3)||!int.TryParse(a[1],out int lo)||a.Length==3&&!int.TryParse(a[2],out hi))return false;
                yes=clock>=lo&&clock<=hi;
            }
            else if(a[0]=="SEASON"&&a.Length>1)yes=a.Skip(1).Contains(new[]{"spring","summer","fall","winter"}[Eddie.FishingMachine.Core.MachineTimeline.Season(day)]);
            else if(a[0]=="LOCATION_SEASON"&&a.Length>2&&a[1]=="Here"&&location!=null)
            {
                // Seasonless contexts (Ginger Island, the desert) keep their own fixed season.
                string season=location.GetSeason()!=Game1.season
                    ?location.GetSeasonKey()
                    :new[]{"spring","summer","fall","winter"}[Eddie.FishingMachine.Core.MachineTimeline.Season(day)];
                yes=a.Skip(2).Any(x=>string.Equals(x,season,StringComparison.OrdinalIgnoreCase));
            }
            else return false; // Unknown remains unknown, including a negated weather/event predicate.
            if(yes==invert)return false;
        }
        return true;
    }
    internal static bool Check(string? condition,int clock,GameLocation location,Farmer owner)
    {
        if(string.IsNullOrWhiteSpace(condition))return true;
        foreach(string part in condition.Split(','))
        {
            string raw=part.Trim();bool invert=raw.StartsWith('!');var a=(invert?raw[1..]:raw).Split(' ',StringSplitOptions.RemoveEmptyEntries);
            if(a.Length==0)return false;
            if(a[0]=="TIME")
            {
                int hi=int.MaxValue;
                if(a.Length is not(2 or 3)||!int.TryParse(a[1],out int lo)||a.Length==3&&!int.TryParse(a[2],out hi))return false;
                bool yes=clock>=lo&&clock<=hi;if(yes==invert)return false;
            }
            else
            {
                // Custom/nested time queries cannot be safely executed as historical queries.
                if(a[0].Contains('/')||a[0] is "RANDOM" or "SYNCED_RANDOM" or "ANY" or "ALL")return false;
                if(!GameStateQuery.CheckConditions(raw,location,owner))return false;
            }
        }
        return true;
    }
}

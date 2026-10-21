namespace Eddie.FishingMachine.Core;

/// <summary>One choice per eligible ocean BASE opportunity, before fish/junk and before bonuses.</summary>
public static class KingCrabRules
{
    public const string Id="Eddie.BetterStardewValley_KingCrab", Fish="(O)"+Id;
    public const string BowlId="Eddie.BetterStardewValley_KingCrabRiceBowl", Bowl="(O)"+BowlId;
    public const double Chance=.015;
    public const int Price=750, Defense=8, Immunity=4, Minutes=600;
    public static bool IsFish(string? id)=>id==Id||id==Fish;
    public static bool Select(bool ocean,double u)
    {
        if(!double.IsFinite(u)||u<0||u>=1)throw new ArgumentOutOfRangeException(nameof(u));
        return LobsterKingRules.Select(ocean,false,u,true)==RareCrab.KingCrab;
    }
    // Year one premiere; author-approved annual reruns on the same date thereafter.
    public static bool Premiere(int year,int season,int day)=>year>=1&&season==3&&day==18;
}

/// <summary>Ten-minute world-clock resolution. Round UP by at most one tick at application,
/// so eating just before a tick never loses almost ten minutes. No elapsed real-time debit.</summary>
public sealed class KingCrabClock
{
    public double Remaining {get;private set;}
    public int Last {get;private set;}
    public int Day {get;}
    private bool FirstTick;
    public KingCrabClock(int day,int hhmm,double minutes,bool partialTick=false)
    {if(!double.IsFinite(minutes)||minutes<=0)throw new ArgumentOutOfRangeException(nameof(minutes));Day=day;Last=ToMinutes(hhmm);Remaining=minutes;FirstTick=partialTick;}
    public static int ToMinutes(int hhmm)
    {if(hhmm<0||hhmm%100>=60)throw new ArgumentOutOfRangeException(nameof(hhmm));return checked(hhmm/100*60+hhmm%100);}
    public bool Advance(int day,int hhmm)
    {
        if(day!=Day){Remaining=0;return true;} // vanilla food never becomes an overnight blessing
        int now=ToMinutes(hhmm);int delta=Math.Max(0,now-Last);Last=now;
        // Skip only the unknown partial portion of the first ten-minute tick. A jump still counts.
        if(delta>0&&FirstTick){delta=Math.Max(0,delta-10);FirstTick=false;}
        Remaining=Math.Max(0,Remaining-delta);return Remaining<=0;
    }
}

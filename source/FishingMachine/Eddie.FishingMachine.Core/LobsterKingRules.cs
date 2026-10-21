namespace Eddie.FishingMachine.Core;

public enum RareCrab { Ordinary, GiantLobster, LobsterKing, KingCrab }
public static class LobsterKingRules
{
    public const string Id="Eddie.BetterStardewValley_LobsterKing", Fish="(O)"+Id;
    public const string SashimiId="Eddie.BetterStardewValley_LobsterKingSashimi";
    public const string ClaimKey="Eddie.BetterStardewValley/LobsterKing/WildClaim";
    public const double Chance=.003;
    public static RareCrab Select(bool ocean,bool kingEligible,double u,bool kingCrabEnabled=false)
    {
        if(!double.IsFinite(u)||u<0||u>=1)throw new ArgumentOutOfRangeException(nameof(u));
        if(!ocean)return RareCrab.Ordinary;
        if(u<Chance)return kingEligible?RareCrab.LobsterKing:RareCrab.Ordinary;
        // Fixed mutually exclusive intervals. Ineligible intervals fall through to ordinary,
        // never re-normalize another species or take a second rare draw.
        if(u<Chance+GiantLobsterRules.CrabChance)return RareCrab.GiantLobster;
        if(kingCrabEnabled&&u<Chance+GiantLobsterRules.CrabChance+KingCrabRules.Chance)return RareCrab.KingCrab;
        return RareCrab.Ordinary;
    }
    public static bool IsFish(string? id)=>id==Id||id==Fish;
    public static string SeasonKey(int year,int season)
    {if(year<1||season is <0 or >3)throw new ArgumentOutOfRangeException();return year+"/"+season;}
}
public sealed class KingClaim
{
    public string Token {get;set;}="";
    public string Season {get;set;}="";
    public string Source {get;set;}="";
    public int Day {get;set;}
    public bool Committed {get;set;}
    public long Recipient {get;set;}
}
public sealed class KingLedger
{
    public int Schema {get;set;}=1;
    public List<KingClaim> Claims {get;set;}=new();
    public Dictionary<long,int> FirstAcquired {get;set;}=new();
    public bool Available(int year,int season)=>season!=3&&!Claims.Any(c=>c.Season==LobsterKingRules.SeasonKey(year,season));
    public KingClaim Prepare(int year,int season,int day,string source)
    {
        if(!Available(year,season))throw new InvalidOperationException("Farm seasonal Lobster King claim unavailable.");
        var c=new KingClaim{Token=Guid.NewGuid().ToString("N"),Season=LobsterKingRules.SeasonKey(year,season),Source=source,Day=day};Claims.Add(c);return c;
    }
    public bool Acquire(string token,long player,int day)
    {
        var c=Claims.SingleOrDefault(c=>c.Token==token);
        if(c==null||!c.Committed||c.Recipient!=0||player==0)return false;
        c.Recipient=player;FirstAcquired.TryAdd(player,day);return true;
    }
    public void Validate()
    {
        if(Schema!=1||Claims==null||FirstAcquired==null||Claims.Any(c=>!Guid.TryParseExact(c.Token,"N",out _)||string.IsNullOrEmpty(c.Source)||c.Day<0
            ||c.Season.Split('/').Length!=2||!int.TryParse(c.Season.Split('/')[0],out int y)||y<1||!int.TryParse(c.Season.Split('/')[1],out int s)||s is <0 or >2)
            ||Claims.Select(c=>c.Token).Distinct().Count()!=Claims.Count||Claims.Select(c=>c.Season).Distinct().Count()!=Claims.Count
            ||FirstAcquired.Any(p=>p.Key==0||p.Value<0))throw new InvalidOperationException("Invalid Lobster King ledger; preserved without reset.");
    }
}
public sealed class KingPondState
{
    public int Schema {get;set;}=1;
    public string PondId {get;set;}=Guid.NewGuid().ToString("N");
    public int CycleIndex {get;set;}
    public int DueDay {get;set;}=-1;
    public int LastDay {get;set;}=-1;
    public bool Advance(int day,int population,bool occupied,Random random)
    {
        if(Schema!=1||!Guid.TryParseExact(PondId,"N",out _)||CycleIndex<0||DueDay< -1||LastDay< -1||day<0)throw new InvalidOperationException("Invalid pond cycle; preserved.");
        if(population!=2){DueDay=-1;LastDay=day;return false;}
        if(DueDay<0){DueDay=checked(day+random.Next(4,8));LastDay=day;return false;}
        if(day<LastDay)return false;
        LastDay=day;
        if(day<DueDay||occupied)return false;
        CycleIndex=checked(CycleIndex+1);DueDay=checked(day+random.Next(4,8));return true;
    }
}

public static class KingTransactions
{
    // Persist prepare -> persist complete output/inventory -> persist commit. Roll back
    // output before reopening quota. If rollback fails, the prepared record blocks reuse.
    public static bool Commit(KingLedger ledger,int year,int season,int day,string source,
        Action<KingLedger> save,Action<string> apply,Action rollback)
    {
        ledger.Validate();if(!ledger.Available(year,season))return false;
        var claim=ledger.Prepare(year,season,day,source);
        save(ledger);
        try{apply(claim.Token);claim.Committed=true;save(ledger);return true;}
        catch
        {
            try{rollback();claim.Committed=false;ledger.Claims.Remove(claim);save(ledger);}
            catch{throw new InvalidOperationException("King transaction rollback incomplete; prepared quota must be reconciled before reuse.");}
            throw;
        }
    }
}

namespace Eddie.FishingMachine.Core;

public sealed class MutantAnnualClaim
{
    public int Year {get;set;}
    public int Day {get;set;}
    public string Token {get;set;}="";
    public string Source {get;set;}="";
    public bool Committed {get;set;}
}
public sealed class MutantAnnualLedger
{
    public int Schema {get;set;}=1;
    // This is an explicit uncertainty boundary, never a fabricated capture record.
    public int MigrationLockedThroughYear {get;set;}
    public string MigrationReason {get;set;}="fresh-save";
    public List<MutantAnnualClaim> Claims {get;set;}=new();
    public bool Available(int year)=>year>MigrationLockedThroughYear&&!Claims.Any(c=>c.Year==year);
    public void Validate()
    {
        if(Schema!=1||MigrationLockedThroughYear<0||string.IsNullOrWhiteSpace(MigrationReason)||Claims==null
            ||Claims.Any(c=>c.Year<1||c.Day<0||MachineTimeline.Year(c.Day)!=c.Year||string.IsNullOrWhiteSpace(c.Source)||!Guid.TryParseExact(c.Token,"N",out _))
            ||Claims.Select(c=>c.Year).Distinct().Count()!=Claims.Count||Claims.Select(c=>c.Token).Distinct().Count()!=Claims.Count)
            throw new InvalidOperationException("Invalid Mutant annual ledger; preserved without reset.");
    }
    public bool Commit(int day,string source,Action<MutantAnnualLedger> save,Action<string> apply,Action rollback)
    {
        Validate();int year=MachineTimeline.Year(day);if(!Available(year))return false;
        var claim=new MutantAnnualClaim{Year=year,Day=day,Source=source,Token=Guid.NewGuid().ToString("N")};
        Claims.Add(claim);save(this);
        try{apply(claim.Token);claim.Committed=true;save(this);return true;}
        catch
        {
            try{rollback();Claims.Remove(claim);save(this);}
            catch{throw new InvalidOperationException("Mutant rollback incomplete; prepared annual quota retained.");}
            throw;
        }
    }
}

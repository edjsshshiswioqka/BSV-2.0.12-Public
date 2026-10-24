using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace BetterStardewValley.Configuration;

// A recoverable three-file transaction, not a claim of filesystem-wide atomicity.
// A durable journal precedes replacement; only a committed marker permits keeping
// the new files on restart. Failed rollback retains the journal and blocks writes.
internal static class ConfigFiles
{
    private const string Journal="bsv-config-transaction.json";
    internal static bool Writable {get;private set;}=true;
    internal sealed class Entry { public string Name="";public byte[]? Before;public byte[] After=Array.Empty<byte>(); }
    internal sealed class Record { public int Schema=1;public bool Committed;public List<Entry> Entries=new(); }
    private static readonly HashSet<string> Allowed=new(){"config.json","fishing-machine.json","crab-catcher.json"};
    private static void Atomic(string path,byte[] bytes)
    {
        string temp=path+".bsv-stage";
        using(var file=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
        {file.Write(bytes);file.Flush(true);}
        File.Move(temp,path,true);
    }
    private static byte[] Encode(object value)=>System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value,Formatting.Indented));
    private static void Restore(string dir,Record record)
    {
        foreach(var entry in record.Entries)
        { string path=Path.Combine(dir,entry.Name);if(entry.Before==null)File.Delete(path);else Atomic(path,entry.Before); }
    }
    internal static void Recover(string dir)
    {
        string path=Path.Combine(dir,Journal);if(!File.Exists(path))return;
        var record=JsonConvert.DeserializeObject<Record>(File.ReadAllText(path))??throw new InvalidDataException("Empty config journal.");
        if(record.Schema!=1||record.Entries==null||record.Entries.Count is <1 or >3
            ||record.Entries.Any(e=>e==null||!Allowed.Contains(e.Name))||record.Entries.Select(e=>e.Name).Distinct().Count()!=record.Entries.Count)
            throw new InvalidDataException("Invalid config journal; preserved for recovery.");
        if(!record.Committed)Restore(dir,record);
        File.Delete(path);
    }
    internal static void Commit(string dir,IReadOnlyDictionary<string,object> values,Action<int>? afterWrite=null)
    {
        if(!Writable)throw new InvalidOperationException("Config input/recovery failed; repair the preserved files before saving.");
        Recover(dir);
        if(values.Count is <1 or >3||values.Keys.Any(n=>!Allowed.Contains(n)))throw new InvalidDataException("Invalid config transaction targets.");
        var record=new Record{Entries=values.Select(p=>new Entry{Name=p.Key,
            Before=File.Exists(Path.Combine(dir,p.Key))?File.ReadAllBytes(Path.Combine(dir,p.Key)):null,After=Encode(p.Value)}).ToList()};
        string journal=Path.Combine(dir,Journal);Atomic(journal,Encode(record));
        try
        {
            int count=0;foreach(var entry in record.Entries){Atomic(Path.Combine(dir,entry.Name),entry.After);afterWrite?.Invoke(++count);}
            record.Committed=true;Atomic(journal,Encode(record));
        }
        catch
        {
            try{Restore(dir,record);File.Delete(journal);}catch{Writable=false;throw;}
            throw;
        }
        // If cleanup fails, the committed journal is harmless and recovered next time.
        try{File.Delete(journal);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    internal static ModConfig Load(IModHelper helper,IMonitor monitor)
    {
        try
        {
            Recover(helper.DirectoryPath);
            string path=Path.Combine(helper.DirectoryPath,"config.json");
            if(!File.Exists(path))
            {
                // First start: the package ships no config.json (so updates never reset players' settings); create it now.
                var fresh=new ModConfig{ConfigSchemaVersion=4};
                try{Commit(helper.DirectoryPath,new Dictionary<string,object>{["config.json"]=fresh});}catch(Exception ex){monitor.Log("BSV could not create config.json: "+ex.Message,LogLevel.Warn);}
                return fresh;
            }
            var raw=JObject.Parse(File.ReadAllText(path));
            var config=raw.ToObject<ModConfig>()??throw new InvalidDataException("Empty BSV config.");
            if(raw.GetValue(nameof(ModConfig.EnableHarvestSeeds),StringComparison.OrdinalIgnoreCase)==null
                && (raw.Value<int?>(nameof(ModConfig.ConfigSchemaVersion))??0)<2)config.EnableHarvestSeeds=true;
            int schema=raw.Value<int?>(nameof(ModConfig.ConfigSchemaVersion))??0;
            // A short-lived test build stored J for everyone (schema 3); the key is H again and only switches
            // to J at runtime while another mod uses H (see HorseFeature.CallKey).
            if(schema==3&&config.CallHorseKey==StardewModdingAPI.SButton.J)config.CallHorseKey=StardewModdingAPI.SButton.H;
            config.ConfigSchemaVersion=4;
            if(schema<4)try{Commit(helper.DirectoryPath,new Dictionary<string,object>{["config.json"]=config});}catch(Exception ex){monitor.Log("BSV could not update config.json: "+ex.Message,LogLevel.Warn);}
            return config;
        }
        catch(Exception ex)
        {
            Writable=false;
            monitor.Log(BsvI18n.Get(helper,"diag.config-error")+"\n"+ex,LogLevel.Error);
            return new ModConfig{EnableHarvestSeeds=false,HarvestBonus=new(){Enabled=false},EnableAllProfessions=false,EnableOrdersSystem=false};
        }
    }
}

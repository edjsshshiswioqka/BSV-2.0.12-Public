using System.Text.Json;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Objects;
namespace Eddie.FishingMachine;

/// <summary>Host-observed, saved environmental journal. Contains values only, never live game references.</summary>
internal static class TimelineEnvironment
{
    private const string Key=ModEntry.Tag+"EnvironmentTimeline/v1";
    private static string? CachedRaw;
    private static MachineEnvironmentJournal? Cached;
    private static readonly Dictionary<string,bool> Weather=new();
    internal static void Reset(){CachedRaw=null;Cached=null;Weather.Clear();LastClock=long.MinValue;}
    private static MachineEnvironmentJournal Read()
    {
        var farm=Game1.getFarm();farm.modData.TryGetValue(Key,out string raw);
        if(Cached!=null&&raw==CachedRaw)return Cached;
        Cached=raw==null?new():JsonSerializer.Deserialize<MachineEnvironmentJournal>(raw)??throw new InvalidOperationException("Missing timeline journal.");
        if(Cached.Version!=1)throw new InvalidOperationException("Unsupported timeline journal; preserved.");
        CachedRaw=raw;return Cached;
    }
    internal static MachineEnvironmentProfile At(string token,long at)=>Read().At(token,at);
    internal static List<FishChoice> Candidates(Chest c,ProductionState state,Farmer owner)
    {
        var probe=state.IdleCopy();probe.OwnerId=owner.UniqueMultiplayerID;
        var profile=Capture(c,probe);if(profile.Error!=null)return new();
        return profile.Fish.SelectMany(p=>p.Fish).DistinctBy(p=>p.Id).ToList();
    }
    internal static List<FishChoice> CandidatesAt(Chest c,ProductionState s,Farmer owner,long at)
    {
        if(at>=ProductionService.Now)return Candidates(c,s,owner);
        if(s.Continuity?.EnvironmentToken is {} token&&Read().Cycles.ContainsKey(token))
        {
            var profile=Read().At(token,at);if(profile.Error!=null)throw new InvalidOperationException(profile.Error);
            var fish=profile.Fish.SelectMany(p=>p.Fish).DistinctBy(p=>p.Id).ToList();
            return fish;
        }
        return new();
    }
    internal static bool HasLegalOtherAt(Chest c,ProductionState s,long at)
    {
        MachineEnvironmentProfile profile;
        if(at>=ProductionService.Now){var probe=s.IdleCopy();probe.OwnerId=c.owner.Value;profile=Capture(c,probe);}
        else if(s.Continuity?.EnvironmentToken is {} token&&Read().Cycles.ContainsKey(token))profile=Read().At(token,at);
        else return false;
        return profile.Error==null&&(profile.Other.Any(id=>s.Level==1||!MachineUpgrade.IsTrash(id))||s.Level>=2&&profile.Jelly!=null);
    }
    internal static bool ObservedQuota(string token,int day,string id)
    {
        if(!Read().Cycles.TryGetValue(token,out var frames))return false;
        var end=frames.LastOrDefault(w=>w.From<=(long)day*1440+1560);
        if(end==null)return false;
        return end.Profile.Fish.Any(p=>end.Profile.Day==day&&p.Fish.Any(f=>f.Id==id&&f.IsLegendary))
            ||end.Profile.SafeFish.GetValueOrDefault(MachineTimeline.Season(day))?.Any(p=>p.Fish.Any(f=>f.Id==id&&f.IsLegendary))==true;
    }

    private static void Save(MachineEnvironmentJournal journal)
    {string raw=JsonSerializer.Serialize(journal);Game1.getFarm().modData[Key]=raw;CachedRaw=raw;Cached=journal;}
    internal static MachineEnvironmentProfile Capture(Chest c,ProductionState s)
    {
        var profile=new MachineEnvironmentProfile{Location=c.Location.NameOrUniqueName,MachineId=c.modData.TryGetValue(ModEntry.Tag+"MachineId",out var machineId)?machineId:"",X=(int)c.TileLocation.X,Y=(int)c.TileLocation.Y,
            Day=MachineTimeline.Day(ProductionService.Now),Rain=c.Location.IsRainingHere(),UnknownWeather=Game1.timeOfDay<600};
        var owner=Game1.GetPlayer(s.OwnerId,onlyOnline:false);
        if(owner==null){profile.Error="Machine owner unavailable";return profile;}
        if(ModEntry.IsCrab(c))
        {
            var habitats=c.Location.GetCrabPotFishForTile(c.TileLocation).ToHashSet(StringComparer.Ordinal);
            profile.Crab=CrabRules.NativePool(DataLoader.Fish(Game1.content),habitats).ToList();
            profile.Sewer=BetterStardewValley.MutantLobsterKing.MutantService.Eligible(c.Location,c.TileLocation);
            profile.Ocean=habitats.Contains("ocean")&&!profile.Sewer;
            profile.KingCrabEnabled=BetterStardewValley.KingCrab.KingCrabService.Ready;
            if(profile.Crab.Count==0)profile.Error="No supported trap ecology";
        }
        else
        {
            var times=new SortedSet<int>{600,1800,2600};
            foreach(var text in DataLoader.Fish(Game1.content).Values)
            {
                var f=text.Split('/');if(f.Length<13||f[1]=="trap")continue;
                foreach(string t in f[5].Split(' '))if(int.TryParse(t,out int n)&&n>=600&&n<=2600)times.Add(n);
            }
            // TIME has inclusive upper bounds; an adjacent minute starts the next constant segment.
            var inherited=new HashSet<string>();
            void ReadTimes(StardewValley.GameData.Locations.LocationData? data,int depth)
            {
                if(data?.Fish==null||depth>4)return;
                foreach(var row in data.Fish)
                {
                    foreach(string atom in (row.Condition??"").Split(','))
                    {
                        var a=atom.Trim().TrimStart('!').Split(' ',StringSplitOptions.RemoveEmptyEntries);
                        if(a.Length is 2 or 3&&a[0]=="TIME")
                        {
                            if(int.TryParse(a[1],out int lo)&&lo>=600&&lo<2600)times.Add(lo);
                            if(a.Length==3&&int.TryParse(a[2],out int hi)&&hi>=600&&hi<2600){int m=hi/100*60+hi%100+1;times.Add(m/60*100+m%60);}
                        }
                    }
                    var query=(row.ItemId??"").Split(' ',StringSplitOptions.RemoveEmptyEntries);
                    if(query.Length==5&&query[0]=="LOCATION_FISH"&&inherited.Add(query[1]))ReadTimes(GameLocation.GetData(query[1]),depth+1);
                }
            }
            ReadTimes(GameLocation.GetData("Default"),0);ReadTimes(c.Location.GetData(),0);
            var boundaries=times.ToArray();
            for(int i=0;i<boundaries.Length-1;i++)profile.Fish.Add(new(boundaries[i],boundaries[i+1],FishPool.Get(c.Location,c.TileLocation,WorldService.GetFacing(c),owner,WorldService.IsExtended(c),s.Level,Game1.year,boundaries[i])));
            for(int season=0;season<4;season++)
            {
                int day=Game1.Date.TotalDays/112*112+season*28;
                profile.SafeFish[season]=Enumerable.Range(0,boundaries.Length-1).Select(i=>new TimedMachineFish(boundaries[i],boundaries[i+1],
                    FishPool.Get(c.Location,c.TileLocation,WorldService.GetFacing(c),owner,WorldService.IsExtended(c),s.Level,Game1.year,boundaries[i],day))).ToList();
            }
            if(profile.UnknownWeather)profile.Fish=profile.SafeFish.GetValueOrDefault(MachineTimeline.Season(profile.Day))??new();
            profile.Other=FishPool.Other(c.Location,c.TileLocation,WorldService.GetFacing(c),WorldService.IsExtended(c)).ToList();
            profile.Jelly=FishPool.Jelly(c.Location,c.TileLocation,WorldService.GetFacing(c),WorldService.IsExtended(c));
            if(profile.Other.Count==0)profile.Error="Machine water geometry is no longer valid";
        }
        return profile;
    }
    internal static void Start(Chest c,ProductionState s)
    {
        if(!Game1.IsMasterGame)throw new InvalidOperationException("Timeline observations require host.");
        var profile=Capture(c,s);if(profile.Error!=null)throw new InvalidOperationException(profile.Error);
        // Detach before writes: a failed save cannot leave a mutated cached history.
        var journal=JsonSerializer.Deserialize<MachineEnvironmentJournal>(JsonSerializer.Serialize(Read()))!;
        // Transfer observations before pruning the previous token. Historical starts must retain their original ecology.
        var previous=journal.Cycles.Where(p=>p.Key!=s.CycleToken&&p.Value.LastOrDefault()?.Profile.MachineId==profile.MachineId).SelectMany(p=>p.Value).Where(p=>p.From<=ProductionService.Now).OrderBy(p=>p.From).ToList();
        if(!journal.Cycles.ContainsKey(s.CycleToken)&&previous.Count>0)
        {
            var frames=previous.Where(p=>p.From>=s.StartedAt).ToList();var basis=previous.LastOrDefault(p=>p.From<s.StartedAt);
            if(basis!=null)frames.Insert(0,basis);
            journal.Cycles[s.CycleToken]=frames;
        }
        if(profile.MachineId.Length>0)foreach(string token in journal.Cycles.Where(p=>p.Key!=s.CycleToken&&p.Value.LastOrDefault()?.Profile.MachineId==profile.MachineId).Select(p=>p.Key).ToArray())journal.Cycles.Remove(token);
        journal.Record(s.CycleToken,ProductionService.Now,profile,false);Save(journal);
    }
    /// <summary>Drop the saved observations of a cycle (used when the calendar moved backwards).</summary>
    internal static void Forget(params string?[] tokens)
    {
        if(!Game1.IsMasterGame)return;
        var journal=JsonSerializer.Deserialize<MachineEnvironmentJournal>(JsonSerializer.Serialize(Read()))!;
        bool changed=false;foreach(var t in tokens)if(!string.IsNullOrEmpty(t))changed|=journal.Cycles.Remove(t);
        if(changed)Save(journal);
    }
    internal static void Ensure(Chest c,ProductionState s)
    {if(s.Timeline!=null&&!Read().Cycles.ContainsKey(s.CycleToken))Start(c,s);}
    private static long LastClock=long.MinValue;

    internal static bool WeatherChanged(ModEntry mod)
    {
        bool changed=LastClock!=ProductionService.Now;LastClock=ProductionService.Now;
        foreach(var location in mod.Machines.All.Select(p=>p.Value).Distinct())
        {bool rain=location.IsRainingHere();if(!Weather.TryGetValue(location.NameOrUniqueName,out bool old)||old!=rain)changed=true;Weather[location.NameOrUniqueName]=rain;}
        return changed;
    }
    internal static void Observe(ModEntry mod,bool closeDay=false)
    {
        if(!Game1.IsMasterGame)return;
        var journal=JsonSerializer.Deserialize<MachineEnvironmentJournal>(JsonSerializer.Serialize(Read()))!;
        foreach(var pair in mod.Machines.All)
        {
            ProductionState s;
            try{s=mod.Production.Read(pair.Key);}catch{continue;} // ProductionService reports invalid state; other machines still observe.
            if(s.Timeline==null)continue;
            if(s.Phase=="ready"){journal.Cycles.Remove(s.CycleToken);continue;}
            if(s.Phase!="working")continue;
            var p=Capture(pair.Key,s);journal.Record(s.CycleToken,ProductionService.Now,p,closeDay);
        }
        Save(journal);
    }
}

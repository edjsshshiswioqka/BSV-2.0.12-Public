// Explicit environmental boundary for inventory-only fake-game suites.
// Real journal/calendar/weather capture is tested separately; these fixtures are constant environments.
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Objects;
namespace Eddie.FishingMachine;
internal static class TimelineEnvironment
{
    internal static Func<string,long,MachineEnvironmentProfile>? ReadOverride;
    internal static List<FishChoice> Candidates(Chest c,ProductionState s,Farmer owner)
        =>FishPool.Get(c.Location,c.TileLocation,WorldService.GetFacing(c),owner,WorldService.IsExtended(c),s.Level,Game1.year);
    internal static List<FishChoice> CandidatesAt(Chest c,ProductionState s,Farmer owner,long at)=>Candidates(c,s,owner);
    private static readonly Dictionary<string,Chest> Sources=new();
    internal static void Start(Chest c,ProductionState s)=>Sources[s.CycleToken]=c;
    internal static void Ensure(Chest c,ProductionState s) {Sources[s.CycleToken]=c;}
    internal static bool HasLegalOtherAt(Chest c,ProductionState s,long at)=>FishPool.Other(c.Location,c.TileLocation,WorldService.GetFacing(c),WorldService.IsExtended(c)).Any(id=>s.Level==1||!MachineUpgrade.IsTrash(id));
    internal static bool ObservedQuota(string token,int day,string id)=>true;
    internal static MachineEnvironmentProfile At(string token,long at)
    {
        if(ReadOverride!=null)return ReadOverride(token,at);
        var c=Sources[token];var s=ProductionState.Load(c.modData[ProductionService.StateKey]);
        var owner=Game1.GetPlayer(s.OwnerId,false)!;
        return new(){Location=c.Location.NameOrUniqueName,X=(int)c.TileLocation.X,Y=(int)c.TileLocation.Y,Day=MachineTimeline.Day(at),
            Fish=new(){new(600,2600,Candidates(c,s,owner))},Other=FishPool.Other(c.Location,c.TileLocation,WorldService.GetFacing(c),WorldService.IsExtended(c)).ToList(),
            Crab=CrabRules.NativePool(DataLoader.Fish(Game1.content),c.Location.GetCrabPotFishForTile(c.TileLocation).ToHashSet()).ToList()};
    }
}

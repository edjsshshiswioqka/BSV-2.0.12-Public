using System.Globalization;
using Eddie.FishingMachine.Core;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.GameData.Locations;
using StardewValley.Tools;
using StardewValley.Locations;

namespace Eddie.FishingMachine;

/// <summary>Read-only fish table adapter. Never calls getFish, quest rewards, or item query side effects.</summary>
internal static class FishPool
{
    private static readonly HashSet<string> Excluded = new() { "159", "160", "163", "682", "775", "898", "899", "900", "901", "902" };
    internal static List<FishChoice> Get(GameLocation location, Vector2 anchor, Facing facing, Farmer owner, bool extended=false, int machineLevel=1, int catchYear=0, int? observedTime=null,int? historicalDay=null)
    {
        int clock=observedTime??Game1.timeOfDay;
        var fishData = DataLoader.Fish(Game1.content);
        var waterTile = SavedFootprint.WaterBobber(new((int)anchor.X,(int)anchor.Y),facing,p=>WorldService.Water(location,p),extended);
        if(!waterTile.HasValue)return new(); // A changed map must not consume bait for dry land.
        var castTile = waterTile.Value;
        var bankTile = DirectionRules.BankTiles(new((int)anchor.X,(int)anchor.Y),facing)[0];
        var bobber = new Vector2(castTile.X,castTile.Y);
        int depth = FishingRod.distanceToLand((int)bobber.X, (int)bobber.Y, location);
        var result = new Dictionary<string, double>();
        var visited = new HashSet<string>();
        void Read(LocationData? data, GameLocation environment, Vector2 cast, int castDepth, bool inherited, int recursion)
        {
            if (data?.Fish == null || recursion > 4) return;
            environment.TryGetFishAreaForTile(cast, out var area, out _);
            foreach (var spawn in data.Fish)
            {
                string raw = spawn.ItemId ?? "";
                if(MutantLobsterRules.IsItem(raw)||LobsterKingRules.IsFish(raw))continue;
                bool legendary=MachineLegendaryBridge.IsLegendary(raw);
                bool spirit=BetterStardewValley.SpiritFish.SpiritRules.IsFish(raw);
                if(spirit)continue; // Never enter the weighted pool through a data candidate.
                bool allowedLegend=legendary && (machineLevel>=2) && (historicalDay.HasValue?MachineLegendaryBridge.AvailableTimeline(owner,raw,catchYear,true):MachineLegendaryBridge.Available(owner,raw,catchYear>0?catchYear:Game1.year));
                if (legendary && !allowedLegend || spawn.IsBossFish && !allowedLegend || spawn.RequireMagicBait || spawn.CatchLimit >= 0 && !allowedLegend
                    || !string.IsNullOrEmpty(spawn.SetFlagOnCatch) || spawn.UseFishCaughtSeededRandom
                    || inherited && !spawn.CanBeInherited || spawn.Chance <= 0
                    || spawn.FishAreaId != null && spawn.FishAreaId != area
                    || spawn.Season.HasValue && (int)spawn.Season != (historicalDay.HasValue?MachineTimeline.Season(historicalDay.Value):(int)Game1.GetSeasonForLocation(environment))
                    || owner.fishingLevel.Value < spawn.MinFishingLevel || castDepth < spawn.MinDistanceFromShore
                    || spawn.MaxDistanceFromShore >= 0 && castDepth > spawn.MaxDistanceFromShore
                    || spawn.BobberPosition.HasValue && !spawn.BobberPosition.Value.Contains((int)cast.X, (int)cast.Y)
                    || spawn.PlayerPosition.HasValue && !spawn.PlayerPosition.Value.Contains(bankTile.X, bankTile.Y)
                    || !(historicalDay.HasValue?MachineTimeConditions.Historical(spawn.Condition,clock,historicalDay.Value):MachineTimeConditions.Check(spawn.Condition,clock,environment,owner))) continue;
                if (raw.StartsWith("LOCATION_FISH ", StringComparison.Ordinal))
                {
                    var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 5 && visited.Add(raw))
                    {
                        bool Parse(string value, string token, int current, out int number)
                        { if(value==token){number=current;return true;} return int.TryParse(value,out number); }
                        if (Parse(parts[2],"BOBBER_X",(int)cast.X,out int x) && Parse(parts[3],"BOBBER_Y",(int)cast.Y,out int y)
                            && Parse(parts[4],"WATER_DEPTH",castDepth,out int inheritedDepth))
                        {
                            var inheritedLocation=Game1.getLocationFromName(parts[1]) ?? environment;
                            Read(GameLocation.GetData(parts[1]),inheritedLocation,new(x,y),inheritedDepth,true,recursion+1);
                        }
                    }
                    continue;
                }
                string id = raw.StartsWith("(O)", StringComparison.Ordinal) ? raw[3..] : raw;
                // Only explicit object IDs from Data/Fish; queries, traps, flags and boss fish are deliberately excluded.
                if (Excluded.Contains(id) && !allowedLegend || !fishData.TryGetValue(id, out var text)) continue;
                var fields = text.Split('/');
                if (fields.Length < 13 || fields[1] == "trap") continue;
                if (!spawn.IgnoreFishDataRequirements)
                {
                    var times = fields[5].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    bool timeOk = false;
                    for (int i = 0; i + 1 < times.Length; i += 2)
                        if (int.TryParse(times[i], out int from) && int.TryParse(times[i + 1], out int to)
                            && clock >= from && clock < to) timeOk = true;
                    if (!timeOk || historicalDay.HasValue && fields[7] != "both"
                        || fields[7] == "rainy" && !environment.IsRainingHere()
                        || fields[7] == "sunny" && environment.IsRainingHere()
                        || !int.TryParse(fields[12], out int level) || owner.fishingLevel.Value < level) continue;
                }
                if (!double.TryParse(fields[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var weight)) continue;
                weight = Math.Clamp(weight, .001, 1) * Math.Clamp(spawn.Chance, .001f, 1);
                string qualified = "(O)" + id;
                if (ItemRegistry.Create(qualified) is not StardewValley.Object item || item.Category != -4 || item.questItem.Value) continue;
                result[qualified] = Math.Max(result.GetValueOrDefault(qualified), weight);
            }
        }
        // Mine special fish are selected in MineShaft.getFish, not in Data/Locations.
        if(location is MineShaft mine)
        {
            int area=mine.getMineArea();
            if(area!=80)
            {
                Read(GameLocation.GetData("Default"),location,bobber,depth,false,0);
                Read(GameLocation.GetData("UndergroundMine"),location,bobber,depth,false,0);
            }
            var special=CaveFishing.SpecialFish(area,owner.fishingLevel.Value,depth);
            if(special!=null && ItemRegistry.Create(special.Id) is StardewValley.Object item && item.Category==-4)
                result[special.Id]=special.Weight;
        }
        else
        {
            Read(GameLocation.GetData("Default"),location,bobber,depth,false,0);
            Read(location.GetData(),location,bobber,depth,false,0);
        }
        // Environment/quota availability only. Per-consumed-bait eligibility is checked in Production.Begin.
        if(BetterStardewValley.SpiritFish.SpiritRules.Eligible(owner.fishingLevel.Value,clock,location.NameOrUniqueName)
            && (historicalDay.HasValue?MachineLegendaryBridge.AvailableTimeline(owner,Production.SpiritId,catchYear,true):MachineLegendaryBridge.Available(owner,Production.SpiritId,catchYear>0?catchYear:Game1.year)))
            result[Production.SpiritId]=1;
        return result.Select(p => new FishChoice(p.Key, p.Value, MachineLegendaryBridge.IsLegendary(p.Key),ItemRegistry.Create<StardewValley.Object>(p.Key).Price)).ToList();
    }

    internal static string? Jelly(GameLocation location, Vector2 anchor, Facing facing, bool extended=false)
    {
        var water=SavedFootprint.WaterBobber(new((int)anchor.X,(int)anchor.Y),facing,p=>WorldService.Water(location,p),extended);
        if(!water.HasValue)return null;
        var tile=water.Value;
        location.TryGetFishAreaForTile(new(tile.X,tile.Y),out _,out var area);
        bool ocean=area?.CrabPotFishTypes?.Contains("ocean") ?? location.Name=="Beach";
        return CaveFishing.Jelly(location is MineShaft mine ? mine.getMineArea() : null,ocean);
    }

    internal static string[] Other(GameLocation location, Vector2 anchor, Facing facing, bool extended=false)
    {
        var waterTile=SavedFootprint.WaterBobber(new((int)anchor.X,(int)anchor.Y),facing,p=>WorldService.Water(location,p),extended);
        if(!waterTile.HasValue)return Array.Empty<string>();
        var tile=waterTile.Value;
        location.TryGetFishAreaForTile(new(tile.X,tile.Y),out _,out var area);
        bool ocean = area?.CrabPotFishTypes?.Contains("ocean") ?? location.Name == "Beach";
        if(location is MineShaft mine)
            return mine.getMineArea()==80
                ? new[]{"(O)167","(O)168","(O)169","(O)170","(O)171","(O)172"}
                : new[]{"(O)168","(O)169","(O)170","(O)171","(O)172","(O)157"};
        return new[] { "(O)168", "(O)169", "(O)170", "(O)171", "(O)172", ocean ? "(O)152" : "(O)153" };
    }
}

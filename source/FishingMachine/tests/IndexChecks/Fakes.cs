// Deliberately minimal test doubles for index bookkeeping. These are not game integration tests.
namespace StardewModdingAPI { public static class Context { public static bool IsMainPlayer=true,IsWorldReady=true; } }
namespace StardewValley
{
    public class Object { public bool Anchor; }
    public class GameLocation { public readonly Dictionary<string,Object> objects=new(); }
    public static class Utility
    {
        public static List<GameLocation> Locations=new();public static int Scans;
        public static void ForEachLocation(Func<GameLocation,bool> action,bool includeInteriors,bool includeGenerated)
        {Scans++;foreach(var l in Locations)if(!action(l))break;}
    }
}
namespace StardewValley.Objects { public class Chest:StardewValley.Object {} }
namespace StardewModdingAPI.Events
{
    public class ObjectListChangedEventArgs:EventArgs
    {
        public StardewValley.GameLocation Location=null!;
        public IEnumerable<KeyValuePair<string,StardewValley.Object>> Added=Array.Empty<KeyValuePair<string,StardewValley.Object>>();
        public IEnumerable<KeyValuePair<string,StardewValley.Object>> Removed=Array.Empty<KeyValuePair<string,StardewValley.Object>>();
    }
}
namespace Eddie.FishingMachine { internal static class ModEntry {internal static bool IsAnchor(StardewValley.Object item)=>item is StardewValley.Objects.Chest && item.Anchor;} }

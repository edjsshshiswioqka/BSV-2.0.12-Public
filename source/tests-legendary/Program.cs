using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BetterStardewValley;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.GameData.Locations;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }

    private static void Main()
    {
        Check(AnnualLegendaryRules.FishIds.Length == 5 && AnnualLegendaryRules.FishIds.Distinct().Count() == 5,
            "exactly five distinct original legendary fish");
        Check(AnnualLegendaryRules.FishIds.All(id => AnnualLegendaryRules.NormalizeId(id) == id
            && AnnualLegendaryRules.NormalizeId("(O)" + id) == id), "qualified and unqualified fish IDs agree");
        Check(AnnualLegendaryRules.NormalizeId("(O)145") is null, "ordinary fish excluded");
        Check(AnnualLegendaryRules.NormalizeId("(O)900") is null && AnnualLegendaryRules.NormalizeId("(O)898") is null,
            "Qi quest legendary fish excluded");
        Check(AnnualLegendaryRules.NormalizeId("(BC)163") is null && AnnualLegendaryRules.NormalizeId(null) is null,
            "unrelated types and null IDs excluded");

        var never = AnnualLegendaryRules.ReadRecord(null, 0, 1);
        Check(AnnualLegendaryRules.IsAvailable(never, 1), "uncaught fish available in year one");
        var spring28Year1 = new LegendaryCatchRecord(1, 1);
        Check(!AnnualLegendaryRules.IsAvailable(spring28Year1, 1), "same spring after catching is blocked");
        Check(!AnnualLegendaryRules.IsAvailable(spring28Year1, 1), "summer/fall/winter in same year do not reset quota");
        Check(AnnualLegendaryRules.IsAvailable(spring28Year1, 2), "spring 28 year one to spring 1 year two is eligible");
        var winter28Year1 = new LegendaryCatchRecord(1, 1);
        Check(AnnualLegendaryRules.IsAvailable(winter28Year1, 2), "winter 28 to next spring 1 resets immediately without 112-day cooldown");
        var nextCatch = new LegendaryCatchRecord(2, 2);
        Check(!AnnualLegendaryRules.IsAvailable(nextCatch, 2) && AnnualLegendaryRules.IsAvailable(nextCatch, 3),
            "year two catch blocks rest of year and permits year three");
        Check(!AnnualLegendaryRules.IsAvailable(nextCatch, 1), "clock moved backward cannot bypass quota");
        Check(AnnualLegendaryRules.IsAvailable(spring28Year1, 9), "skipped years still permit a catch");
        Check(!AnnualLegendaryRules.IsAvailable(new LegendaryCatchRecord(9, 2), 9), "unused years do not accumulate extra catches");

        var oldSave = AnnualLegendaryRules.ReadRecord(null, 1, 4);
        Check(oldSave.Year == 4 && !AnnualLegendaryRules.IsAvailable(oldSave, 4), "undated old save imported as caught this year");
        Check(AnnualLegendaryRules.IsAvailable(oldSave, 5), "imported old catch unlocks next year");
        Check(AnnualLegendaryRules.IsAvailable(AnnualLegendaryRules.ReadRecord(null, 0, 4), 4), "old-save fish never caught remains eligible immediately");
        var reload = AnnualLegendaryRules.ReadRecord(nextCatch.Serialize(), 2, 2);
        Check(reload == nextCatch && !AnnualLegendaryRules.IsAvailable(reload, 2), "save reload preserves exhausted quota");
        Check(AnnualLegendaryRules.ReadRecord(nextCatch.Serialize(), 2, 3).Year == 2,
            "normal year change does not rewrite prior catch year");
        Check(AnnualLegendaryRules.ReadRecord("bad record", 1, 3).Year == 3,
            "malformed metadata cannot grant an extra catch");
        Check(AnnualLegendaryRules.ReadRecord("1:1", 2, 3).Year == 3,
            "untracked catch while mod disabled is imported conservatively");
        Check(AnnualLegendaryRules.ReadRecord("3:2", 1, 3).Year == 3,
            "external decrease of lifetime count does not reset current quota");

        var playerOne = new Dictionary<string, string> { ["163"] = "2:1", ["159"] = "0:0" };
        var playerTwo = new Dictionary<string, string> { ["163"] = "0:0" };
        Check(!AnnualLegendaryRules.IsAvailable(AnnualLegendaryRules.ReadRecord(playerOne["163"], 1, 2), 2)
            && AnnualLegendaryRules.IsAvailable(AnnualLegendaryRules.ReadRecord(playerTwo["163"], 0, 2), 2),
            "players have independent annual quotas");
        Check(AnnualLegendaryRules.IsAvailable(AnnualLegendaryRules.ReadRecord(playerOne["159"], 0, 2), 2),
            "catching Legend does not use Crimsonfish quota");
        string savedJson = JsonSerializer.Serialize(playerOne);
        var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(savedJson)!;
        Check(loaded["163"] == "2:1" && loaded["159"] == "0:0", "annual metadata survives serialization");

        Check(AnnualLegendaryRules.IsSuccessfulCatch(false, 1, 1, 2), "completed catch consumes quota even without a new size record");
        Check(!AnnualLegendaryRules.IsSuccessfulCatch(false, 1, 1, 1), "failed or escaped fish does not consume quota");
        Check(!AnnualLegendaryRules.IsSuccessfulCatch(true, 1, 1, 2), "fish pond retrieval does not consume wild catch quota");
        Check(!AnnualLegendaryRules.IsSuccessfulCatch(false, 0, 1, 1), "zero-result attempt does not consume quota");
        Check(AnnualLegendaryRules.IsSuccessfulCatch(false, 3, 0, 3)
            && !AnnualLegendaryRules.IsAvailable(new LegendaryCatchRecord(2, 3), 2),
            "challenge bait multi-fish catch uses the single annual catch event");

        var legend = new SpawnFishData
        {
            Id = "Legend", ItemId = "(O)163", CatchLimit = 1, IsBossFish = true,
            Season = Season.Spring, Chance = 0.1f, Condition = "LOCATION_SEASON Here spring, WEATHER Here rain",
            MinFishingLevel = 10, MinDistanceFromShore = 5, MaxDistanceFromShore = 10,
            PlayerPosition = new Rectangle(1, 2, 3, 4), BobberPosition = new Rectangle(5, 6, 7, 8),
            FishAreaId = "Lake", CanUseTrainingRod = false, Precedence = -100,
            IgnoreFishDataRequirements = false, SetFlagOnCatch = "example_original_flag"
        };
        var originalProperties = typeof(SpawnFishData).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.Name != nameof(SpawnFishData.CatchLimit))
            .ToDictionary(p => p, p => p.GetValue(legend));
        var normal = new SpawnFishData { Id = "normal", ItemId = "(O)145", CatchLimit = 12 };
        var quest = new SpawnFishData { Id = "LegendII", ItemId = "(O)900", CatchLimit = -1, Condition = "PLAYER_HAS_SPECIAL_ORDER Current QiChallenge9" };
        var locations = new Dictionary<string, LocationData>
        {
            ["Mountain"] = new LocationData { Fish = new List<SpawnFishData> { legend, normal, quest } },
            ["Empty"] = new LocationData { Fish = null }
        };
        Check(AnnualLegendaryData.RemoveLifetimeLimits(locations) == 1 && legend.CatchLimit == -1,
            "only original legendary lifetime cap removed");
        Check(originalProperties.All(p => Equals(p.Value, p.Key.GetValue(legend))),
            "all other fish spawn properties unchanged, including season/weather/area/skill/chance");
        Check(normal.CatchLimit == 12 && quest.CatchLimit == -1 && quest.Condition == "PLAYER_HAS_SPECIAL_ORDER Current QiChallenge9",
            "ordinary and Qi quest spawn rules preserved");
        Check(AnnualLegendaryData.RemoveLifetimeLimits(locations) == 0, "repeated data edit is idempotent");
        Console.WriteLine($"{checks} legendary-fish checks passed. No in-game playtest was performed.");
    }
}

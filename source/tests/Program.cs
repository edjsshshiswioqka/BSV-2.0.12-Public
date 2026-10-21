using System;
using BetterStardewValley;

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
        // These scenarios protect against the crop-removal/harvest-success distinction.
        Check(HarvestRules.IsReady(false, false, 5, 4, false, 0), "mature first harvest");
        Check(HarvestRules.IsReady(false, false, 5, 4, true, 0), "mature repeat harvest");
        Check(!HarvestRules.IsReady(false, false, 5, 3, false, 0), "immature crop ignored");
        Check(!HarvestRules.IsReady(true, false, 5, 4, false, 0), "dead crop ignored even if removed by Junimo");
        Check(!HarvestRules.IsReady(false, true, 5, 4, true, 0), "natural forage ignored");
        Check(!HarvestRules.IsReady(false, false, 0, 0, false, 0), "invalid empty phases ignored");
        Check(!HarvestRules.IsReady(false, false, 5, 4, true, 3), "growing repeat crop ignored");
        Check(HarvestRules.WasHarvested(true, true, true, -1, false, false, 0, 0), "single-harvest removal succeeds");
        Check(HarvestRules.WasHarvested(true, true, false, 4, true, true, 0, 4), "strawberry succeeds despite false return");
        Check(HarvestRules.WasHarvested(true, true, false, 4, true, true, -1, 4), "overdue regrowth succeeds");
        Check(HarvestRules.WasHarvested(true, true, false, 4, false, true, 8, 4), "delayed FIRST strawberry harvest succeeds");
        Check(HarvestRules.WasHarvested(true, true, false, 4, false, true, 4, 4), "first harvest on matching day still succeeds");
        Check(!HarvestRules.WasHarvested(true, true, false, -1, false, false, 0, 0), "full bag failed harvest gives nothing");
        Check(!HarvestRules.WasHarvested(true, true, false, 4, true, true, 0, 0), "full bag failed repeat harvest gives nothing");
        Check(!HarvestRules.WasHarvested(true, true, false, 4, false, false, 4, 4), "failed FIRST harvest on matching day gives nothing");
        Check(!HarvestRules.WasHarvested(false, true, true, -1, false, false, 0, 0), "not ready cannot award");
        Check(!HarvestRules.WasHarvested(true, false, true, -1, false, false, 0, 0), "suppressed original cannot award");
        Check(!HarvestRules.WasHarvested(true, true, false, 4, true, true, 4, 4), "unchanged repeat state cannot award");
        Check(HarvestRules.Wins(0.499999, 0.5), "roll below 50 percent wins");
        Check(!HarvestRules.Wins(0.5, 0.5), "roll at threshold loses");
        Check(!HarvestRules.Wins(0, 0), "zero percent never wins");
        Check(HarvestRules.Wins(0.999999, 1), "one hundred percent always wins");
        Check(HarvestRules.NormalizeChance(double.NaN) == 0.5, "NaN config resets to default");
        Check(HarvestRules.NormalizeChance(double.PositiveInfinity) == 0.5, "infinite config resets to default");
        Check(HarvestRules.NormalizeChance(-0.1) == 0 && HarvestRules.NormalizeChance(1.1) == 1, "config range clamped");

        var random = new Random(29092026);
        int wins = 0;
        for (int i = 0; i < 100000; i++)
            if (HarvestRules.Wins(random.NextDouble(), 0.5)) wins++;
        Check(wins > 49000 && wins < 51000, $"100000 seeded rolls: {wins} bonuses");
        Console.WriteLine($"{checks} checks passed. This is a rule test, not an in-game playtest.");
    }
}

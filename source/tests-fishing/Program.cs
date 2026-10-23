// BSV fishing machine: level values, upgrade path, partial cycles and "no junk-only" checks.
// Pure rules (Eddie.FishingMachine.Core); no game running.
using Eddie.FishingMachine.Core;

int passed = 0, failed = 0;
void Check(bool ok, string name) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL: " + name); } }
void Near(double got, double want, double tol, string name) => Check(Math.Abs(got - want) <= tol, $"{name}: {got:0.000} (expected {want:0.000} ± {tol:0.000})");

var cfg = new BalanceConfig().ForNewCycle();
cfg.Validate();

// ---- 1. Static values per level ----
var levels = new[] { 1, 2, 3 };
int[] bait = { 200, 300, 400 }, minutes = { 2880, 4320, 5760 }, slots = { 36, 70, 70 }, cycles = { 15, 20, 0 };
foreach (int lv in levels)
{
    var s = new ProductionState { Level = lv };
    Check(MachineUpgrade.RequiredBait(s, cfg) == bait[lv - 1], $"Lv{lv} full cycle bait = {bait[lv - 1]}");
    Check(cfg.MinutesForLevel(lv) == minutes[lv - 1], $"Lv{lv} cycle length = {minutes[lv - 1]} min");
    Check(MachineUpgrade.Capacity(lv) == slots[lv - 1], $"Lv{lv} slots = {slots[lv - 1]}");
    Check(MachineCycleRules.RequiredCycles(MachineKinds.Fishing, lv) == cycles[lv - 1], $"Lv{lv} cycles to next level = {cycles[lv - 1]}");
}
Check(MachineUpgrade.Requirements(1).SequenceEqual(new[] { ("(O)335", 25), ("(O)336", 15), ("(O)787", 3), ("(O)709", 10) }), "Lv1->2 materials");
Check(MachineUpgrade.Requirements(2).SequenceEqual(new[] { ("(O)337", 15), ("(O)336", 25), ("(O)787", 8), ("(T)IridiumRod", 6) }), "Lv2->3 materials");

// ---- 2. Upgrade path: progress + materials -> plan; missing anything -> no plan ----
UpgradeSlot[] Mats(int lv, bool rodsSafe = true) => MachineUpgrade.Requirements(lv).SelectMany((r, i) =>
    r.Id.StartsWith("(T)") ? Enumerable.Range(0, r.Count).Select(k => new UpgradeSlot(i * 10 + k, r.Id, 1, rodsSafe)) : new[] { new UpgradeSlot(i * 10, r.Id, r.Count) }).ToArray();
Check(MachineUpgrade.Plan(new ProductionState { Level = 1, CompletedCycles = 15 }, Mats(1)) != null, "Lv1 upgrades at 15 cycles with materials");
Check(MachineUpgrade.Plan(new ProductionState { Level = 1, CompletedCycles = 14 }, Mats(1)) == null, "Lv1 does not upgrade at 14 cycles");
Check(MachineUpgrade.Plan(new ProductionState { Level = 1, CompletedCycles = 15 }, Mats(1).Skip(1)) == null, "Lv1 does not upgrade without iron bars");
Check(MachineUpgrade.Plan(new ProductionState { Level = 2, CompletedCycles = 99, LevelCycles = 20 }, Mats(2)) != null, "Lv2 upgrades after 20 Lv2 cycles with materials");
Check(MachineUpgrade.Plan(new ProductionState { Level = 2, CompletedCycles = 99, LevelCycles = 19 }, Mats(2)) == null, "Lv2 needs 20 cycles at Lv2 (not lifetime)");
Check(MachineUpgrade.Plan(new ProductionState { Level = 2, CompletedCycles = 99, LevelCycles = 20 }, Mats(2, false)) == null, "Lv2 refuses unsafe (enchanted/used) iridium rods");
Check(MachineUpgrade.Plan(new ProductionState { Level = 3, CompletedCycles = 99, LevelCycles = 99 }, Mats(2)) == null, "Lv3 is max");
Check(MachineUpgrade.Plan(new ProductionState { Level = 1, CompletedCycles = 15, Phase = "working" }, Mats(1)) == null, "no upgrade in the middle of a cycle");

// ---- 3. Cycles complete and count; partial loads add up ----
long start = CycleClock.ToAbsoluteMinutes(10, 600);
{
    var s = new ProductionState(); long t = start;
    for (int k = 0; k < 15; k++)
    {
        var run = MachineTimeline.StartFish(s, cfg, new[] { new BaitSpend(0, 200, BaitKind.Normal, null) }, 1, t, 100 + k);
        run.Timeline!.Cursor = run.Timeline.Plan.Count; run.FishCount = run.BaitUsed;
        Check(run.Complete(run.DueAt), "full cycle completes");
        s = run.IdleCopy(); t = run.DueAt;
    }
    Check(s.CompletedCycles == 15 && MachineUpgrade.Plan(s, Mats(1)) != null, "15 full Lv1 cycles unlock the Lv2 upgrade");
    var credit = new ProductionState(); t = start;
    for (int k = 0; k < 8; k++)
    {
        var run = MachineTimeline.StartFish(credit, cfg, new[] { new BaitSpend(0, 50, BaitKind.Normal, null) }, 1, t, 7 + k);
        run.Timeline!.Cursor = run.Timeline.Plan.Count; run.FishCount = run.BaitUsed;
        run.Complete(run.DueAt); credit = run.IdleCopy(); t = run.DueAt;
    }
    Check(credit.CompletedCycles == 2 && credit.BaitCredit == 0, "8 loads of 50 bait = 2 full Lv1 cycles");
    foreach (int n in new[] { 1, 7, 50, 199, 200 })
    {
        var run = MachineTimeline.StartFish(new(), cfg, new[] { new BaitSpend(0, n, BaitKind.Normal, null) }, 1, start, 9);
        Check(MachineTimeline.Valid(run) && run.BaitUsed == n && run.Timeline!.Plan.Count == n, $"{n} bait = {n} casts");
        Check(run.Timeline!.Plan.All(o => MachineTimeline.Time(o.At) is >= 600 and < 2600), $"{n} bait casts only between 06:00 and 02:00");
    }
}

// ---- 4. Rates per level (one cast at a time, like the running machine) ----
var fishes = new List<FishChoice> { new("(O)129", .3, false, 30), new("(O)131", .3, false, 30), new("(O)147", .2, false, 50), new("(O)701", .2, false, 70) };
var other = new List<string> { "(O)167", "(O)168", "(O)169", "(O)170", "(O)171", "(O)172", "(O)152" };
Console.WriteLine("level bait     fish%  junk%  fish/cast  normal silver gold iridium  treasure");
foreach (int lv in levels)
    foreach (var kind in new[] { BaitKind.Normal, BaitKind.Deluxe })
    {
        int casts = 60000, fishCasts = 0, junk = 0; long fishItems = 0; var q = new long[5]; int treasure = 0;
        var prev = new ProductionState { Level = lv };
        for (int i = 0; i < casts; i++)
        {
            var u = Production.Begin(cfg, new[] { new BaitSpend(0, 1, kind, null) }, fishes, other, start, 1000 + i * 7919, prev, "(O)SeaJelly", true);
            fishCasts += u.FishCount; junk += u.Pending.Count(p => p.Id.StartsWith("(O)16") || p.Id.StartsWith("(O)17")) > 0 ? 1 : 0;
            foreach (var p in u.Pending.Where(p => fishes.Any(f => f.Id == p.Id))) { fishItems += p.Count; q[p.Quality] += p.Count; }
            treasure += u.NormalTreasures + u.AdvancedTreasures;
        }
        double fishShare = fishCasts / (double)casts;
        Console.WriteLine($"Lv{lv}   {kind,-7}  {fishShare * 100,5:0.0}  {junk * 100.0 / casts,5:0.0}  {fishItems / (double)casts,8:0.00}   {q[0] * 100.0 / fishItems,5:0.0} {q[1] * 100.0 / fishItems,5:0.0} {q[2] * 100.0 / fishItems,5:0.0} {q[4] * 100.0 / fishItems,5:0.0}   {treasure * 100.0 / casts,5:0.00}%");
        var r = cfg.GetRates(kind, lv);
        double expected = lv == 1 ? r.FishShare : r.Fish / (r.Fish + r.OtherItem * (0.10 + 0.90 * (1.0 / 7)));
        Near(fishShare, expected, .01, $"Lv{lv} {kind} fish share");
        if (lv >= 2) Check(junk == 0, $"Lv{lv} never gives junk");
    }
// Monotonic: every level is better than the one before.
double Mult(int lv) => 1 + (lv == 1 ? cfg.FishCriticalChance : lv == 2 ? cfg.LevelTwoCriticalChance + 2 * cfg.LevelTwoTripleChance : cfg.LevelThreeCriticalChance + 2 * cfg.LevelThreeTripleChance);
Check(Mult(1) < Mult(2) && Mult(2) < Mult(3), $"fish per catch rises: {Mult(1):0.00} < {Mult(2):0.00} < {Mult(3):0.00}");
Check(MachineLevelRules.TreasureShares(1).Sum() < MachineLevelRules.TreasureShares(2).Sum() && MachineLevelRules.TreasureShares(2).Sum() < MachineLevelRules.TreasureShares(3).Sum(), "treasure chance rises with level");
Check(CycleLegendaryRules.Chance(1) == 0 && CycleLegendaryRules.Chance(2) == .05 && CycleLegendaryRules.Chance(3) == .15, "legendary chance per cycle 0 / 5% / 15%");
foreach (int lv in levels) Near(MachineLevelRules.QualityShares(lv).Sum(), 1, 1e-9, $"Lv{lv} quality shares add up to 100%");

// ---- 5. Unwatched time never turns into junk-only ----
{
    var j = new MachineEnvironmentJournal();
    MachineEnvironmentProfile P(int day) => new() { Location = "Beach", Day = day, Fish = new() { new(600, 1800, new() { new("(O)129", 1) }), new(1800, 2600, new() { new("(O)151", 1) }) }, Other = new() { "(O)168" } };
    j.Record("c", 20L * 1440 + 700, P(20), false); j.Record("c", 20L * 1440 + 900, P(20), false);
    var before = j.At("c", 20L * 1440 + 400);   // catch-up before the first observation
    var night = j.At("c", 20L * 1440 + 1300);   // later the same day, not observed yet
    var nextDay = j.At("c", 21L * 1440 + 500);  // a day nobody watched, same season
    Check(before.Fish.Any(w => w.Fish.Count > 0) && night.Fish.Any(w => w.Fish.Count > 0) && nextDay.Fish.Any(w => w.Fish.Count > 0), "unwatched time still has the local fish list");
    Check(night.Fish.Single(w => 2100 >= w.From && 2100 < w.Until).Fish[0].Id == "(O)151", "after 18:00 only after-18:00 fish");
}

Console.WriteLine($"{{\"suite\":\"fishing-machine\",\"passed\":{passed},\"failed\":{failed},\"status\":\"{(failed == 0 ? "PASS" : "FAIL")}\"}}");
return failed == 0 ? 0 : 1;

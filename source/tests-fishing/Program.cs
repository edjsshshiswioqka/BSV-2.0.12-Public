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
    // A cycle needs a full load: 199 bait cannot start a Lv1 cycle, 200 can.
    foreach (int n in new[] { 1, 199, 201 })
    {
        bool refused = false;
        try { MachineTimeline.StartFish(new(), cfg, new[] { new BaitSpend(0, n, BaitKind.Normal, null) }, 1, start, 9); } catch (ArgumentException) { refused = true; }
        Check(refused, $"Lv1 refuses to start with {n} bait");
    }
    var full = MachineTimeline.StartFish(new(), cfg, new[] { new BaitSpend(0, 200, BaitKind.Normal, null) }, 1, start, 9);
    Check(MachineTimeline.Valid(full) && full.Timeline!.Plan.Count == 200 && full.DueAt == start + 2880, "Lv1 starts with exactly 200 bait for 2 days");
    Check(full.Timeline!.Plan.All(o => MachineTimeline.Time(o.At) is >= 600 and < 2600), "casts only between 06:00 and 02:00");
    var lv2 = MachineTimeline.StartFish(new ProductionState { Level = 2, CompletedCycles = 15 }, cfg, new[] { new BaitSpend(0, 300, BaitKind.Normal, null) }, 1, start, 9);
    Check(MachineTimeline.Valid(lv2) && lv2.DueAt == start + 4320, "Lv2 starts with 300 bait for 3 days");
    // Switched off half way: the bait already fished counts towards the next full cycle.
    var half = MachineTimeline.StartFish(new(), cfg, new[] { new BaitSpend(0, 200, BaitKind.Normal, null) }, 1, start, 9);
    half.Timeline!.Cursor = 100; half.FishCount = 100; half.BaitUsed = 100; half.Timeline.Plan = half.Timeline.Plan.Take(100).ToList();
    half.Complete(half.DueAt);
    Check(half.CompletedCycles == 0 && half.BaitCredit == 100, "a cycle cut short at 100 bait is half a cycle, not a whole one");
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

// ---- 6. Mixed bait: any mix that adds up to a full load starts; each bait keeps its own quality ----
{
    var mix = new[] { new BaitSlot(0, 120, BaitKind.Normal), new BaitSlot(3, 50, BaitKind.Deluxe), new BaitSlot(5, 40, BaitKind.Targeted, "(O)999") };
    var plan = Production.PlanBait(mix, 200, new HashSet<string> { "(O)129" });
    Check(plan != null && plan.Sum(p => p.Count) == 200 && plan.Count == 3, "120 bait + 50 deluxe + 30 targeted (fish not here now) make one Lv1 load");
    Check(Production.PlanBait(mix.Take(2), 200, new HashSet<string>()) == null, "120 + 50 = 170 mixed bait still cannot start Lv1");
    var run = MachineTimeline.StartFish(new(), cfg, plan!, 1, start, 11);
    Check(MachineTimeline.Valid(run) && run.Timeline!.Plan.Count == 200, "a mixed load starts a normal 2-day cycle");
    Console.WriteLine("level bait       normal silver gold iridium   (quality of fish caught)");
    foreach (int lv in levels)
    {
        double lastGood = -1;
        foreach (var kind in new[] { BaitKind.Normal, BaitKind.Targeted, BaitKind.Deluxe })
        {
            var q = new long[5]; long n = 0;
            var prev = new ProductionState { Level = lv };
            string? target = kind == BaitKind.Targeted ? "(O)129" : null;
            for (int i = 0; i < 30000; i++)
            {
                var u = Production.Begin(cfg, new[] { new BaitSpend(0, 1, kind, target) }, fishes, other, start, 5000 + i * 7919, prev, "(O)SeaJelly", true);
                foreach (var p in u.Pending.Where(p => fishes.Any(f => f.Id == p.Id))) { q[p.Quality] += p.Count; n += p.Count; }
            }
            var want = MachineLevelRules.QualityShares(lv, 1, kind);
            Console.WriteLine($"Lv{lv}   {kind,-9}  {q[0] * 100.0 / n,5:0.0} {q[1] * 100.0 / n,5:0.0} {q[2] * 100.0 / n,5:0.0} {q[4] * 100.0 / n,5:0.0}");
            Near(q[0] / (double)n, want[0], .015, $"Lv{lv} {kind} normal quality share");
            Near(q[4] / (double)n, want[3], .015, $"Lv{lv} {kind} iridium share");
            double good = (q[2] + 2.0 * q[4]) / n + q[1] / (2.0 * n);
            Check(good > lastGood, $"Lv{lv}: {kind} bait gives better quality than the bait tier below"); lastGood = good;
        }
    }
}

// ---- 7. Date moved backwards (cheat menu): a running Lv3 cycle and an idle machine carry on from today ----
{
    long then = CycleClock.ToAbsoluteMinutes(5263, 600) + 130, now = CycleClock.ToAbsoluteMinutes(5177, 600) + 60;
    var lv3 = MachineTimeline.StartFish(new ProductionState { Level = 3, CompletedCycles = 36 }, cfg, new[] { new BaitSpend(0, 300, BaitKind.Normal, null), new BaitSpend(4, 100, BaitKind.Deluxe, null) }, 1, then, 21);
    lv3.Continuity = new MachineContinuity { ObservedAt = then, NextAt = lv3.DueAt, Stopped = false, Fuel = new() { new("bait", then - 30, 50) }, Policies = new() { new MachinePolicy { At = then - 30, Auto = true } } };
    Check(MachineRewind.Needed(lv3, now), "a cycle that started 86 days in the future is noticed");
    long d = MachineRewind.Delta(lv3, now); var moved = lv3.Snapshot(); MachineRewind.Shift(moved, d);
    Check(d % 1440 == 0 && moved.StartedAt <= now && moved.StartedAt > now - 1440 && !MachineRewind.Needed(moved, now), "times move back by whole days to today");
    Check(MachineTimeline.Valid(moved) && moved.Continuity!.Valid(), "the moved cycle is still a valid paid cycle");
    Check(moved.Timeline!.Plan.Select(o => MachineTimeline.Time(o.At)).SequenceEqual(lv3.Timeline!.Plan.Select(o => MachineTimeline.Time(o.At))), "every cast keeps its time of day");
    Check(moved.Timeline.Plan.Count == 400 && moved.DueAt - moved.StartedAt == 5760, "still 400 bait over 4 days");
    Check(moved.Timeline.Plan.Any(o => o.At <= now + 1440), "casts happen again from today, not in 86 days");
    var idle = new ProductionState { Continuity = new MachineContinuity { ObservedAt = then, NextAt = then, Fuel = new() { new("bait", then, 200) }, Policies = new() { new MachinePolicy { At = then } } } };
    MachineRewind.Shift(idle, MachineRewind.Delta(idle, now));
    Check(idle.Continuity!.Valid() && idle.Continuity.Fuel.All(f => f.At <= now) && idle.Continuity.NextAt <= now, "an idle machine's bait counts again today");
    Check(!MachineRewind.Needed(moved, moved.StartedAt + 10) && MachineRewind.Delta(new ProductionState(), now) == 0, "normal play never triggers it");
}

// ---- 8. Switching off mid-cycle: fishing and crab (with and without Luremaster) ----
{
    void Cast(ProductionState st, int n) { st.Timeline!.Cursor = n; st.FishCount = n; st.Pending = new() { new("(O)129", n) }; }
    var fish = MachineTimeline.StartFish(new(), cfg, new[] { new BaitSpend(0, 150, BaitKind.Normal, null), new BaitSpend(2, 50, BaitKind.Deluxe, null) }, 1, start, 31);
    Cast(fish, 120);
    var cut = MachineStop.Cut(fish, fish.Timeline!.Plan[119].At + 1);
    Check(cut != null && MachineTimeline.Valid(cut.Value.State) && cut.Value.Refund.Sum(r => r.Count) == 80 && cut.Value.State.BaitUsed == 120, "fishing: 120 cast, 80 bait back");
    Check(cut != null && !MachineStop.WouldCut(cut.Value.State), "fishing: nothing more to cut after switching off");
    var crabCfg = new CrabBalanceConfig();
    foreach (bool lure in new[] { false, true })
    {
        int need = crabCfg.Required(1, lure);
        var crab = MachineTimeline.StartCrab(new ProductionState { Kind = MachineKinds.Crab }, crabCfg, new[] { new CrabBait(0, "(O)685", need - 20, false), new CrabBait(3, "(O)DeluxeBait", 20, true) }, 1, lure, false, start, 41);
        int rolls = crab.Timeline!.Plan.Count; int done = rolls / 2 + 1;
        crab.Timeline.Cursor = done; crab.FishCount = done; crab.Pending = new() { new("(O)717", done) };
        var c2 = MachineStop.Cut(crab, crab.Timeline.Plan[done - 1].At + 1);
        int per = lure ? 2 : 1;
        Check(c2 != null && MachineTimeline.Valid(c2.Value.State), $"crab{(lure ? " (Luremaster)" : "")}: cut cycle stays valid");
        if (c2 == null) continue;
        int kept = c2.Value.State.Timeline!.Plan.Count;
        Check(kept >= done && kept - done < 3 && c2.Value.State.BaitUsed * per == kept, $"crab{(lure ? " (Luremaster)" : "")}: keeps the {done} checks done{(lure ? " plus the second check of half-used bait" : "")}");
        Check(c2.Value.State.BaitUsed + c2.Value.Refund.Sum(r => r.Count) == need, $"crab{(lure ? " (Luremaster)" : "")}: used + returned bait = {need}, nothing lost or duplicated");
    }
    var fresh = MachineTimeline.StartFish(new(), cfg, new[] { new BaitSpend(0, 200, BaitKind.Normal, null) }, 1, start, 51);
    var none = MachineStop.Cut(fresh, start);
    Check(none != null && none.Value.State.Phase == "idle" && none.Value.Refund.Sum(r => r.Count) == 200, "switched off before the first cast: all 200 bait back, machine idle");
}

// ---- 9. Spirit Fish in the machine: Giant Lobster Bait only; the machine's crits still apply (1-3 per catch) ----
{
    var pool = new List<FishChoice>(fishes) { new(Production.SpiritId, 1, true, 6000) };
    int spirits = 0, wrong = 0;
    foreach (int lv in new[] { 2, 3 })
        for (int i = 0; i < 40000; i++)
        {
            var u = Production.Begin(cfg, new[] { new BaitSpend(0, 1, BaitKind.GiantLobster, null) }, pool, other, start, 77 + i * 7919, new ProductionState { Level = lv }, "(O)SeaJelly", true);
            foreach (var p in u.Pending.Where(p => p.Id == Production.SpiritId)) { spirits++; if (p.Count < 1 || p.Count > 3) wrong++; }
        }
    Check(spirits > 0, $"Spirit Fish can be caught with Giant Lobster Bait ({spirits} catches in 80000 casts)");
    Check(wrong == 0, "a machine Spirit Fish catch is 1-3 fish (crits kept, never more)");
    int other_bait = 0;
    for (int i = 0; i < 40000; i++)
        other_bait += Production.Begin(cfg, new[] { new BaitSpend(0, 1, BaitKind.Deluxe, null) }, pool, other, start, 99 + i * 7919, new ProductionState { Level = 3 }, "(O)SeaJelly", true).Pending.Count(p => p.Id == Production.SpiritId);
    Check(other_bait == 0, "no Spirit Fish without Giant Lobster Bait");
}

Console.WriteLine($"{{\"suite\":\"fishing-machine\",\"passed\":{passed},\"failed\":{failed},\"status\":\"{(failed == 0 ? "PASS" : "FAIL")}\"}}");
return failed == 0 ? 0 : 1;

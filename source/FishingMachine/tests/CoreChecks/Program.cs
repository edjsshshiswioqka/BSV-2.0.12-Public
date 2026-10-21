using System.Text.Json;
using Eddie.FishingMachine.Core;

string configPath = args.Length > 0 ? args[0] : "src/Eddie.FishingMachine/config.json";
var config = JsonSerializer.Deserialize<BalanceConfig>(File.ReadAllText(configPath)) ?? throw new Exception("Empty config");
config.Validate();
int assertions = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAIL: " + name);
    assertions++;
}
void Reject(Action action, string name)
{
    bool rejected = false;
    try { action(); } catch (ArgumentException) { rejected = true; }
    Check(rejected, name);
}

Check(config.BaitPerCycle == 350 && config.CycleGameMinutes == 2880, "agreed batch and two-day duration");
Check(CatchRoller.Decide(BaitKind.Normal, config.Normal, config.Normal.FishShare - 1e-9, 0) == CatchOutcome.OrdinaryFish, "fish threshold below");
Check(CatchRoller.Decide(BaitKind.Normal, config.Normal, config.Normal.FishShare, 0) == CatchOutcome.OtherItem, "fish threshold exact");
Check(CatchRoller.Decide(BaitKind.Normal, config.Normal, .999999, 0) == CatchOutcome.OtherItem, "highest roll still produces ordinary item");
Check(CatchRoller.Decide(BaitKind.Targeted, config.Targeted, .1, .69999) == CatchOutcome.TargetFish, "target share below");
Check(CatchRoller.Decide(BaitKind.Targeted, config.Targeted, .1, .70) == CatchOutcome.OrdinaryFish, "target share exact");
Check(CatchRoller.Decide(BaitKind.Targeted, config.Targeted, .999999, 0) == CatchOutcome.TargetFish, "targeted bait never empty");
Reject(() => new BaitRates { Fish = .9, OtherItem = .2 }.Validate("bad"), "reject overfull probability");
Reject(() => new BaitRates { Fish = double.NaN }.Validate("bad"), "reject NaN");
Reject(() => CatchRoller.Decide(BaitKind.Normal, config.Normal, 1, 0), "reject out-of-range RNG");
Reject(() => new BalanceConfig { BaitPerCycle = 0 }.Validate(), "reject empty batch");
Reject(() => CycleClock.ToAbsoluteMinutes(0, 1260), "reject invalid HHMM");
Check(CycleClock.ToAbsoluteMinutes(1, 600) - CycleClock.ToAbsoluteMinutes(0, 600) == 1440, "same time next day is 24h");
Check(CycleClock.ToAbsoluteMinutes(1, 600) - CycleClock.ToAbsoluteMinutes(0, 2600) == 240, "2am to 6am is four calendar hours");
Check(CycleClock.ToAbsoluteMinutes(28, 600) - CycleClock.ToAbsoluteMinutes(27, 600) == 1440, "season boundary via elapsed day index");
Check(CycleClock.ToAbsoluteMinutes(112, 600) - CycleClock.ToAbsoluteMinutes(111, 600) == 1440, "year boundary via elapsed day index");
long due = CycleClock.DueAt(CycleClock.ToAbsoluteMinutes(0, 1000), config.CycleGameMinutes);
Check(!CycleClock.IsComplete(CycleClock.ToAbsoluteMinutes(1, 950), due), "not complete early");
Check(!CycleClock.IsComplete(CycleClock.ToAbsoluteMinutes(1, 1000), due), "one day is not complete");
Check(CycleClock.IsComplete(CycleClock.ToAbsoluteMinutes(2, 1000), due), "complete at exact two-day deadline");
Check(CatchRoller.Simulate(config, BaitKind.Normal, 123) == CatchRoller.Simulate(config, BaitKind.Normal, 123), "repeatable seed");
var roundtrip = JsonSerializer.Deserialize<BalanceConfig>(JsonSerializer.Serialize(config))!;
roundtrip.Validate();
Check(CatchRoller.Simulate(config, BaitKind.Targeted, 123) == CatchRoller.Simulate(roundtrip, BaitKind.Targeted, 123), "config roundtrip unchanged");

// Stage-2 requirements: placement, exact tool ingredients and protection of loaded rods.
var anchor = new GridTile(10, 20);
var footprint = PreparationRules.Footprint(anchor);
Check(footprint.Length == 4 && footprint.Distinct().Count() == 4, "four distinct occupied tiles");
Check(footprint.Contains(new GridTile(11,21)), "lower-right tile reserved");
var materialSlots = new[]
{
    new IngredientSlot(0, PreparationRules.Copper, 7), new IngredientSlot(1, PreparationRules.Copper, 13),
    new IngredientSlot(2, PreparationRules.Iron, 20),
    new IngredientSlot(3, PreparationRules.FiberglassRod, 1), new IngredientSlot(4, PreparationRules.FiberglassRod, 1),
    new IngredientSlot(5, PreparationRules.FiberglassRod, 1), new IngredientSlot(6, PreparationRules.FiberglassRod, 1, false),
    new IngredientSlot(7, PreparationRules.Wood, 200),new IngredientSlot(8, PreparationRules.FiberglassRod, 1),
    new IngredientSlot(9, PreparationRules.FiberglassRod, 1),new IngredientSlot(10, PreparationRules.FiberglassRod, 1)
};
Check(PreparationRules.Craftable(4, materialSlots) == 0, "level four cannot craft");
Check(PreparationRules.Craftable(5, materialSlots) == 1, "level five with materials can craft");
var craftPlan = PreparationRules.PlanCraft(5, materialSlots) ?? throw new Exception("craft plan missing");
Check(craftPlan.Count > 0, "craft plan exists");
Check(!craftPlan.Any(x => x.Index == 6), "loaded or enchanted rod preserved");
Check(craftPlan.Where(x => x.Index < 2).Sum(x => x.Count) == 10, "exactly ten copper");
Check(craftPlan.Where(x => x.Index == 2).Sum(x => x.Count) == 10, "exactly ten iron");
Check(craftPlan.Where(x => materialSlots.Single(s=>s.Index==x.Index).Id==PreparationRules.FiberglassRod).Sum(x => x.Count) == 6, "exactly six tools");
var afterCraft = materialSlots.Select(s => s with { Count = s.Count - craftPlan.Where(x => x.Index == s.Index).Sum(x => x.Count) }).ToArray();
Check(PreparationRules.PlanCraft(5, afterCraft) == null, "second craft blocked after tools consumed");
Check(afterCraft.Single(s => s.Index == 6).Count == 1, "protected tool untouched");
Check(PreparationRules.PlanCraft(5, materialSlots.Where(s => s.Index != 5)) == null, "five empty rods plus loaded rod rejected");
Check(PreparationRules.PlanCraft(5, materialSlots.Select(s => s.Id == PreparationRules.Iron ? s with { Count = 9 } : s)) == null, "nine iron rejected");
Check(PreparationRules.Craftable(5, materialSlots.Select(s => s.Id == PreparationRules.FiberglassRod ? s with { Id = "(T)IridiumRod" } : s)) == 0, "different rod is not consumed");
Reject(() => PreparationRules.PlanCraft(5, materialSlots.Append(materialSlots[0])), "duplicate slot ids rejected");

// Production lifecycle, persistence, mixed bait and lossless packing.
var eligible = new HashSet<string> { "(O)128", "(O)129" };
var fishChoices = new[] { new FishChoice("(O)128", .5), new FishChoice("(O)129", .5) };
var mixedBait = new[] { new BaitSlot(0, 120, BaitKind.Normal), new BaitSlot(1, 180, BaitKind.Deluxe), new BaitSlot(2, 80, BaitKind.Targeted, "(O)128") };
var mixedPlan = Production.PlanBait(mixedBait, 350, eligible)!;
Check(mixedPlan.Sum(s => s.Count) == 350 && mixedPlan[^1].Count == 50, "mixed bait spends exactly 350 in slot order");
Check(mixedBait[2].Count == 80, "planning does not mutate input");
Check(Production.PlanBait(new[] { new BaitSlot(0, 349, BaitKind.Normal) }, 350, eligible) == null, "349 cannot start");
Check(Production.PlanBait(new[] { new BaitSlot(0, 350, BaitKind.Targeted, "(O)163") }, 350, eligible) == null, "ineligible targeted bait preserved");
var skipInvalid = Production.PlanBait(new[] { new BaitSlot(0, 350, BaitKind.Targeted, "(O)163"), new BaitSlot(1, 400, BaitKind.Normal) }, 350, eligible)!;
Check(skipInvalid.Count == 1 && skipInvalid[0].Index == 1 && skipInvalid[0].Count == 350, "invalid targeted bait does not block enough valid bait");
Reject(() => Production.PlanBait(mixedBait.Append(mixedBait[0]), 350, eligible), "duplicate bait indices refused");
long startTime = CycleClock.ToAbsoluteMinutes(20, 2350);
var batch = Production.Begin(config, mixedPlan, fishChoices, new[] { "(O)168" }, startTime, 2468);
Check(batch.BaitUsed == 350 && batch.FishCount + batch.OtherCount + batch.EmptyCount == 350, "one trial per bait across all rods");
Check(batch.Pending.Sum(p => p.Count) == batch.ProducedFishCount + batch.OtherCount, "every catch and critical bonus creates a pending object");
Check(batch.BlocksRemoval, "active cycle cannot be removed");
Check(!batch.Complete(CycleClock.ToAbsoluteMinutes(22, 2340)), "not ready ten minutes before two-day deadline");
string savedCycle = batch.Save();
var resumed = ProductionState.Load(savedCycle);
Check(resumed.Save() == savedCycle && resumed.Seed == 2468, "save/load preserves exact catch and seed");
Check(resumed.Complete(CycleClock.ToAbsoluteMinutes(22, 2350)), "ready after exact 48 calendar hours");
Check(!resumed.Complete(CycleClock.ToAbsoluteMinutes(23, 600)), "completion idempotent across repeat ticks");
Check(resumed.Pending.SequenceEqual(batch.Pending), "clock completion never rerolls catch");
Check(ProductionState.Load(resumed.Save()).Pending.SequenceEqual(resumed.Pending), "ready results persist");
Reject(() => ProductionState.Load(savedCycle.Replace("\"Schema\":7", "\"Schema\":99")), "unknown future schema protected");
Reject(() => ProductionState.Load(savedCycle.Replace("\"BaitUsed\":350", "\"BaitUsed\":351")), "corrupt conservation totals refused");
Reject(() => Production.Begin(config, mixedPlan, Array.Empty<FishChoice>(), new[] { "(O)168" }, startTime, 1), "empty fish pool never consumes bait");
Reject(() => Production.Begin(config, mixedPlan.Take(1).ToArray(), fishChoices, new[] { "(O)168" }, startTime, 1), "incomplete bait plan rejected");
var overMidnight = Production.Begin(config, mixedPlan, fishChoices, new[] { "(O)168" }, CycleClock.ToAbsoluteMinutes(5, 2500), 2);
Check(!overMidnight.Complete(CycleClock.ToAbsoluteMinutes(7, 2450)) && overMidnight.Complete(CycleClock.ToAbsoluteMinutes(7, 2500)), "past-midnight start uses correct calendar day");
var pending = new[] { new StoredCatch("(O)128", 1500), new StoredCatch("(O)129", 20) };
var almostFull = new[] { new BufferSlot("(O)128", 990, 999), new BufferSlot("@unrelated", 1, 1) };
var deposit = DepositPlanner.Plan(almostFull, pending, 3, _ => 999);
Check(deposit.Slots.Count == 3 && deposit.Slots[0].Count == 999 && deposit.Slots[2].Count == 999, "fill existing stack then available slot");
Check(deposit.Pending.Single(p => p.Id == "(O)128").Count == 492 && deposit.Pending.Single(p => p.Id == "(O)129").Count == 20, "overflow retained exactly");
Check(almostFull[0].Count == 990 && pending[0].Count == 1500, "deposit planning leaves original inventory and outputs untouched");
var full = DepositPlanner.Plan(deposit.Slots, deposit.Pending, 3, _ => 999);
Check(full.Pending.SequenceEqual(deposit.Pending) && full.Slots.SequenceEqual(deposit.Slots), "full chest repeated tick neither duplicates nor loses items");
var flushed = DepositPlanner.Plan(Array.Empty<BufferSlot>(), full.Pending, 3, _ => 999);
Check(flushed.Pending.Count == 0 && flushed.Slots.Sum(s => s.Count) == 512, "collecting inventory releases all remaining fish");
Check(DepositPlanner.Plan(flushed.Slots, flushed.Pending, 3, _ => 999).Slots.SequenceEqual(flushed.Slots), "already deposited outputs cannot reappear");
resumed.Pending = new();
Check(!ProductionState.Load(resumed.Save()).BlocksRemoval, "ready batch with no pending output may be removed once chest empty");
var targetOnlyConfig = new BalanceConfig { FishCriticalChance=0, Targeted = new BaitRates { Fish = 1, TargetShareOfFish = 0 } };
var targetPlan = Production.PlanBait(new[] { new BaitSlot(0, 350, BaitKind.Targeted, "(O)128") }, 350, eligible)!;
var otherFish = Production.Begin(targetOnlyConfig, targetPlan, fishChoices, new[] { "(O)168" }, 0, 1);
Check(otherFish.Pending.All(p=>p.Id=="(O)129") && otherFish.Pending.Sum(p=>p.Count)==350, "ordinary share of targeted bait excludes its target");
targetOnlyConfig.Targeted.TargetShareOfFish = 1;
var onlyTarget = Production.Begin(targetOnlyConfig, targetPlan, fishChoices, new[] { "(O)168" }, 0, 1);
Check(onlyTarget.Pending.All(p=>p.Id=="(O)128") && onlyTarget.OtherCount == 0, "target selection creates correct species and no junk");

// Relaxed placement: preserve four cells while supporting bank/water mixtures.
foreach(Facing facing in Enum.GetValues<Facing>())
{
    var shore=DirectionRules.BankTiles(anchor,facing);
    var front=DirectionRules.FrontTiles(anchor,facing);
    Check(shore.Length==2 && shore.Distinct().Count()==2,"two rear tiles per direction");
    Check(front.Length==2 && front.All(footprint.Contains),"front water cells belong to reserved footprint");
    Check(DirectionRules.CanPlace(anchor,facing,footprint.Contains,footprint.Contains),"all-water footprint still accepted");
    Check(shore.All(p=>DirectionRules.InReach(anchor,facing,p)),"original rear shore still accessible");
    Check(!DirectionRules.InReach(anchor,facing,anchor),"standing inside reserved footprint rejected");
    var opposite=DirectionRules.BankTiles(anchor,DirectionRules.Next(DirectionRules.Next(facing)));
    Check(opposite.All(p=>DirectionRules.InReach(anchor,facing,p)),"either bank can now operate");
    Check(!DirectionRules.InReach(anchor,facing,new(7,20)),"three tiles from machine remains out of reach");
    foreach(var blocked in footprint)
        Check(!DirectionRules.CanPlace(anchor,facing,footprint.Contains,p=>footprint.Contains(p)&&p!=blocked),"any occupied cell rejects every orientation");
    Check(DirectionRules.CanPlace(anchor,facing,front.Contains,footprint.Contains),"half-water half-bank accepted");
    foreach(var soleWater in front)
    {
        Check(DirectionRules.CanPlace(anchor,facing,p=>p==soleWater,footprint.Contains),"single front water cell accepted at irregular bank");
        Check(DirectionRules.WaterBobber(anchor,facing,p=>p==soleWater)==soleWater,"catch samples water instead of a dry corner");
    }
    Check(!DirectionRules.CanPlace(anchor,facing,p=>footprint.Contains(p)&&!front.Contains(p),footprint.Contains),"rods facing dry land rejected");
    Check(!DirectionRules.CanPlace(anchor,facing,_=>false,footprint.Contains),"fully dry footprint rejected");
    Check(DirectionRules.WaterBobber(anchor,facing,_=>false)==null,"no water returns no cast point");
    foreach(var clicked in footprint)
    {
        var candidates=DirectionRules.AnchorCandidates(clicked,facing).ToArray();
        Check(candidates.Length==4 && candidates.Contains(anchor),"clicking any footprint corner considers the correct anchor");
        var target=DirectionRules.FindPlacement(clicked,facing,true,shore[0],footprint.Contains,footprint.Contains);
        Check(target==new PlacementTarget(anchor,facing),"mouse/controller any-corner placement preserves selected orientation");
    }
    // Exhaust every water/land arrangement, independently checking the occupied cells.
    for(int mask=0;mask<16;mask++)
    {
        var water=footprint.Where((_,i)=>(mask&(1<<i))!=0).ToHashSet();
        Check(DirectionRules.CanPlace(anchor,facing,water.Contains,footprint.Contains)==front.Any(water.Contains),"mixed shoreline truth table");
        foreach(var blocked in footprint)
            Check(!DirectionRules.CanPlace(anchor,facing,water.Contains,p=>footprint.Contains(p)&&p!=blocked),"water never bypasses occupancy");
    }
}
Check(!DirectionRules.CanPlace(anchor,(Facing)99,_=>true,_=>true),"invalid placement direction refused");
Check(!DirectionRules.InReach(anchor,(Facing)99,new(9,20)),"invalid interaction direction refused");
var narrow=DirectionRules.FindPlacement(new(11,20),Facing.Right,true,new(9,20),p=>p.X==11,footprint.Contains);
Check(narrow==new PlacementTarget(anchor,Facing.Right),"one-tile-wide channel can support machine across bank");
var corner=DirectionRules.FindPlacement(new(11,21),Facing.Right,true,new(9,21),p=>p==new GridTile(11,21),footprint.Contains);
Check(corner==new PlacementTarget(anchor,Facing.Right),"irregular single-water corner finds valid footprint");
Check(DirectionRules.FindPlacement(new(11,21),Facing.Up,true,new(9,21),p=>p==new GridTile(11,21),footprint.Contains)==null,"manual direction is not silently changed");
var auto=DirectionRules.FindPlacement(new(11,21),Facing.Up,false,new(9,21),p=>p==new GridTile(11,21),footprint.Contains);
Check(auto.HasValue && DirectionRules.FrontTiles(auto.Value.Anchor,auto.Value.Facing).Contains(new GridTile(11,21)),"automatic direction finds water when preferred faces land");
Check(DirectionRules.FindPlacement(anchor,Facing.Right,false,new(1,1),_=>true,_=>true)==null,"cannot snap to distant water from anywhere on map");
Check(DirectionRules.FindPlacement(new(0,0),Facing.Right,true,new(0,2),_=>true,p=>p.X>=0&&p.Y>=0&&p.X<1)==null,"map boundary cannot be crossed by reserved cells");
// Open-water snapping is preferred over a partly dry base when both are reachable.
var bestWater=DirectionRules.FindPlacement(new(11,20),Facing.Right,false,new(9,20),p=>p.X>=11,p=>p.X>=9&&p.X<=13&&p.Y>=18&&p.Y<=22);
Check(bestWater.HasValue && PreparationRules.Footprint(bestWater.Value.Anchor).All(p=>p.X>=11),"automatic alignment prefers four water cells when available");
var turn=Facing.Right;
for(int n=0;n<4;n++)turn=DirectionRules.Next(turn);
Check(turn==Facing.Right,"four rotations return to initial facing");
Check(DirectionRules.Parse("right")==Facing.Right && DirectionRules.Parse(null)==Facing.Right,"legacy 1.0 machines keep right-facing art");
Check(DirectionRules.Parse("up")==Facing.Up && DirectionRules.Parse("LEFT")==Facing.Left,"persisted directions parse case-insensitively");
Check(DirectionRules.Parse("999")==Facing.Right,"invalid saved direction has deterministic legacy fallback");
Check(DirectionRules.AnchorFromNearWater(new(10,21),Facing.Up)==anchor,"controller approaching from south resolves upper-left anchor");
Check(DirectionRules.AnchorFromNearWater(new(11,20),Facing.Left)==anchor,"controller approaching from east resolves upper-left anchor");
Check(DirectionRules.AnchorFromNearWater(anchor,Facing.Down)==anchor && DirectionRules.AnchorFromNearWater(anchor,Facing.Right)==anchor,"north and west shore controller placement stays aligned");

// Persisted per-player protocol: no repeated world effects or item grants after replay.
var place=new MachineRequest{Id=Guid.NewGuid().ToString("N"),Sequence=1,Actor=42,Kind=ActionKind.Place,Location="Farm",X=10,Y=20,Facing=Facing.Up};
Check(place.IsValid() && MachineRequest.Load(place.Save())==place,"placement intent survives save/reconnect");
Check(ActionProtocol.Order(place,null)==RequestOrder.New,"first request may mutate world");
var placed=MachineReceipt.For(place,ReceiptResult.Accepted);
Check(MachineReceipt.Load(placed.Save())==placed,"host receipt survives reload");
Check(ActionProtocol.Order(place,placed)==RequestOrder.Replay,"lost reply retry replays same receipt");
Check(ActionProtocol.Effect(place,placed)==ReceiptEffect.Complete,"accepted placement consumes escrow without refund");
Check(ActionProtocol.Effect(null,placed)==ReceiptEffect.Ignore,"late placement acknowledgment ignored after completion");
var refused=MachineReceipt.For(place,ReceiptResult.Rejected,"placement.invalid");
Check(ActionProtocol.Effect(place,refused)==ReceiptEffect.ReturnMachine,"occupied placement refunds precisely the escrowed machine");
Check(ActionProtocol.Effect(null,refused)==ReceiptEffect.Ignore,"late refund never gives a second machine");
Check(ActionProtocol.Effect(place,placed with{Actor=99})==ReceiptEffect.Ignore,"another player's receipt ignored");
Check(ActionProtocol.Effect(place,placed with{Sequence=2})==ReceiptEffect.Ignore,"wrong sequence cannot settle escrow");
Check(ActionProtocol.Effect(place,placed with{Id=Guid.NewGuid().ToString("N")})==ReceiptEffect.Ignore,"wrong nonce cannot settle escrow");
var remove=place with{Id=Guid.NewGuid().ToString("N"),Sequence=2,Kind=ActionKind.Remove,MachineId=place.Id};
Check(remove.IsValid() && ActionProtocol.Order(remove,placed)==RequestOrder.New,"next operation may retrieve placed machine");
var removed=MachineReceipt.For(remove,ReceiptResult.Accepted);
Check(ActionProtocol.Effect(remove,removed)==ReceiptEffect.ReturnMachine,"successful retrieval gives exactly one machine");
Check(ActionProtocol.Order(remove,removed)==RequestOrder.Replay,"retrieval retry cannot remove a second machine");
Check(ActionProtocol.Effect(null,removed)==ReceiptEffect.Ignore,"duplicate retrieval receipt cannot duplicate returned item");
Check(ActionProtocol.Order(place,removed)==RequestOrder.Stale,"old placement cannot recreate a retrieved machine");
Check(ActionProtocol.Effect(place,MachineReceipt.For(place,ReceiptResult.Stale))==ReceiptEffect.Unresolved,"ambiguous stale state preserves escrow instead of inventing a refund");
Check(ActionProtocol.Effect(remove,MachineReceipt.For(remove,ReceiptResult.Rejected))==ReceiptEffect.Complete,"failed retrieval gives no machine");
Check(!(remove with{MachineId=""}).IsValid(),"retrieval needs the exact machine identity");
Check(!(place with{Protocol=99}).IsValid() && !(place with{Facing=(Facing)99}).IsValid(),"unsupported protocol or orientation refused");
Reject(()=>ActionProtocol.Order(place,placed with{Actor=99}),"host journal cannot cross player identities");

// Simulate two honest clients contending for the same footprint using the production planner and protocol.
var occupiedTiles=new HashSet<GridTile>();
var receipts=new Dictionary<long,MachineReceipt>();
int worldMachines=0,worldMutations=0;
MachineReceipt Host(MachineRequest req)
{
    receipts.TryGetValue(req.Actor,out var last);
    var order=ActionProtocol.Order(req,last);
    if(order==RequestOrder.Replay)return last!;
    if(order==RequestOrder.Stale)return MachineReceipt.For(req,ReceiptResult.Stale);
    bool accepted;
    if(req.Kind==ActionKind.Place)
    {
        accepted=DirectionRules.CanPlace(new(req.X,req.Y),req.Facing,_=>true,p=>!occupiedTiles.Contains(p));
        if(accepted){foreach(var tile in PreparationRules.Footprint(new(req.X,req.Y)))occupiedTiles.Add(tile);worldMachines++;worldMutations++;}
    }
    else
    {
        accepted=worldMachines>0;
        if(accepted){occupiedTiles.Clear();worldMachines--;worldMutations++;}
    }
    return receipts[req.Actor]=MachineReceipt.For(req,accepted?ReceiptResult.Accepted:ReceiptResult.Rejected);
}
var aReceipt=Host(place);
var bPlace=place with{Actor=43,Id=Guid.NewGuid().ToString("N")};
var bReceipt=Host(bPlace);
Check(worldMachines==1 && aReceipt.Result==ReceiptResult.Accepted && bReceipt.Result==ReceiptResult.Rejected,"two concurrent placements produce one machine");
Check(ActionProtocol.Effect(bPlace,bReceipt)==ReceiptEffect.ReturnMachine,"losing placer retains their machine through refund");
for(int n=0;n<10;n++)Host(place);
Check(worldMutations==1,"ten repeated messages produce no extra placements");
var aRemoval=Host(remove);
var bRemove=bPlace with{Id=Guid.NewGuid().ToString("N"),Sequence=2,Kind=ActionKind.Remove,MachineId=place.Id};
var bRemoval=Host(bRemove);
Check(worldMachines==0 && ActionProtocol.Effect(remove,aRemoval)==ReceiptEffect.ReturnMachine
    && ActionProtocol.Effect(bRemove,bRemoval)==ReceiptEffect.Complete,"two retrieval requests return one machine total");
for(int n=0;n<10;n++)Host(remove);
Check(worldMutations==2,"retrieval retry never repeats its world effect");
Check(Host(place).Result==ReceiptResult.Stale && worldMachines==0,"delayed placement after retrieval cannot resurrect machine");

const int batches = 10000;
// Visual processing: native assets must keep transparency/colour and never affect gameplay time.
var visual=new VisualOptions();
var samples=Enumerable.Range(0,401).Select(i=>FloatMotion.Offset(i/100d,new(5,20),visual)).ToArray();
Check(samples.Min()==-2 && samples.Max()==2,"gentle bobbing stays in the two-pixel amplitude");
Check(Enumerable.Range(0,40).All(i=>FloatMotion.Offset(i/10d,new(5,20),visual)==FloatMotion.Offset(i/10d+4,new(5,20),visual)),"bobbing closes a four-second loop without drift");
Check(!Enumerable.Range(0,40).Select(i=>FloatMotion.Offset(i/10d,new(5,20),visual)).SequenceEqual(Enumerable.Range(0,40).Select(i=>FloatMotion.Offset(i/10d,new(17,20),visual))),"nearby machines have different visual phases");
Check(FloatMotion.Offset(1,new(5,20),new VisualOptions{EnableBobbing=false})==0,"disabled animation stays still");
Check(FloatMotion.Offset(1,new(5,20),new VisualOptions{BobbingAmplitude=0})==0,"zero amplitude stays still");
var legacyConfig=JsonSerializer.Deserialize<BalanceConfig>("{}")!;legacyConfig.Validate();
Check(legacyConfig.Visual.EnableBobbing && legacyConfig.BaitPerCycle==350,"older configurations receive visual defaults without changing production");
Reject(()=>new VisualOptions{BobbingAmplitude=double.NaN}.Validate(),"invalid animation amplitude rejected");
Reject(()=>new VisualOptions{BobbingPeriodSeconds=0}.Validate(),"invalid animation period rejected");
uint blue=PixelArt.Palette[15],copper=PixelArt.Palette[10];
Check(PixelArt.Bake(new[]{blue},1,1,0,0,1,1,1,1,true)[0]==blue,"native palette pixel survives game loading exactly");
Check(PixelArt.Bake(new[]{0x00ffffffu},1,1,0,0,1,1,1,1,false)[0]==0,"invisible RGB cannot turn into a halo");
uint halfBlue=0x80000000u|((blue&255)/2)|((((blue>>8)&255)/2)<<8)|((((blue>>16)&255)/2)<<16);
Check(PixelArt.Bake(new[]{halfBlue},1,1,0,0,1,1,1,1,true)[0]==blue,"premultiplied edge recovers original colour");
Check(PixelArt.Bake(new[]{copper,blue},2,1,1,0,1,1,1,1,false)[0]==blue,"atlas crop cannot sample the neighbouring state");
Reject(()=>PixelArt.Bake(new[]{blue},1,1,1,0,1,1,1,1,true),"invalid atlas crop rejected");

// 1.4.0: detached cache snapshots, deadline preflight and older eight-tile saves.
var sourceCycle = ProductionState.Load(savedCycle);
var detached = sourceCycle.Snapshot();
detached.Pending.Clear(); detached.Phase = "ready";
Check(sourceCycle.Save() == savedCycle, "detached transaction cannot mutate cached render state");
Check(!ProductionSchedule.NeedsWork(sourceCycle, sourceCycle.DueAt - 1, 100000, 350, false), "no production lock before deadline even with fuel");
Check(ProductionSchedule.NeedsWork(sourceCycle, sourceCycle.DueAt, 0, 350, true), "deadline takes precedence over inventory contents");
Check(ProductionSchedule.NeedsWork(sourceCycle, sourceCycle.DueAt + 10000, 0, 350, false), "overdue save resumes immediately");
Check(!ProductionSchedule.NeedsWork(new(), 0, 349, 350, false), "idle with insufficient bait needs no lock");
Check(ProductionSchedule.NeedsWork(new(), 0, 350, 350, false), "exact fuel threshold schedules immediately");
Check(ProductionSchedule.NeedsWork(new(), 0, (long)int.MaxValue + 1, 350, false), "bait preflight cannot overflow int");
var readyForSchedule = ProductionState.Load(savedCycle); readyForSchedule.Phase = "ready";
Check(ProductionSchedule.NeedsWork(readyForSchedule, readyForSchedule.DueAt, 0, 350, true), "pending overflow always retried even with other items");
readyForSchedule.Pending.Clear();
Check(!ProductionSchedule.NeedsWork(readyForSchedule, readyForSchedule.DueAt, 350, 350, true), "uncollected catch blocks restart without another lock");
Check(ProductionSchedule.NeedsWork(readyForSchedule, readyForSchedule.DueAt, 0, 350, false), "empty ready batch can reset to idle once");
foreach (Facing f in Enum.GetValues<Facing>())
{
    var tiles = SavedFootprint.Tiles(anchor, f, true);
    int minX = anchor.X - (f == Facing.Left ? 2 : 0), minY = anchor.Y - (f == Facing.Up ? 2 : 0);
    int width = f is Facing.Left or Facing.Right ? 4 : 2, height = 8 / width;
    var expected = Enumerable.Range(minY, height).SelectMany(y => Enumerable.Range(minX, width).Select(x => new GridTile(x,y))).ToArray();
    Check(tiles.Length == 8 && tiles.ToHashSet().SetEquals(expected), "eight saved tiles retained in " + f);
    Check(SavedFootprint.Tiles(anchor, f, false).SequenceEqual(footprint), "classic four tiles unchanged in " + f);
    for (int y = minY - 3; y <= minY + height + 2; y++)
    for (int x = minX - 3; x <= minX + width + 2; x++)
    {
        var player = new GridTile(x,y);
        bool reachable = x >= minX-2 && x <= minX+width+1 && y >= minY-2 && y <= minY+height+1 && !expected.Contains(player);
        Check(SavedFootprint.InReach(anchor, f, player, true) == reachable, "eight-tile access and recovery perimeter");
        Check(SavedFootprint.InReach(anchor, f, player, false) == DirectionRules.InReach(anchor,f,player), "classic access unchanged");
    }
    var edge = expected.Where(p => f switch { Facing.Up=>p.Y==minY, Facing.Right=>p.X==minX+width-1, Facing.Down=>p.Y==minY+height-1, _=>p.X==minX }).ToArray();
    var preferredBobber = edge[f is Facing.Right or Facing.Down ? 1 : 0];
    for (int mask = 0; mask < 256; mask++)
    {
        var wet = expected.Where((p,i) => (mask & (1<<i))!=0).ToHashSet();
        var expectedBobber = new[]{preferredBobber}.Concat(edge).Concat(expected).Distinct().Where(wet.Contains).Select(p=>(GridTile?)p).FirstOrDefault();
        Check(SavedFootprint.WaterBobber(anchor,f,wet.Contains,true)==expectedBobber,"existing eight-tile water selection preserved");
    }
}

// Compare preview query memoization against the frozen 1.2.0 search over deterministic maps.
PlacementTarget? OriginalSearch(GridTile clicked, Facing preferred, bool locked, GridTile player, Func<GridTile,bool> water, Func<GridTile,bool> usable)
{
    if(!Enum.IsDefined(typeof(Facing),preferred))preferred=Facing.Right;
    var directions=locked?new[]{preferred}:new[]{preferred,Facing.Up,Facing.Right,Facing.Down,Facing.Left}.Distinct();
    PlacementTarget? best=null;int scoreBest=int.MinValue;
    foreach(var f in directions)foreach(var a in DirectionRules.AnchorCandidates(clicked,f))
    {
        if(!DirectionRules.InReach(a,f,player) || !DirectionRules.CanPlace(a,f,water,usable))continue;
        int score=100*PreparationRules.Footprint(a).Count(water)+10*DirectionRules.BankTiles(a,DirectionRules.Next(DirectionRules.Next(f))).Count(water)+DirectionRules.BankTiles(a,f).Count(p=>!water(p));
        if(score>scoreBest){best=new(a,f);scoreBest=score;}
    }
    return best;
}
long beforeQueries=0, afterQueries=0;
var mapRandom=new Random(1400);
for(int sample=0;sample<1024;sample++)
{
    var wet=new HashSet<GridTile>();var clear=new HashSet<GridTile>();
    for(int y=16;y<25;y++)for(int x=6;x<15;x++)
    {var p=new GridTile(x,y);if(mapRandom.Next(4)!=0)wet.Add(p);if(mapRandom.Next(10)!=0)clear.Add(p);}
    var f=(Facing)(sample%4);bool locked=sample%3==0;var player=new GridTile(8,19);
    var waterCalls=new Dictionary<GridTile,int>();var usableCalls=new Dictionary<GridTile,int>();
    var old=OriginalSearch(anchor,f,locked,player,p=>{beforeQueries++;return wet.Contains(p);},p=>{beforeQueries++;return clear.Contains(p);});
    var optimized=DirectionRules.FindPlacement(anchor,f,locked,player,
        p=>{afterQueries++;waterCalls[p]=waterCalls.GetValueOrDefault(p)+1;return wet.Contains(p);},
        p=>{afterQueries++;usableCalls[p]=usableCalls.GetValueOrDefault(p)+1;return clear.Contains(p);});
    Check(old==optimized,"preview selects same anchor and direction as 1.2.0");
    Check(waterCalls.Values.All(n=>n==1) && usableCalls.Values.All(n=>n==1),"each map predicate called once per tile and search");
}
Check(afterQueries < beforeQueries,"preview actually reduces expensive map predicates");

V15Checks.Run(config,Check,Reject);
Lv2Checks.Run(Check,Reject);
Lv3Checks.Run(Check,Reject);
MachineCycleChecks.Run(Check,Reject);
TimelineChecks.Run(Check,Reject);

var rows = new List<object>();
foreach (BaitKind kind in Enum.GetValues<BaitKind>().Where(k=>k!=BaitKind.Challenge))
{
    long fish = 0, target = 0, other = 0, nothing = 0, critical = 0, criticalTarget = 0;
    for (int i = 0; i < batches; i++)
    {
        var result = CatchRoller.Simulate(config, kind, 20260928 + i);
        if (result.Total != config.BaitPerCycle) throw new Exception("Outcome conservation failed");
        fish += result.Fish; target += result.TargetFish; other += result.OtherItems; nothing += result.Nothing;
        critical += result.CriticalFish; criticalTarget += result.CriticalTargetFish;
    }
    var rate = config.GetRates(kind);
    double n = batches * (double)config.BaitPerCycle;
    double targetP = kind == BaitKind.Targeted ? rate.FishShare * rate.TargetShareOfFish : 0;
    void Within(long observed, double p, string metric)
    {
        double allowance = p is 0 or 1 ? 0 : 6 * Math.Sqrt(n * p * (1 - p));
        Check(Math.Abs(observed - n * p) <= allowance, $"{kind} {metric} within six standard deviations");
    }
    Within(fish, rate.FishShare, "fish");
    Within(target, targetP, "target fish");
    Within(other, rate.OtherShare, "other items");
    Within(nothing, rate.Nothing, "nothing");
    Check(Math.Abs(critical-fish*config.FishCriticalChance)<=6*Math.Sqrt(fish*config.FishCriticalChance*(1-config.FishCriticalChance)),kind+" critical rate conditional on successful fish");
    rows.Add(new { bait = kind.ToString(), batches, baseFishMean = fish / (double)batches, fishMean=(fish+critical)/(double)batches, criticalBonusMean=critical/(double)batches, targetFishMean = (target+criticalTarget) / (double)batches, otherItemMean = other / (double)batches, nothingMean = nothing / (double)batches });
}
Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions, simulatedBatches = batches * 3, legacyPreviewRegression = new { baseline = beforeQueries, optimized = afterQueries, scenarios = 1024 }, note = "Pure C# rules only; no in-game integration tested.", rows }, new JsonSerializerOptions { WriteIndented = true }));

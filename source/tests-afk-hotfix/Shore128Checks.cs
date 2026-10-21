using BetterStardewValley.AfkFishing;
using HarmonyLib;
using StardewValley;
using StardewValley.Objects;
using SObject = StardewValley.Object;

partial class Program
{
    static bool ObserveFirstCast;
    static Chest? FirstCastChest;
    static void ShoreReset()
    {
        Reset(); Map.map = Fixture(); Map.buildings.Clear();
        Map.Map.GetLayer("Back").Tiles[6, 5].Properties["Water"] = "T";
    }
    static void FullBag()
    { for (int i = 1; i < Who.Items.Count; i++) Who.Items[i] = new SObject("388", 999); }
    static void Fill(Chest c)
    { c.Items.Clear(); for (int i = 0; i < c.GetActualCapacity(); i++) c.Items.Add(new SObject("388", 999)); }
    static string LinkText() => (string)typeof(AfkFishingService).GetMethod("AreaText", Any)!.Invoke(Service, null)!;
    static bool ConnectedText() => LinkText() == NativeTranslationFixture.Get("afk.area.connected", new { backups = Field<HashSet<Chest>>("Standby").Count }).ToString();
    static void Shore128Checks(Harmony h)
    {
        HostTransactions = true;
        ShoreReset(); var first = AddChest(4, 5); ObserveFirstCast = true; FirstCastChest = null;
        F8(); ObserveFirstCast = false;
        Check(Data.Enabled && BeginCalls == 1 && ReferenceEquals(FirstCastChest, first)
            && ReferenceEquals(Field<Chest?>("Chest"), first),
            "shoreline F8 binds a legal chest BEFORE the first MAX cast despite adjacent water");
        Console.Error.WriteLine($"SHORE128 startup bound={Field<Chest?>("Chest")?.TileLocation} casts={BeginCalls} areaText={LinkText()}");
        Check(Field<AfkArea>("Area").Cells.Count == 9 && Field<AfkArea>("Area").Cells.Any(c => c.Ground == AfkGround.Water)
            && ConnectedText(), "mixed-land/water overlay retains all nine cells and connected status");

        ShoreReset(); first = AddChest(4, 5); FullBag(); F8();
        var catchItem = new SObject("128", 3, quality: 4); catchItem.modData["fixture/catch"] = "preserved";
        Capture(catchItem); Check(Fish(Pending) == 3 && Fish(first.Items) == 0, "committed full-bag catch retained during native catch action, no unsafe chest access");
        Ready(); Clock(20); Run("Tick"); Clock(21); Run("Tick");
        Check(Fish(Pending) == 0 && Fish(first.Items) == 3 && BeginCalls == 2
            && first.Items.Single(i => i?.ItemId == "128").Quality == 4
            && first.Items.Single(i => i?.ItemId == "128").modData["fixture/catch"] == "preserved"
            && Who.Items.Skip(1).All(i => i.ItemId == "388" && i.Stack == 999),
            "shoreline full bag exports exact quality/metadata once, protects pre-session inventory and resumes casting");
        Console.Error.WriteLine($"SHORE128 full bag pending={Fish(Pending)} chest={Fish(first.Items)} casts={BeginCalls}");

        ShoreReset(); first = AddChest(4, 5); FullBag(); var oldStack = new SObject("128", 997); Who.Items[2] = oldStack; F8();
        Capture(new SObject("128", 5));
        Check(oldStack.Stack == 999 && Fish(Pending) == 3 && Fish(first.Items) == 0,
            "capture first fills two compatible backpack stack spaces, preserves only actual overflow for later chest lock");
        Ready(); Clock(20); Run("Tick");
        Check(oldStack.Stack == 997 && Fish(first.Items) == 5 && Fish(Pending) == 0,
            "only current-session five fish exported; all 997 pre-session fish remain protected");

        ShoreReset(); first = AddChest(4, 5, true); first.Items[0] = new SObject("128", 997);
        var second = AddChest(5, 6); FullBag(); F8(); Capture(new SObject("128", 7)); Ready(); Clock(20); Run("Tick");
        Check(Fish(first.Items) == 999 && Fish(Pending) == 5, "shoreline occupied slots still accept two in compatible partial stack");
        Clock(21); Run("Tick"); Clock(22); Run("Tick");
        Check(Fish(second.Items) == 5 && Fish(Pending) == 0 && ReferenceEquals(Field<Chest?>("Chest"), second) && BeginCalls == 2,
            "shoreline partial overflow continues into preplaced backup then resumes exactly one cast");

        ShoreReset(); first = AddChest(4, 5); second = AddChest(5, 6); FullBag(); F8(); Fill(first); Fill(second);
        Capture(new SObject("128", 7)); Ready(); Clock(20); Run("Tick");
        Check(Data.Phase == AfkPhase.PausedStorageFull && Fish(Pending) == 7 && BeginCalls == 1,
            "all storage filled mid-catch retains seven fish and pauses new casts");
        second.Items[0] = new SObject("128", 997); Clock(23); Run("Tick"); Clock(24); Run("Tick");
        Check(Fish(second.Items) == 999 && Fish(Pending) == 5 && BeginCalls == 1,
            "restoring only two units of capacity transfers only two and preserves paused remainder");
        second.Items[1] = null; Clock(27); Run("Tick"); Clock(28); Run("Tick");
        Check(Fish(second.Items) == 1004 && Fish(Pending) == 0 && BeginCalls == 2,
            "paused shoreline session recovers remaining output before one resumed cast");
        Console.Error.WriteLine($"SHORE128 recovery pending={Fish(Pending)} chest={Fish(second.Items)} casts={BeginCalls}");

        ShoreReset(); F8(); FullBag(); Capture(new SObject("128", 2)); Ready(); Clock(20); Run("Tick");
        first = AddChest(4, 5); Run("TopologyChanged", Map); Clock(23); Run("Tick"); Clock(24); Run("Tick");
        Check(Fish(first.Items) == 2 && Fish(Pending) == 0 && BeginCalls == 2, "already-full paused session discovers newly placed legal shoreline chest and resumes");

        ShoreReset(); first = AddChest(3, 5); F8(); FullBag(); Capture(new SObject("128", 2)); Ready(); Clock(20); Run("Tick");
        Check(Field<Chest?>("Chest") == null && Fish(first.Items) == 0 && Fish(Pending) == 2,
            "chest outside current 3x3 never receives retained output");
        Who.Items[1] = null; Clock(23); Run("Tick");
        Check(Fish(Who.Items) == 2 && Fish(Pending) == 0 && BeginCalls == 2, "backpack capacity recovery drains exact pending catch and resumes without a chest");

        ShoreReset(); first = AddChest(4, 5); var invalid = AddChest(6, 5); F8();
        Check(ReferenceEquals(Field<Chest?>("Chest"), first) && !AfkChestArea.Candidates(Who).Contains(invalid),
            "illegally located chest on water excluded individually without rejecting land chest");
        Ready(); Run("Stop"); Map.objects.Remove(new(4, 5)); invalid.fridge.Value = true; Map.Map.GetLayer("Back").Tiles[6, 5].Properties.Remove("Water");
        F8(); Check(Field<Chest?>("Chest") == null, "fridge is not treated as supported ordinary storage");
        Ready(); Run("Stop"); Map.objects.Clear(); Map.objects[new(4, 5)] = new SObject("388", 1); F8();
        Check(Field<Chest?>("Chest") == null, "ordinary object with an interaction is never treated as a chest");

        ShoreReset(); first = AddChest(4, 5);
        var building = new StardewValley.Buildings.Building(); building.tileX.Value = 5; building.tileY.Value = 6; building.tilesWide.Value = building.tilesHigh.Value = 1; Map.buildings.Add(building);
        invalid = AddChest(5, 6); F8();
        Check(ReferenceEquals(Field<Chest?>("Chest"), first) && !AfkChestArea.Candidates(Who).Contains(invalid), "building excludes only its own illegal chest tile");
        ShoreReset(); Put(Who, Map, 0, 5); first = AddChest(1, 5);
        var edgeArea = AfkChestArea.Inspect(Map, Who.TilePoint, Who);
        Check(edgeArea.Cells.Count == 9 && edgeArea.Cells.Count(c => c.Ground == AfkGround.Outside) == 3
            && AfkChestArea.Candidates(Who).Contains(first), "map-edge invalid cells do not veto in-map adjacent chest");

        // No world-wide filtering or redraw gate may depend on the optional mod pair.
        foreach (var mods in new[] { new string[0], new[] { "Pathoschild.Automate" }, new[] { "Annosz.UiInfoSuite2" }, new[] { "Pathoschild.Automate", "Annosz.UiInfoSuite2" } })
        {
            Recorder.LoadedMods = mods.ToHashSet(); ShoreReset(); first = AddChest(4, 5); FullBag(); F8(); Capture(new SObject("128", 2)); Ready(); Clock(20); Run("Tick");
            Check(Fish(first.Items) == 2 && Fish(Pending) == 0 && ConnectedText(), "shoreline transfer/AFK status independent of loaded mods: " + string.Join("+", mods));
        }
        Recorder.LoadedMods.Clear(); HostTransactions = false; Map.map = Fixture(); Reset();
    }
}

using BetterStardewValley.AfkFishing;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using SObject = StardewValley.Object;

partial class Program
{
    static Chest AddChest(int x, int y, bool full = false)
    {
        var c = new Chest(true); Map.objects[new(x, y)] = c;
        if (full) for (int i = 0; i < 36; i++) c.Items.Add(new SObject("388", 999));
        return c;
    }
    static int Fish(IEnumerable<Item> items) => items.Where(i => i?.ItemId == "128").Sum(i => i.Stack);
    static Action? DeferredLock;
    static bool DeferLock(Action acquired) { DeferredLock = acquired; return false; }
    static void NineTileChecks(Harmony h)
    {
        HostTransactions = true;
        Reset(); var first = AddChest(4, 5); var second = AddChest(6, 5);
        F8(); Check(ReferenceEquals(Field<Chest>("Chest"), first), "3x3 initial equal-distance binding uses stable Y/X order");
        Check(Field<AfkArea>("Area").AllGroundValid && Field<AfkArea>("Area").Cells.Count == 9, "native placement API validates all nine cells; self and ordinary chests exempted");
        Run("BindChest", second); Run("Reconnect");
        Check(ReferenceEquals(Field<Chest>("Chest"), second), "valid existing binding survives nearest-chest refresh");
        var original = new SObject("128", 30); Who.Items[2] = original;
        Capture(new SObject("128", 7)); Ready(); Clock(20); Run("Tick");
        Check(original.Stack == 30 && Fish(second.Items) == 7 && Fish(first.Items) == 0,
            "native stacked capture transfers only seven new fish, leaving all thirty pre-session fish in backpack");
        Check(!second.GetMutex().IsLocked(), "output transfer releases actual native chest mutex");
        Clock(21); Run("Tick"); Check(Fish(second.Items) == 7 && original.Stack == 30, "next tick cannot transfer or duplicate credited output again");

        Reset(); first = AddChest(4, 5, true); second = AddChest(6, 5);
        first.Items[0] = new SObject("128", 997); F8(); Capture(new SObject("128", 7)); Ready(); Clock(20); Run("Tick");
        Check(Fish(first.Items) == 999 && Fish(Who.Items) == 5, "no empty slot: first chest accepts two into its partial stack and only those two leave backpack");
        Clock(21); Run("Tick"); Check(Fish(second.Items) == 5 && Fish(Who.Items) == 0 && ReferenceEquals(Field<Chest>("Chest"), second), "remaining five fish switch to preplaced backup within same 3x3");
        Run("RefreshArea", true);
        Check(!Field<HashSet<Chest>>("Standby").Contains(first), "full chest is gray/unavailable, never advertised as a capacity-bearing blue standby");
        Check(Fish(first.Items) + Fish(second.Items) == 1004, "real native item conservation across partial-stack and backup transfers");

        Reset(); first = AddChest(4, 5);
        for (int i = 1; i < Who.Items.Count; i++) Who.Items[i] = new SObject("388", 999);
        F8(); Check(BeginCalls == 1, "full original backpack can fish when an eligible connected chest has capacity");
        Capture(new SObject("128", 3)); Ready(); Clock(20); Run("Tick");
        Check(Fish(first.Items) == 3 && Pending.Count == 0, "committed overflow reaches chest without moving any original inventory");
        Reset(); first = AddChest(4, 5, true); second = AddChest(6, 5, true); F8();
        Capture(new SObject("128", 3)); Ready(); Clock(20); Run("Tick");
        Check(Fish(Who.Items) == 3 && Fish(first.Items) == 0 && Fish(second.Items) == 0 && Data.Enabled, "all chests full retains native catches in backpack without stopping AFK");
        Ready(); for (int i = 1; i < Who.Items.Count; i++) Who.Items[i] = new SObject("388", 999);
        Capture(new SObject("128", 2)); Clock(23); Run("Tick");
        Check(Data.Phase == AfkPhase.PausedStorageFull && Fish(Pending) == 2, "already committed catch retained while all destinations full; next cast paused");
        second.Items[1] = null; Clock(26); Run("Tick"); Clock(27); Run("Tick");
        Check(Fish(Pending) == 0 && Fish(second.Items) == 2 && Data.Enabled, "new chest capacity automatically drains exact pending result once");

        Reset(); first = AddChest(4, 5, true); F8(); Capture(new SObject("128", 4)); Ready(); Clock(20); Run("Tick");
        Check(Fish(Who.Items) == 4 && Fish(first.Items) == 0, "no backup chest never discards backpack output");
        Reset(); first = AddChest(3, 5); F8(); Check(Field<Chest?>("Chest") == null, "distance two is out of range despite being close and on land");
        Reset(); first = AddChest(4, 5); F8(); Capture(new SObject("128", 4)); Ready();
        Put(Who, Map, 6, 5); Run("RefreshArea", true); Clock(20); Run("Tick");
        Check(Field<AfkArea>("Area").Center.X == 6 && Field<Chest?>("Chest") == null && Fish(first.Items) == 0,
            "actual player movement updates overlay center and invalidates remote chest before any transfer");
        Reset(); first = AddChest(4, 5); F8(); Ready(); Capture(new SObject("128", 4)); Map.objects.Clear(); Clock(20); Run("Tick");
        Check(Fish(first.Items) == 0 && Fish(Who.Items) == 4 && Field<Chest?>("Chest") == null, "removed chest reference is never reused");

        Reset(); first = AddChest(4, 5); F8(); Ready(); var layer = Map.Map.GetLayer("Back");
        layer.Tiles[6, 6].Properties["Water"] = "T"; Run("RefreshArea", true);
        Check(!Field<AfkArea>("Area").AllGroundValid && Field<AfkArea>("Area").Reason == AfkGround.Water && ReferenceEquals(Field<Chest?>("Chest"), first),
            "one water tile is marked invalid while the reachable land chest stays connected");
        layer.Tiles[6, 6].Properties.Remove("Water");
        var building = new StardewValley.Buildings.Building(); building.tileX.Value = 6; building.tileY.Value = 6; building.tilesWide.Value = building.tilesHigh.Value = 1; Map.buildings.Add(building);
        Check(AfkChestArea.Inspect(Map, Who.TilePoint, Who).Reason == AfkGround.Building, "building cell retains its own explicit invalid reason"); Map.buildings.Clear();
        Check(!AfkChestArea.Inspect(Map, new(0, 5), Who).AllGroundValid && AfkChestArea.Inspect(Map, new(0, 5), Who).Cells.Count == 9, "map-edge cells remain invalid while full nine-cell snapshot is retained");
        layer.Tiles[6, 6].Properties["NoFurniture"] = "total";
        Check(AfkChestArea.Inspect(Map, Who.TilePoint, Who).Reason == AfkGround.Forbidden, "native NoFurniture placement restriction retained"); layer.Tiles[6, 6].Properties.Remove("NoFurniture");
        first.fridge.Value = true; Check(!AfkChestArea.Inspect(Map, Who.TilePoint, Who).AllGroundValid, "special container is not exempted as a usable ordinary chest"); first.fridge.Value = false;
        first.GetMutex().RequestLock(); first.GetMutex().Update(Map); Run("Reconnect");
        Check(Field<Chest?>("Chest") == null && first.GetMutex().IsLocked(), "busy chest loses binding and AFK never steals existing lock"); first.GetMutex().ReleaseLock();
        Run("Reconnect"); Check(ReferenceEquals(Field<Chest>("Chest"), first), "released ordinary chest may reconnect");

        // Deliver a delayed acquisition after player movement, stop, chest removal or terrain
        // changes. Only the transport envelope is delayed; the production callback runs.
        var lockMethod = first.GetMutex().GetType().GetMethod("RequestLock", new[] { typeof(Action), typeof(Action) })!;
        h.Patch(lockMethod, prefix: new(typeof(Program), nameof(DeferLock)));
        var settings=(BetterStardewValley.ModConfig)typeof(AfkFishingService).GetField("Config",Any)!.GetValue(Service)!;
        foreach (string cause in new[] { "move", "water", "neighbor-water", "removed", "disabled", "generation", "casting", "saving", "chest-setting", "afk-setting" })
        {
            Reset(); first = AddChest(4, 5); F8(); Ready(); bool ran = false;
            Run("WithChest", first, (Action)(() => ran = true));
            Check(DeferredLock != null && Field<bool>("LockPending"), "deferred lock scheduled: " + cause);
            if (cause == "move") Put(Who, Map, 6, 5);
            if (cause == "water") layer.Tiles[4, 5].Properties["Water"] = "T";
            if (cause == "neighbor-water") layer.Tiles[6, 6].Properties["Water"] = "T";
            if (cause == "removed") Map.objects.Clear();
            if (cause == "disabled") Run("Stop");
            if (cause == "generation") Field("Generation", Field<int>("Generation") + 1);
            if (cause == "casting") { Who.UsingTool = true; Rod.isCasting = true; }
            if (cause == "saving") Field("Saving", true);
            if(cause=="chest-setting")settings.AfkFishing.EnableChestConnection=false;
            if(cause=="afk-setting")settings.AfkFishing.Enabled=false;
            // Hold the genuine mutex before delivering the deferred callback.
            h.Unpatch(lockMethod, HarmonyPatchType.Prefix, h.Id); first.GetMutex().RequestLock(); first.GetMutex().Update(Map);
            DeferredLock!(); DeferredLock = null;
            Check(ran == (cause == "neighbor-water") && !first.GetMutex().IsLocked(), "post-lock revalidation accepts only legal chest tiles; reason=" + cause + "; mutex released");
            settings.AfkFishing.EnableChestConnection=true;settings.AfkFishing.Enabled=true;
            layer.Tiles[4, 5].Properties.Remove("Water");
            layer.Tiles[6, 6].Properties.Remove("Water");
            h.Patch(lockMethod, prefix: new(typeof(Program), nameof(DeferLock)));
        }
        h.Unpatch(lockMethod, HarmonyPatchType.Prefix, h.Id);

        Reset(); first = AddChest(4, 5); Game1.objectData["774"].Category = SObject.baitCategory;
        Rod.UpgradeLevel = 2; Rod.attachments[0] = new SObject("774", 1); first.Items.Add(new SObject("774", 5)); first.Items.Add(new SObject("388", 9));
        F8(); Check(Rod.attachments[0]!.Stack == 6 && first.Items[1].Stack == 9, "chosen bait tops up under chest lock; unrelated supplies remain unchanged");
        Ready(); Rod.attachments[0] = null; first.Items[0] = new SObject("774", 2); Clock(20); Run("Tick");
        Check(Rod.attachments[0]!.Stack == 2 && first.Items[0] == null, "native bait consumed to zero may reload only the previously chosen type");

        foreach (var mods in new[] { new string[0], new[] { "Pathoschild.Automate" }, new[] { "Annosz.UiInfoSuite2" }, new[] { "Pathoschild.Automate", "Annosz.UiInfoSuite2" } })
        {
            Recorder.LoadedMods = mods.ToHashSet(); Reset(); first = AddChest(4, 5); F8(); Capture(new SObject("128", 2)); Ready(); Clock(20); Run("Tick");
            Check(Data.Enabled && Field<AfkArea>("Area").AllGroundValid && Fish(first.Items) == 2 && Fish(Who.Items) == 0,
                "AFK-specific overlay state and once-only transport independent of optional mod registry combination: " + string.Join("+", mods));
        }
        Check(Recorder.Events.Any(e => e.Name == "add_RenderedWorld" && e.Handler.Method.DeclaringType == typeof(AfkFishingService)), "AFK owns its world overlay registration, independent of InformationHost/UI2 gate");
        Run("Stop"); Check(Field<AfkArea?>("Area") == null && Field<Chest?>("Chest") == null, "toggle off immediately clears overlay snapshot and binding");
        Reset(); first = AddChest(4, 5); F8(); Capture(new SObject("128", 3)); Ready(); Run("Persist"); Run("Load");
        Check(Data.Enabled && Field<Chest?>("Chest") == null && !Field<AfkCatchLedger>("Catches").HasOutput(Who.Items)
            && Fish(Who.Items) == 3, "reload discards stale references and conservatively protects previously carried items without loss");
        Recorder.LoadedMods.Clear(); Reset(); HostTransactions = false;
    }
}

using BetterStardewValley.AfkFishing;
using HarmonyLib;
using StardewValley;
using StardewValley.Objects;
using SObject = StardewValley.Object;

partial class Program
{
    static void Food129Reset(float ratio = .29f)
    {
        ShoreReset(); HostTransactions = true; Who.maxStamina.Value = 270;
        Who.Stamina = Who.MaxStamina * ratio; Eaten = 0; EatenId = "";
        FoodChest = new Chest(true); RejectEating = FailEating = false;
    }
    static int CountFood(IEnumerable<Item> items, string id) => items.Where(i => i?.ItemId == id).Sum(i => i.Stack);
    static void Food129Checks(Harmony h)
    {
        Food129Reset(); Who.Items[2] = new SObject("216", 3); F8();
        Console.Error.WriteLine($"FOOD129 baseline hungry={AfkRules.Hungry(Who.Stamina, Who.MaxStamina)} eaten={Eaten} bread={CountFood(Who.Items, "216")} casts={BeginCalls}");
        Check(Data.Enabled && Who.isEating && Eaten == 1 && Who.Items[2].Stack == 2 && BeginCalls == 0,
            "29 percent energy eats one backpack bread before first cast, without any chest");
        Run("Tick"); Check(Eaten == 1 && Who.Items[2].Stack == 2, "eating state prevents repeated food consumption");
        // The native eating effects/animation are the existing explicit headless boundary.
        // Supply its completed energy result, then exercise the real session continuation.
        Ready(); Who.Stamina = Who.MaxStamina * .8f; Clock(20); Run("Tick");
        Check(BeginCalls == 1 && LastPower == 1 && ReferenceEquals(Who.CurrentTool, Rod) && Data.Enabled,
            "after eating completes, same rod resumes a single MAX cast with AFK enabled");

        foreach (int max in new[] { 100, 270, 508 })
        {
            Check(AfkRules.Hungry(max * .30f, max) && !AfkRules.Hungry(max * .30f + .01f, max), "inclusive proportional thirty-percent boundary: " + max);
            Food129Reset(.3f); Who.maxStamina.Value = max; Who.Stamina = max * .3f;
            Who.Items[2] = new SObject("216", 2); F8();
            Check(Eaten == 1 && BeginCalls == 0 && Who.Items[2].Stack == 1, "exact thirty-percent starts safe eating for max " + max);
        }
        Food129Reset(.301f); Who.Items[2] = new SObject("216", 2); F8();
        Check(Eaten == 0 && Who.Items[2].Stack == 2 && BeginCalls == 1, "above thirty-percent keeps food and starts fishing");

        Food129Reset(); var chest = FoodChest = AddChest(4, 5); chest.Items.Add(new SObject("196", 4));
        Who.Items[1] = new SObject("128", 5, quality: 4); Who.Items[2] = new SObject("152", 6);
        Who.Items[3] = new SObject("227", 7); Who.Items[4] = new SObject("216", 3); F8();
        Check(EatenId == "216" && Who.Items[4].Stack == 2 && chest.Items[0].Stack == 4 && !FoodLockHeld
            && Who.Items[1].Stack == 5 && Who.Items[2].Stack == 6 && Who.Items[3].Stack == 7,
            "backpack safe food has priority over chest; fish, algae and seafood dish untouched");

        Food129Reset(); chest = FoodChest = AddChest(4, 5); chest.Items.Add(new SObject("128", 8)); chest.Items.Add(new SObject("216", 3));
        Who.Items[1] = new SObject("163", 2, quality: 4); Who.Items[2] = new SObject("730", 3); F8();
        Check(Eaten == 1 && EatenId == "216" && chest.Items[1].Stack == 2 && FoodLockHeld && !chest.GetMutex().IsLocked()
            && Who.Items[1].Stack == 2 && Who.Items[2].Stack == 3 && chest.Items[0].Stack == 8,
            "aquatic-only backpack falls back to legal shoreline chest, consumes once under genuine mutex");
        Check(ConnectedText(), "nearby water does not block food chest or AFK range status");
        Ready(); Who.Stamina = Who.MaxStamina * .8f; Clock(20); Run("Tick");
        Check(BeginCalls == 1 && LastPower == 1 && Data.Enabled, "chest meal finishes into automatic MAX fishing");

        Food129Reset(); var first = AddChest(4, 5); first.Items.Add(new SObject("128", 8));
        var second = FoodChest = AddChest(5, 6); second.Items.Add(new SObject("196", 2)); F8();
        Check(EatenId == "196" && Eaten == 1 && second.Items[0].Stack == 1 && first.Items[0].Stack == 8
            && ReferenceEquals(Field<Chest?>("Chest"), second) && FoodLockHeld,
            "bound fish-only chest can hand off to an existing eligible food chest in the same nine cells");
        Food129Reset(); chest = FoodChest = AddChest(3, 5); chest.Items.Add(new SObject("216", 3)); F8();
        Check(Eaten == 0 && BeginCalls == 1 && chest.Items[0].Stack == 3, "food beyond centered three-by-three cannot be taken");
        Food129Reset(); chest = FoodChest = AddChest(6, 5); chest.Items.Add(new SObject("216", 3)); F8();
        Check(Eaten == 0 && chest.Items[0].Stack == 3, "chest illegally located on water cannot feed the player");

        Food129Reset(); var bread = new SObject("216", 2); bread.modData["meal"] = "kept"; Who.Items[2] = bread;
        RejectEating = true; F8();
        Check(Eaten == 0 && ReferenceEquals(Who.Items[2], bread) && bread.Stack == 2 && bread.modData["meal"] == "kept",
            "refused backpack eating restores exact original stack and metadata");
        RejectEating = false; FailEating = true; Clock(20); Run("Guard", (Action)(() => Run("Tick")));
        Check(Eaten == 0 && ReferenceEquals(Who.Items[2], bread) && bread.Stack == 2 && !Data.Enabled,
            "exception before eating starts rolls back backpack item and actual event guard safely yields control"); FailEating = false;
        Food129Reset(); Who.Items[2] = new SObject("216", 1); F8();
        Check(Eaten == 1 && Who.Items[2] == null && ReferenceEquals(Who.Items[0], Rod), "last food clears only its own slot, never the rod");

        Food129Reset(.8f); F8(); Who.Items[2] = new SObject("216", 2); Who.Stamina = Who.MaxStamina * .29f;
        Clock(20); Run("Tick");
        Check(Eaten == 0 && Who.Items[2].Stack == 2 && BeginCalls == 1, "in-flight cast is not interrupted for eating");
        Ready(); Clock(21); Run("Tick"); Check(Eaten == 1 && BeginCalls == 1, "food begins at next safe fishing boundary");
        Food129Reset(.05f); Who.Items[2] = new SObject("216", 3); F8();
        Check(Eaten == 0 && Field<bool>("HomeIntent") && Data.RestReason == AfkRestReason.LowEnergy,
            "five-percent emergency rest remains latched and is not cancelled by food");
        Food129Reset(); Who.Items[2] = new SObject("216", 3); Game1.timeOfDay = 2430; F8();
        Check(Eaten == 0 && Field<bool>("HomeIntent"), "bedtime rest still takes priority over food");
        Food129Reset(.8f); F8(); Ready(); Run("Stop"); Who.Items[2] = new SObject("216", 3); Who.Stamina = 60; Clock(20); Run("Tick");
        Check(Eaten == 0 && Who.Items[2].Stack == 3 && !Data.Enabled, "manual AFK stop cancels auto-feeding");

        Food129Reset(.8f); F8(); FullBag(); Capture(new SObject("128", 3, quality: 4)); Ready(); Clock(20); Run("Tick");
        Check(Data.Phase == AfkPhase.PausedStorageFull && Fish(Pending) == 3, "full inventory pauses with committed fish retained before meal recovery");
        Who.Items[2] = new SObject("216", 1); Who.Stamina = Who.MaxStamina * .29f; Clock(23); Run("Tick");
        Check(Eaten == 1 && Who.Items[2] == null && Fish(Pending) == 3 && BeginCalls == 1,
            "safe meal frees one backpack slot while full-storage session preserves all pending fish");
        Ready(); Who.Stamina = Who.MaxStamina * .8f; Clock(24); Run("Tick");
        Check(Fish(Pending) == 0 && Fish(Who.Items) == 3 && Who.Items[2].Quality == 4 && BeginCalls == 2,
            "after meal, full-storage session drains retained fish once before resuming MAX cast");

        // A delayed chest acquisition must recheck the food source and live player state.
        var lockMethod = typeof(Chest).GetMethod("GetMutex")!.ReturnType.GetMethod("RequestLock", new[] { typeof(Action), typeof(Action) })!;
        foreach (string change in new[] { "moved", "removed", "disabled", "saving", "refilled", "backpack-food", "food-replaced" })
        {
            Food129Reset(); chest = FoodChest = AddChest(4, 5); chest.Items.Add(new SObject("216", 3));
            h.Patch(lockMethod, prefix: new(typeof(Program), nameof(DeferLock))); DeferredLock = null; F8();
            Check(DeferredLock != null && Field<bool>("LockPending"), "food lock deferred: " + change);
            if (change == "moved") Put(Who, Map, 6, 5);
            if (change == "removed") Map.objects.Clear();
            if (change == "disabled") Run("Stop");
            if (change == "saving") Field("Saving", true);
            if (change == "refilled") Who.Stamina = Who.MaxStamina;
            if (change == "backpack-food") Who.Items[2] = new SObject("196", 2);
            if (change == "food-replaced") chest.Items[0] = new SObject("128", 3);
            h.Unpatch(lockMethod, HarmonyPatchType.Prefix, h.Id); chest.GetMutex().RequestLock(); chest.GetMutex().Update(Map);
            DeferredLock!(); DeferredLock = null;
            Check(chest.Items[0].Stack == 3 && !chest.GetMutex().IsLocked()
                && Eaten == (change == "backpack-food" ? 1 : 0), "post-lock recheck preserves inventory: " + change);
            if (change == "backpack-food") Check(EatenId == "196" && Who.Items[2].Stack == 1, "backpack priority rechecked after delayed chest grant");
        }
        foreach (var mods in new[] { new string[0], new[] { "Pathoschild.Automate" }, new[] { "Annosz.UiInfoSuite2" }, new[] { "Pathoschild.Automate", "Annosz.UiInfoSuite2" } })
        {
            Recorder.LoadedMods = mods.ToHashSet(); Food129Reset(); chest = FoodChest = AddChest(4, 5); chest.Items.Add(new SObject("216", 2)); F8();
            Check(Eaten == 1 && chest.Items[0].Stack == 1 && FoodLockHeld, "AFK food remains available with optional mods: " + string.Join("+", mods));
        }
        Recorder.LoadedMods.Clear(); Who.maxStamina.Value = 270; HostTransactions = false; Map.map = Fixture(); Reset();
    }
}

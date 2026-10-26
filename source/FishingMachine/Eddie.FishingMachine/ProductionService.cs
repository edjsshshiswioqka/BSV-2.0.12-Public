using System.Text.Json;
using Eddie.FishingMachine.Core;
using StardewValley.Tools;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using System.Runtime.CompilerServices;

namespace Eddie.FishingMachine;

internal sealed partial class ProductionService
{
    internal const string StateKey = ModEntry.Tag + "Cycle";
    internal const string AutoTrashKey = ModEntry.Tag + "AutoTrash";
    internal const string RecoveryKey = ModEntry.Tag + "PreparingRecovery";
    internal static bool PreparingRecovery(Chest c) => c.modData.ContainsKey(RecoveryKey);
    private readonly ModEntry Mod;
    private readonly HashSet<Chest> LockRequests = new();
    private readonly HashSet<string> Errors = new();
    private sealed record CachedState(string? Raw, string? Option, ProductionState? State, Exception? Error);
    private ConditionalWeakTable<Chest, CachedState> Cache = new();
    private sealed record BaitVisualCache(double Until,bool Present);
    private ConditionalWeakTable<Chest,BaitVisualCache> BaitCache=new();
    internal ProductionService(ModEntry mod) => Mod = mod;
    internal static long Now => CycleClock.ToAbsoluteMinutes(Game1.Date.TotalDays, Game1.timeOfDay);
    internal void Reset() { TraceStates.Clear();LockRequests.Clear(); Errors.Clear(); Cache = new(); BaitCache=new(); ClearCycleDiagnostics(); }

    private CachedState GetCached(Chest c)
    {
        c.modData.TryGetValue(StateKey, out string raw);
        c.modData.TryGetValue(AutoTrashKey,out string option);
        if (Cache.TryGetValue(c, out var cached) && cached.Raw == raw && cached.Option==option) return cached;
        try
        {
            var state=raw==null?new ProductionState{Kind=ModEntry.IsCrab(c)?MachineKinds.Crab:MachineKinds.Fishing}:ProductionState.Load(raw);
            if((state.Kind==MachineKinds.Crab)!=ModEntry.IsCrab(c))throw new InvalidOperationException("Machine kind does not match its saved data.");
            // Legacy AutoTrash metadata is retained but has no operational effect.
            cached=new(raw,option,state,null);
        }
        catch (Exception ex) { cached = new(raw,option,null,ex); }
        Cache.Remove(c); Cache.Add(c, cached);
        return cached;
    }
    internal ProductionState Read(Chest c)
    {
        var cached = GetCached(c);
        return cached.State ?? throw new InvalidOperationException("Invalid saved machine cycle; preserved.", cached.Error);
    }
    internal string Phase(Chest? c)
    {
        if (c == null) return "idle";
        return GetCached(c).State?.Phase ?? "error";
    }
    internal bool HasBait(Chest c)
    {
        if(Phase(c)=="working")return true;
        double now=Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
        if(BaitCache.TryGetValue(c,out var cached) && now<cached.Until)return cached.Present;
        bool present=ModEntry.IsCrab(c)?c.Items.Any(CrabBaitItem):c.Items.Any(i=>Bait(i,0,Level(c))!=null);
        BaitCache.Remove(c);BaitCache.Add(c,new(now+250,present));return present;
    }
    internal bool BlocksRemoval(Chest c)
    {
        try { return Read(c).BlocksRemoval; } catch { return true; }
    }

    /// <summary>What each bait adds at this level (balance version 2), for the machine's info text.</summary>
    internal string BaitEffects(int level)
    {
        string P(double p)=>(p*100).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"%";
        var m=BaitRules.MagnetTreasure(level);
        return Mod.T("machine.bait-effects",new{target=P(BaitRules.TargetShare(level)),wild=P(BaitRules.WildDouble(level)),
            magnet=P(m[0])+" / "+P(m[1]),challenge=P(BaitRules.ChallengeTriple(level))});
    }
    /// <summary>Every bait the fishing machine uses (challenge bait only from level 2).</summary>
    internal static bool IsFishingBait(string? qualifiedId)=>qualifiedId is "(O)685" or "(O)ChallengeBait" or "(O)DeluxeBait" or "(O)SpecificBait" or "(O)774" or "(O)703" or "(O)908" or GiantLobsterRules.Bait;
    internal static BaitSlot? Bait(Item? item, int index, int level = 1)
    {
        if (item is not StardewValley.Object o || o.Stack <= 0 || o.questItem.Value) return null;
        return o.QualifiedItemId switch
        {
            "(O)685" => new(index, o.Stack, BaitKind.Normal),
            GiantLobsterRules.Bait => new(index, o.Stack, BaitKind.GiantLobster),
            "(O)ChallengeBait" when level >= 2 => new(index, o.Stack, BaitKind.Challenge),
            "(O)DeluxeBait" => new(index, o.Stack, BaitKind.Deluxe),
            "(O)774" => new(index, o.Stack, BaitKind.Wild),
            "(O)703" => new(index, o.Stack, BaitKind.Magnet),
            "(O)908" => new(index, o.Stack, BaitKind.Magic),
            "(O)SpecificBait" when !string.IsNullOrEmpty(o.preservedParentSheetIndex.Value)
                => new(index, o.Stack, BaitKind.Targeted, o.preservedParentSheetIndex.Value.StartsWith("(O)")
                    ? o.preservedParentSheetIndex.Value : "(O)" + o.preservedParentSheetIndex.Value),
            _ => null
        };
    }

    internal void Tick()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || Mod.Saving || Now<360) return;
        TickContinuity();
    }

    private bool NeedsWork(Chest c)
    {
        var cached = GetCached(c);
        if (cached.State == null)
        {
            if (Errors.Add(c.modData[ModEntry.Tag + "MachineId"] + ":invalid-state"))
                Mod.Monitor.Log($"Machine paused at {c.Location?.NameOrUniqueName}:{c.TileLocation}; invalid cycle preserved. {cached.Error}", LogLevel.Error);
            return false;
        }
        var state = cached.State;
        if(state.StageProgressUnverified&&Errors.Add(c.modData.GetValueOrDefault(ModEntry.Tag+"MachineId")+":stage-history"))
            Mod.Monitor.Log($"Machine stage history cannot be verified at {c.Location?.NameOrUniqueName}:{c.TileLocation}; level and lifetime count {state.CompletedCycles} retained. Production remains available; 20 newly verified Lv2 completions establish eligibility without crediting unknown lifetime history.",LogLevel.Warn);
        if (c.Items.Any(i => i != null && i.Stack <= 0)) return true;
        if (MachineStateMigration.Needed(state)) return true;
        if (MachineRewind.Needed(state, Now)) return true;
        if(state.Phase!="working" && state.Continuity is {Stopped:false} history && history.NextAt<=Now)return true;
        if(ModEntry.IsCrab(c))return CrabNeedsWork(c,state);

        if(state.Phase=="working"&&state.Timeline!=null&&PoweredOff(c)&&StopWouldCut(state))return true;
        if(state.Phase=="working"&&state.Timeline!=null)return state.Timeline.Cursor<state.Timeline.Plan.Count?state.Timeline.Plan[state.Timeline.Cursor].At<Now:Now>=state.DueAt
            || state.Pending.Count>0 && DeliverableNow(state).Count>0 && c.Items.Count(i=>i!=null)<c.GetActualCapacity();
        if (PreparingRecovery(c)) return state.Phase == "working" ? Now >= state.DueAt : state.Pending.Count > 0;
        if (state.Phase != "working" && !PoweredOff(c) && UpgradePlan(c,state) != null) return true;
        int required=MachineUpgrade.RequiredBait(state,Mod.Config);
        if (state.Phase == "working") return ProductionSchedule.NeedsWork(state, Now, 0, required, false);
        if(PoweredOff(c))return state.Phase=="ready";
        long bait=0; bool other=false;
        foreach(var item in c.Items)
        {
            if(item==null)continue;
            var slot=Bait(item,0,state.Level);
            if(slot!=null)bait+=slot.Count; else if(!UpgradeMaterial(item,state))other=true;
        }
        return ProductionSchedule.NeedsWork(state,Now,bait,required,other) || state.Phase=="ready" && bait>=required;
    }

    internal int Level(Item? item)
    {
        if(item is Chest c)return GetCached(c).State?.Level ?? 1;
        if(item==null || !item.modData.TryGetValue(StateKey,out var raw))return 1;
        // Called from per-frame draw/name/description hooks: parse each saved state only once.
        if(LevelCache.TryGetValue(item,out var hit)&&ReferenceEquals(hit.Raw,raw))return hit.Level;
        int level;try{level=ProductionState.Load(raw).Level;}catch{level=1;}
        LevelCache.AddOrUpdate(item,new LevelHit(raw,level));return level;
    }
    private sealed record LevelHit(string Raw,int Level);
    private readonly ConditionalWeakTable<Item,LevelHit> LevelCache=new();
    internal string Portable(Chest c){var portable=Read(c).IdleCopy();portable.Continuity=null;return portable.Save();}
    internal static bool IsTrash(Item? i)=>i!=null && MachineUpgrade.IsTrash(i.QualifiedItemId)
        && i is StardewValley.Object o && !o.questItem.Value;
    private static bool UpgradeMaterial(Item i,ProductionState s)=>s.Level<3 && MachineUpgrade.IsMaterial(i.QualifiedItemId,s.Level);
    private static IReadOnlyList<UpgradeSpend>? UpgradePlan(Chest c,ProductionState s)=>MachineUpgrade.Plan(s,
        c.Items.Select((i,n)=>(Item:i,Index:n)).Where(x=>x.Item!=null&&c.Items.Count(i=>ReferenceEquals(i,x.Item))==1)
            .Select(x=>new UpgradeSlot(x.Index,x.Item.QualifiedItemId,x.Item.Stack,SafeRod(x.Item)&&c.Items.Count(i=>ReferenceEquals(i,x.Item))==1)));
    internal static bool SafeRod(Item item)=>item is FishingRod rod && item.GetType()==typeof(FishingRod)
        && (rod.QualifiedItemId==PreparationRules.FiberglassRod&&rod.UpgradeLevel==2||rod.QualifiedItemId=="(T)IridiumRod"&&rod.UpgradeLevel==3)
        && !ReferenceEquals(item,Game1.player.CurrentTool) && rod.Stack==1 && rod.attachments.All(a=>a==null)
        && rod.enchantments.Count==0 && !rod.modData.Any();
    private void Upgrade(Chest c,ProductionState s)
    {
        var plan=UpgradePlan(c,s); if(plan==null)return;
        var inventory=c.Items.Select(Clone).ToArray();
        foreach(var spend in plan)
        {
            inventory[spend.Index].Stack-=spend.Count;
            if(inventory[spend.Index].Stack==0)inventory[spend.Index]=null!;
        }
        s.Level++; s.LevelCycles=0; s.BaitCredit=0;
        // Materials are taken now, so the next cycle (at the new level) starts now, not in the past.
        if(s.Continuity!=null)s.Continuity.NextAt=Math.Max(s.Continuity.NextAt,Math.Max(Now,s.Continuity.ObservedAt));
        Commit(c,inventory.Where(i=>i!=null).ToArray(),s);
        c.SpecialChestType=Chest.SpecialChestTypes.BigChest;
            Mod.Monitor.Log(Mod.T("diag.machine-upgraded",new{level=s.Level,location=c.Location.NameOrUniqueName+":"+c.TileLocation,cycles=s.CompletedCycles}),LogLevel.Info);
    }

    private bool CompleteFootprint(Chest c) => WorldService.Footprint(c)
        .All(p => Mod.World.TryAnchor(c.Location, new(p.X, p.Y), out var same) && ReferenceEquals(c, same));

    private void MigrateCycle(Chest c)
    {
        var original = Read(c);
        if (MachineStateMigration.Needed(original))
        {
            var migrated = MachineStateMigration.Prepare(original);
            Commit(c, null, migrated);
            Mod.Monitor.Log($"Machine cycle migrated at {c.Location?.NameOrUniqueName}:{c.TileLocation}; phase={migrated.Phase}, legacyPrecomputed={migrated.LegacyPrecomputed}, originalMinutes={migrated.OriginalCycleMinutes}, due={migrated.DueAt}. Output/quota identity preserved.", LogLevel.Trace);
        }
    }
    private void Process(Chest c, bool deferDelivery = false,bool manualStart=false)
    {
        MigrateCycle(c);
        HandleRewind(c);
        // Machines from before the power switch (BSV 1.0 saves) keep working as they did: switched on.
        if(!c.modData.ContainsKey(PowerKey))c.modData[PowerKey]="on";
        if(!Scheduling)ObserveContinuity(c);
        RemoveEmptyStacks(c);
        if(ModEntry.IsCrab(c)){ProcessCrab(c, deferDelivery);return;}
        AdvanceTimeline(c);
        if (PoweredOff(c)) StopNow(c);
        // Fish appear in the machine as soon as each cast lands, not only when the whole cycle ends.
        if(!deferDelivery&&Read(c) is {Phase:"working",Timeline:not null} live&&live.Pending.Count>0)
        {var running=live.Snapshot();Deliver(c,running,DeliverableNow(running));}
        // Work with a detached state. Render caches never observe half a transaction.
        var state = Read(c).Snapshot();

        if (state.Phase == "working")
        {
            SettleCycleLegendary(c,state);
            if (state.Timeline==null&&!MachineLegendaryBridge.CanFinish(state) || !state.Complete(Now)) return;
            if(state.Timeline!=null){Commit(c,null,state);LogCycle(c,state);}
            else MachineLegendaryBridge.Finish(state,()=>Commit(c,null,state));
        }
        // Upgrade between cycles whenever progress and materials are there. The old gate only allowed it when
        // the machine was not catching up, so a machine that restarts by itself after every cycle never upgraded.
        // Before the next cycle: upgrade first if progress and materials allow (only while switched on).
        if (!PreparingRecovery(c) && !PoweredOff(c)) Upgrade(c,state);
        if (state.Phase == "ready")
        {
            if (deferDelivery) return;
            Deliver(c, state);
            // Pending overflow blocks a new batch; already deposited outputs do not.
            if (state.Pending.Count > 0) {StopContinuity(c,"output capacity");return;}
            state = state.IdleCopy();
            Commit(c, null, state);
        }
        if (PreparingRecovery(c)) {StopContinuity(c,"recovery");return;}
        if(!manualStart && c.modData.ContainsKey(CrabCommandKey))return;
        if(PoweredOff(c)){StopContinuity(c,"switched off");return;}
        long at=StartAt(state,manualStart);if(at>Now)return;
        var policy=state.Continuity!.PolicyAt(at);
        if(!manualStart&&!policy.Auto){SetPower(c,false);StopContinuity(c,"automatic restart disabled");return;}
        var config=policy.Fish.ForNewCycle();
        int required=MachineUpgrade.RequiredBait(state,config);
        var budget=FuelBudget(state,at);
        var slots=c.Items.Select((item,index)=>Bait(item,index,state.Level) is {} b?b with{Count=FuelLimit(item,budget)}:null)
            .Where(b=>b!=null&&b.Count>0).Cast<BaitSlot>().ToArray();
        // A cycle needs a full load for the machine's (possibly just upgraded) level. Not enough: switch off
        // and wait for the player to add bait and press ON again.
        if(slots.Sum(s=>(long)s.Count)<required){SetPower(c,false);StopContinuity(c,"insufficient historically available bait");return;}
        Farmer owner=Game1.GetPlayer(c.owner.Value,onlyOnline:false)
            ??throw new InvalidOperationException($"Machine owner {c.owner.Value} unavailable; bait and cycle preserved.");
        var fish=TimelineEnvironment.CandidatesAt(c,state,owner,at);
        if(!fish.Any(f=>!f.IsLegendary)&&!TimelineEnvironment.HasLegalOtherAt(c,state,at)){StopContinuity(c,"no known eligible ecology");return;}
        var plan=Production.PlanBait(slots,required,fish.Select(f=>f.Id).ToHashSet());
        if(plan==null){SetPower(c,false);StopContinuity(c,"no eligible bait plan");return;}
        var next=MachineTimeline.StartFish(state,config,plan,owner.UniqueMultiplayerID,at,
            state.CompletedCycles==0?Game1.random.Next():MachineTimeline.SeedFor(state.Seed,(int)(state.CompletedCycles%int.MaxValue),0x51710));
        PayEvidence(c,next,plan.Select(p=>(p.Index,p.Count)),at);
        // Validate currently known candidate content before paying. Later content removal pauses, never rerolls, a paid opportunity.
        foreach (var candidate in fish) Resolve(new(candidate.Id,1));
        var inventory = c.Items.Select(Clone).ToArray();
        foreach (var spend in plan)
        {
            var item = inventory[spend.Index];
            if (item == null || item.Stack < spend.Count) throw new InvalidOperationException("Inventory changed during bait planning.");
            item.Stack -= spend.Count;
            if (item.Stack == 0) inventory[spend.Index] = null!;
        }
        TimelineEnvironment.Start(c,next);
        Commit(c, inventory.Where(i => i != null).ToArray(), next);
    }

    private static Item Clone(Item? i)
    {
        if (i == null) return null!;
        var copy = i.getOne(); copy.Stack = i.Stack; return copy;
    }
    private void RemoveEmptyStacks(Chest c)
    {
        if (c.Items.Any(i => i != null && i.Stack <= 0))
            Commit(c, c.Items.Where(i => i != null && i.Stack > 0).Select(Clone).ToArray(), Read(c).Snapshot());
    }
    private static Item Resolve(StoredCatch output)
    {
        string id=output.Id;
        var data = ItemRegistry.GetData(id);
        if (data == null || data.IsErrorItem) throw new InvalidOperationException("Missing fish content: " + id);
        var item=ItemRegistry.Create(id,quality:output.Quality);
        if(output.WildClaim!=null)
        {
            if((!LobsterKingRules.IsFish(id)&&!MutantLobsterRules.IsItem(id))||output.Count!=1||!Guid.TryParseExact(output.WildClaim,"N",out _))throw new InvalidOperationException("Invalid wild King output.");
            item.modData[MutantLobsterRules.IsItem(id)?BetterStardewValley.MutantLobsterKing.MutantWildQuota.ClaimKey:LobsterKingRules.ClaimKey]=output.WildClaim;
        }
        return item;
    }
    /// <summary>Catches that may leave a running cycle now. Fish that a legendary roll at the end of the
    /// cycle could still replace stay in the machine's ledger until the cycle settles.</summary>
    private static List<StoredCatch> DeliverableNow(ProductionState s)
    {
        var hold=new Dictionary<(string,int),long>();
        var t=s.Timeline;
        bool bossSettled=(t?.FishingConfig?.FishingBalanceVersion??0)<2||t!.BossDecision is {StatsRecorded:true};
        if(t!=null&&(!t.LegendaryDecision.StatsRecorded&&!t.LegendaryDecision.Committed||!bossSettled))
            foreach(var o in t.LegendaryOpportunities){var k=(o.OrdinaryId,o.Quality);hold[k]=hold.GetValueOrDefault(k)+o.Count;}
        var result=new List<StoredCatch>();
        foreach(var p in s.Pending)
        {
            if(p.WildClaim!=null)continue;
            var k=(p.Id,p.Quality);long keep=Math.Min(p.Count,hold.GetValueOrDefault(k));hold[k]=hold.GetValueOrDefault(k)-keep;
            if(p.Count-keep>0)result.Add(p with{Count=(int)(p.Count-keep)});
        }
        return result;
    }
    private void Deliver(Chest c, ProductionState state, List<StoredCatch>? only = null)
    {
        if (state.Pending.Count == 0) return;
        if (only != null)
        {
            // Deliver part of the ledger; whatever does not fit or is held stays pending.
            if (only.Count == 0) return;
            var held = state.Pending.Select(p => p).ToList();
            foreach (var d in only)
            {
                int i = held.FindIndex(p => p.Key == d.Key);
                held[i] = held[i] with { Count = held[i].Count - d.Count };
            }
            held.RemoveAll(p => p.Count <= 0);
            var partial = state.Snapshot(); partial.Pending = only.ToList();
            DeliverCore(c, partial, commit: false, out var inventory);
            if (inventory == null) return;
            foreach (var left in partial.Pending)
            {
                int i = held.FindIndex(p => p.Key == left.Key);
                if (i < 0) held.Add(left); else held[i] = held[i] with { Count = held[i].Count + left.Count };
            }
            state.Pending = held;
            Commit(c, inventory, state);
            return;
        }
        DeliverCore(c, state, commit: true, out _);
    }
    private void DeliverCore(Chest c, ProductionState state, bool commit, out Item[]? inventoryOut)
    {
        inventoryOut = null;
        if (state.Pending.Count == 0) return;
        var original = c.Items.Where(i => i != null).ToArray();
        var templates = state.Pending.GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>Resolve(g.First()));
        var slots = original.Select((i, index) => new BufferSlot(
            templates.TryGetValue(new StoredCatch(i.QualifiedItemId,1,i.Quality,i.modData.TryGetValue(LobsterKingRules.ClaimKey,out var token)?token:null).Key, out var output) && OutputCompatible(i, output) ? new StoredCatch(i.QualifiedItemId,1,i.Quality,token).Key : "@" + index,
            i.Stack, i.maximumStackSize())).ToArray();
        var result = DepositPlanner.Plan(slots, state.Pending, c.GetActualCapacity(), id => templates[id].maximumStackSize());
        if (result.Pending.Sum(p => p.Count) == state.Pending.Sum(p => p.Count)) return;
        var inventory = result.Slots.Select((s, i) =>
        {
            var item = i < original.Length ? Clone(original[i]) : templates[s.Key].getOne();
            item.Stack = s.Count; return item;
        }).ToArray();
        state.Pending = result.Pending.ToList();
        inventoryOut = inventory;
        if (commit) Commit(c, inventory, state);
    }

    private void Commit(Chest c,IReadOnlyList<Item>? inventory,ProductionState state)
    {InventoryMutation++;try{CommitCore(c,inventory,state);}finally{InventoryMutation--;if(inventory!=null)TrackInputs(c);}}
    private void CommitCore(Chest c, IReadOnlyList<Item>? inventory, ProductionState state)
    {
        if(!Context.IsMainPlayer || !c.GetMutex().IsLockHeld())throw new InvalidOperationException("Only the host holding the machine mutex may commit production.");
        string serialized = state.Save();
        BaitCache.Remove(c);
        if (inventory == null)
        {
            c.modData[StateKey] = serialized;
            RememberCommit(c,serialized,state);
            return;
        }
        var before = c.Items.ToArray();
        bool hadState = c.modData.TryGetValue(StateKey, out string previous);
        try
        {
            c.Items.Clear(); foreach (var i in inventory) c.Items.Add(i);
            c.modData[StateKey] = serialized;
            RememberCommit(c,serialized,state);
        }
        catch
        {
            c.Items.Clear(); foreach (var i in before) c.Items.Add(i);
            if (hadState) c.modData[StateKey] = previous; else c.modData.Remove(StateKey);
            Cache.Remove(c);
            throw;
        }
    }

    internal string Status(Chest c)
        => StatusCore(c) + " · " + Mod.T("status.collectible", new { count = CollectibleCount(c) });
    internal long CollectibleCount(Chest c)
    {
        try { return c.Items.Where(IsCollectible).Sum(i => (long)i.Stack); }
        catch { return 0; }
    }
    private string StatusCore(Chest c)
    {
        try
        {
            var s = Read(c);
            if (PreparingRecovery(c)) return Mod.T(s.Phase == "working" ? "remove.status-finishing" : "remove.status-empty");
            if(ModEntry.IsCrab(c))return CrabStatus(c,s);
            if (s.Phase == "working")
            {
                if(s.Timeline?.PausedReason!=null)return Mod.T("status.timeline-paused",new{reason=Mod.T(MachinePauseText.KeyFor(s.Timeline.PausedReason))});
                long left = Math.Max(0, s.DueAt - Now);
                return Mod.T("status.working", new { hours = left / 60, minutes = left % 60 });
            }
            if (s.Phase == "ready" && s.Pending.Count > 0) return Mod.T("status.full");
            if (PoweredOff(c)) { var (have, need) = BaitForNextCycle(c); return Mod.T("status.off", new { count = have, needed = need }); }
            if (!BetterStardewValley.Configuration.SharedPreferences.Current.AutoRestartProduction) return Mod.T("status.auto-off");
            long bait = c.Items.Select((i, n) => (long)(Bait(i, n,s.Level)?.Count ?? 0)).Sum();
            int required=MachineUpgrade.RequiredBait(s,Mod.Config);
            return Mod.T(bait >= required ? "status.waiting" : "status.idle", new { count = bait, needed = required });
        }
        catch { return Mod.T("status.error"); }
    }

    private bool CurrentMenu(out Chest c,out ItemGrabMenu menu)
    {
        c=Mod.OpenChest.Value!; menu=Game1.activeClickableMenu as ItemGrabMenu ?? null!;
        return c!=null && menu!=null && ReferenceEquals(menu.context,c);
    }
    private static Rectangle TrashButton(ItemGrabMenu menu)
        => new(menu.inventory.xPositionOnScreen+menu.inventory.width-246,menu.inventory.inventory[0].bounds.Top-42,242,36);
    internal bool MenuButton(StardewModdingAPI.SButton button)
    {
        if(!CurrentMenu(out var c,out var menu) || !c.GetMutex().IsLockHeld())return false;
        if (PreparingRecovery(c) && button is (SButton.R or SButton.ControllerY))
        {
            c.modData.Remove(RecoveryKey);
            Mod.Message("remove.resumed");
            return true;
        }
        try{if(PanelButton(c,menu,button))return true;}catch(Exception ex){PanelFailed(ex);}
        if(ModEntry.IsCrab(c)){try{return CrabMenuButton(c,menu,button);}catch{Mod.Message("status.error");return true;}}
        return false; // Fishing machines have no trash toggle.
    }
    private string UpgradeSummary(Chest c,ProductionState s,IEnumerable<(string Id,int Count)> requirements)
    {
        string materials=string.Join("; ",requirements.Select(r=>
            ItemRegistry.GetDataOrErrorItem(r.Id).DisplayName+" "+c.Items.Where(i=>i?.QualifiedItemId==r.Id&&(!r.Id.StartsWith("(T)")||SafeRod(i))).Sum(i=>(long)i.Stack)+"/"+r.Count));
        return Mod.T(s.StageProgressUnverified?"machine.stage-unknown":"machine.upgrade-summary",
            new{progress=MachineUpgrade.Progress(s),needed=MachineCycleRules.RequiredCycles(s),materials});
    }
    internal void DrawMenu(SpriteBatch b)
    {
        if(!CurrentMenu(out var c,out var menu))return;
        if(ReferenceEquals(FullMenu,menu))return; // already drawn as one window by MachineMenuDrawPatch
        try{if(TryDrawPanel(b,c,menu))return;}catch(Exception ex){PanelFailed(ex);}
        if(ModEntry.IsCrab(c)){try{DrawCrabMenu(b,c,menu);}catch{}return;}
        ProductionState s;
        try{s=Read(c);}catch{return;}
        string title=Mod.T(s.Level==3 ? "machine.lv3" : s.Level==2 ? "machine.lv2" : "machine.name")+" · "+Mod.T("status.cycles",new{count=s.CompletedCycles});
        if(s.Level<3)title+=" · "+UpgradeSummary(c,s,MachineUpgrade.Requirements(s.Level));
        if(s.Phase=="ready"||s.LastNormalTreasures.HasValue)title+=" · "+Mod.T("status.treasures",new{normal=s.Phase=="ready"?s.NormalTreasures:s.LastNormalTreasures??0,advanced=s.Phase=="ready"?s.AdvancedTreasures:s.LastAdvancedTreasures??0});
        float scale=Math.Min(.85f,(Game1.uiViewport.Width-32)/Math.Max(1,Game1.smallFont.MeasureString(title).X));
        var size=Game1.smallFont.MeasureString(title)*scale;
        int x=(Game1.uiViewport.Width-(int)size.X)/2;
        int y=Math.Max(8,menu.ItemsToGrabMenu.yPositionOnScreen-52);
        b.Draw(Game1.staminaRect,new Rectangle(x-8,y-4,(int)size.X+16,(int)size.Y+8),Color.Black*.88f);
        b.DrawString(Game1.smallFont,title,new Vector2(x,y),s.Level==3?new Color(174,105,228):s.Level==2?Color.Gold:Color.White,0,Vector2.Zero,scale,SpriteEffects.None,1);
        if(new Rectangle(x-8,y-4,(int)size.X+16,(int)size.Y+8).Contains(Game1.getMouseX(),Game1.getMouseY()))
        {
            var config=Mod.Config;int level=s.Level;
            double twice=level==1?config.FishCriticalChance:level==2?config.LevelTwoCriticalChance:config.LevelThreeCriticalChance;
            double triple=level==1?0:level==2?config.LevelTwoTripleChance:config.LevelThreeTripleChance;
            string P(double p)=>(p*100).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"%";
            var q=MachineLevelRules.QualityShares(level);var t=MachineLevelRules.TreasureShares(level);
            IClickableMenu.drawHoverText(b,Mod.T("machine.balance",new{capacity=MachineUpgrade.Capacity(level),mult=string.Join(" / ",new[]{1-twice-triple,twice,triple}.Select(P)),
                quality=string.Join(" / ",q.Select(P)),treasure=string.Join(" / ",t.Select(P)),legend=P(CycleLegendaryRules.Chance(level))})+"\n"+Mod.T("machine.bait-quality",new{mid=string.Join(" / ",MachineLevelRules.QualityShares(level,1,BaitKind.Targeted).Select(P)),high=string.Join(" / ",MachineLevelRules.QualityShares(level,1,BaitKind.Deluxe).Select(P))})+"\n"+BaitEffects(level)+"\n"+Mod.T(level>=2?"machine.no-trash":"machine.trash-lv1"),Game1.smallFont);
        }
        string status=Status(c);
        float statusScale=Math.Min(.75f,(menu.inventory.width-16)/Math.Max(1,Game1.smallFont.MeasureString(status).X));
        b.DrawString(Game1.smallFont,status,new Vector2(menu.inventory.xPositionOnScreen,menu.inventory.inventory[0].bounds.Top-34),Game1.textColor,0,Vector2.Zero,statusScale,SpriteEffects.None,1);
    }

    // ---------------- On / off switch (fishing machine) ----------------
    internal const string PowerKey = ModEntry.Tag + "Power";
    /// <summary>ON = fish and keep starting cycles while there is a full load of bait. A machine without the
    /// setting is on only while it is already fishing.</summary>
    internal bool PoweredOff(Chest c) => c.modData.TryGetValue(PowerKey, out var v) ? v != "on" : GetCached(c).State?.Phase != "working";
    private void SetPower(Chest c, bool on) { c.modData[PowerKey] = on ? "on" : "off"; BaitCache.Remove(c); }
    /// <summary>Bait a new cycle needs right now (the next level's amount when an upgrade is due first).</summary>
    internal (long Have, int Need) BaitForNextCycle(Chest c)
    {
        var s = Read(c);
        if (ModEntry.IsCrab(c)) return (StoredBait(c), RequiredBait(c));
        int level = s.Phase != "working" && UpgradePlan(c, s) != null ? s.Level + 1 : s.Level;
        int need = MachineUpgrade.RequiredBait(new ProductionState { Level = level }, Mod.Config.ForNewCycle());
        long have = c.Items.Select((i, n) => (long)(Bait(i, n, level)?.Count ?? 0)).Sum();
        return (have, need);
    }
    /// <summary>Flip the switch. Any player who has the machine open may do it; the host applies it.</summary>
    internal void TogglePower(Chest c)
    {
        if (!PoweredOff(c)) { SetPower(c, false); Game1.playSound("trashcanlid"); Game1.showGlobalMessage(Mod.T(ModEntry.IsCrab(c) ? "crab.power-now-off" : "panel.power-now-off")); return; }
        var (have, need) = BaitForNextCycle(c);
        if (Read(c).Phase != "working" && have < need) { Game1.showRedMessage(Mod.T("panel.power-need-bait", new { count = have, needed = need })); return; }
        SetPower(c, true); Game1.playSound("powerup"); Game1.showGlobalMessage(Mod.T(ModEntry.IsCrab(c) ? "crab.power-now-on" : "panel.power-now-on"));
    }
    private static bool StopWouldCut(ProductionState s)=>MachineStop.WouldCut(s);
    /// <summary>Switched off while working: what was already done stays, unused bait goes back into the machine.</summary>
    private void StopNow(Chest c)
    {
        var s = Read(c);
        var cut = MachineStop.Cut(s, Now);
        if (cut == null) return;
        var t = s.Timeline!; bool crab = s.Kind == MachineKinds.Crab;
        var fishBait = t.FishingBait.ToList(); var crabBait = t.CrabBait.ToList();
        Commit(c, null, cut.Value.State);
        foreach (var (index, count) in cut.Value.Refund)
        {
            Item item = crab ? CrabRefund(crabBait[index], count) : FishRefund(fishBait[index], count);
            var left = c.addItem(item);
            if (left != null) Game1.createItemDebris(left, DryDropPoint(c), -1, c.Location);
        }
        Mod.Monitor.Log($"[BSV {(crab ? "crab" : "fishing")} machine] switched off at {c.Location?.NameOrUniqueName}:{c.TileLocation}; {cut.Value.State.Timeline?.Plan.Count ?? 0} {(crab ? "trap checks" : "casts")} kept, {cut.Value.Refund.Sum(r => r.Count)} bait returned.", LogLevel.Info);
    }
    /// <summary>Bait that no longer fits back into the machine is dropped on the nearest dry, walkable
    /// tile, never on the water the machine stands in (items dropped on water sink).</summary>
    private static Vector2 DryDropPoint(Chest c)
    {
        var l = c.Location; var origin = c.TileLocation;
        if (l != null)
            for (int r = 0; r <= 4; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        var t = origin + new Vector2(dx, dy);
                        if (!l.isWaterTile((int)t.X, (int)t.Y) && l.isTilePassable(t)) return t * 64f + new Vector2(32f, 32f);
                    }
        return (Game1.player?.Tile ?? origin) * 64f + new Vector2(32f, 32f);
    }
    private static Item FishRefund(BaitSpend bait, int count) => bait.Kind switch
    {
        BaitKind.Deluxe => ItemRegistry.Create("(O)DeluxeBait", count),
        BaitKind.Challenge => ItemRegistry.Create("(O)ChallengeBait", count),
        BaitKind.Wild => ItemRegistry.Create("(O)774", count),
        BaitKind.Magnet => ItemRegistry.Create("(O)703", count),
        BaitKind.Magic => ItemRegistry.Create("(O)908", count),
        BaitKind.GiantLobster => ItemRegistry.Create(GiantLobsterRules.Bait, count),
        BaitKind.Targeted when bait.Target != null => FlavoredBait(bait.Target, count),
        _ => ItemRegistry.Create("(O)685", count)
    };
    /// <summary>The exact bait that went into the traps: same item, quality, flavour and data.</summary>
    private static Item CrabRefund(CrabBait bait, int count)
    {
        string? parent = null; Dictionary<string, string>? data = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrEmpty(bait.Metadata) ? "{}" : bait.Metadata);
            if (doc.RootElement.TryGetProperty("Parent", out var p) && p.ValueKind == JsonValueKind.String) parent = p.GetString();
            if (doc.RootElement.TryGetProperty("Data", out var d) && d.ValueKind == JsonValueKind.Object)
                data = d.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString() ?? "");
        }
        catch (JsonException) { }
        var item = !string.IsNullOrEmpty(parent) && ItemRegistry.Create(parent) is StardewValley.Object fish && bait.Id == "(O)SpecificBait"
            ? (StardewValley.Object)FlavoredBait(fish.QualifiedItemId, count)
            : (StardewValley.Object)ItemRegistry.Create(bait.Id, count, bait.Quality);
        item.Quality = bait.Quality;
        if (parent != null) item.preservedParentSheetIndex.Value = parent;
        if (data != null) foreach (var (k, v) in data) item.modData[k] = v;
        return item;
    }
    private static Item FlavoredBait(string fishId, int count)
    {
        var fish = ItemRegistry.Create<StardewValley.Object>(fishId);
        var bait = ItemRegistry.GetObjectTypeDefinition().CreateFlavoredBait(fish);
        bait.Stack = count; return bait;
    }
}

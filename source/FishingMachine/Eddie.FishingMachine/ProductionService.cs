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
    internal void Reset() { TraceStates.Clear();LockRequests.Clear(); Errors.Clear(); Cache = new(); BaitCache=new(); }

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

    internal static BaitSlot? Bait(Item? item, int index, int level = 1)
    {
        if (item is not StardewValley.Object o || o.Stack <= 0 || o.questItem.Value) return null;
        return o.QualifiedItemId switch
        {
            "(O)685" => new(index, o.Stack, BaitKind.Normal),
            GiantLobsterRules.Bait => new(index, o.Stack, BaitKind.GiantLobster),
            "(O)ChallengeBait" when level >= 2 => new(index, o.Stack, BaitKind.Challenge),
            "(O)DeluxeBait" => new(index, o.Stack, BaitKind.Deluxe),
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
        if(state.Phase!="working" && state.Continuity is {Stopped:false} history && history.NextAt<=Now)return true;
        if(ModEntry.IsCrab(c))return CrabNeedsWork(c,state);

        if(state.Phase=="working"&&state.Timeline!=null)return state.Timeline.Cursor<state.Timeline.Plan.Count?state.Timeline.Plan[state.Timeline.Cursor].At<Now:Now>=state.DueAt;
        if (PreparingRecovery(c)) return state.Phase == "working" ? Now >= state.DueAt : state.Pending.Count > 0;
        if (state.Phase != "working" && UpgradePlan(c,state) != null) return true;
        int required=MachineUpgrade.RequiredBait(state,Mod.Config);
        if (state.Phase == "working") return ProductionSchedule.NeedsWork(state, Now, 0, required, false);
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
        try{return ProductionState.Load(raw).Level;}catch{return 1;}
    }
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
        s.Level++; s.LevelCycles=0;
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
            Mod.Monitor.Log($"Machine cycle migrated at {c.Location?.NameOrUniqueName}:{c.TileLocation}; phase={migrated.Phase}, legacyPrecomputed={migrated.LegacyPrecomputed}, originalMinutes={migrated.OriginalCycleMinutes}, due={migrated.DueAt}. Output/quota identity preserved.", LogLevel.Debug);
        }
    }
    private void Process(Chest c, bool deferDelivery = false,bool manualStart=false)
    {
        MigrateCycle(c);
        if(!Scheduling)ObserveContinuity(c);
        RemoveEmptyStacks(c);
        if(ModEntry.IsCrab(c)){ProcessCrab(c, deferDelivery);return;}
        AdvanceTimeline(c);
        // Work with a detached state. Render caches never observe half a transaction.
        var state = Read(c).Snapshot();

        if (state.Phase == "working")
        {
            SettleCycleLegendary(c,state);
            if (state.Timeline==null&&!MachineLegendaryBridge.CanFinish(state) || !state.Complete(Now)) return;
            if(state.Timeline!=null)Commit(c,null,state);
            else MachineLegendaryBridge.Finish(state,()=>Commit(c,null,state));
        }
        if (!PreparingRecovery(c) && (state.Continuity?.NextAt??Now)>=Now) Upgrade(c,state);
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
        long at=StartAt(state,manualStart);if(at>Now)return;
        var policy=state.Continuity!.PolicyAt(at);
        if(!manualStart&&!policy.Auto){StopContinuity(c,"automatic restart disabled");return;}
        var config=policy.Fish.ForNewCycle();
        int required=MachineUpgrade.RequiredBait(state,config);
        var budget=FuelBudget(state,at);
        var slots=c.Items.Select((item,index)=>Bait(item,index,state.Level) is {} b?b with{Count=FuelLimit(item,budget)}:null)
            .Where(b=>b!=null&&b.Count>0).Cast<BaitSlot>().ToArray();
        if(slots.Sum(s=>(long)s.Count)<required){StopContinuity(c,"insufficient historically available bait");return;}
        Farmer owner=Game1.GetPlayer(c.owner.Value,onlyOnline:false)
            ??throw new InvalidOperationException($"Machine owner {c.owner.Value} unavailable; bait and cycle preserved.");
        var fish=TimelineEnvironment.CandidatesAt(c,state,owner,at);
        if(!fish.Any(f=>!f.IsLegendary)&&!TimelineEnvironment.HasLegalOtherAt(c,state,at)){StopContinuity(c,"no known eligible ecology");return;}
        var plan=Production.PlanBait(slots,required,fish.Select(f=>f.Id).ToHashSet());
        if(plan==null){StopContinuity(c,"no eligible bait plan");return;}
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
    private void Deliver(Chest c, ProductionState state)
    {
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
        Commit(c, inventory, state);
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
        try{if(PanelButton(c,menu,button))return true;}catch{}
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
        try{if(TryDrawPanel(b,c,menu))return;}catch{}
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
                quality=string.Join(" / ",q.Select(P)),treasure=string.Join(" / ",t.Select(P)),legend=P(CycleLegendaryRules.Chance(level))})+"\n"+Mod.T(level>=2?"machine.no-trash":"machine.trash-lv1"),Game1.smallFont);
        }
        string status=Status(c);
        float statusScale=Math.Min(.75f,(menu.inventory.width-16)/Math.Max(1,Game1.smallFont.MeasureString(status).X));
        b.DrawString(Game1.smallFont,status,new Vector2(menu.inventory.xPositionOnScreen,menu.inventory.inventory[0].bounds.Top-34),Game1.textColor,0,Vector2.Zero,statusScale,SpriteEffects.None,1);
    }
}

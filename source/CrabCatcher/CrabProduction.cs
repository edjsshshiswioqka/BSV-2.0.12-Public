using System.Text.Json;
using Eddie.FishingMachine.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley.Menus;
namespace Eddie.FishingMachine;

internal sealed partial class ProductionService
{
    internal const string CrabCommandKey=ModEntry.Tag+"CrabCommand";
    internal static bool CrabBaitItem(Item? item)=>item is StardewValley.Object o && o.Stack>0 && o.Category==StardewValley.Object.baitCategory && !o.questItem.Value;
    internal static CrabBait? CrabSlot(Item? item,int index)
    {
        if(!CrabBaitItem(item))return null;
        var o=(StardewValley.Object)item!;
        string metadata=JsonSerializer.Serialize(new{Parent=o.preservedParentSheetIndex.Value,Data=o.modData.Keys.ToDictionary(k=>k,k=>o.modData[k])});
        return new(index,o.QualifiedItemId,o.Stack,o.QualifiedItemId=="(O)DeluxeBait",o.Quality,metadata);
    }
    internal IReadOnlyList<CrabAquatic> CrabPool(Chest c)
    {
        var habitats=c.Location.GetCrabPotFishForTile(c.TileLocation).ToHashSet(StringComparer.Ordinal);
        return CrabRules.NativePool(DataLoader.Fish(Game1.content),habitats);
    }
    internal Farmer? CrabOperator(Chest c)
    {
        long id=Read(c).OwnerId;
        return Game1.GetPlayer(id==0?c.owner.Value:id,onlyOnline:false);
    }
    internal int RequiredBait(Chest c)
    {
        var s=Read(c);
        if(s.Kind!=MachineKinds.Crab)return MachineUpgrade.RequiredBait(s,Mod.Config);
        return s.Phase=="working"?s.BaitUsed:Mod.CrabConfig.Required(s.Level,CrabOperator(c)?.professions.Contains(11)==true);
    }
    internal long StoredBait(Chest c)
    {
        if(ModEntry.IsCrab(c))return c.Items.Where(CrabBaitItem).Sum(i=>(long)i.Stack);
        int level=Level(c);return c.Items.Select((i,n)=>(long)(Bait(i,n,level)?.Count??0)).Sum();
    }
    private bool CrabNeedsWork(Chest c,ProductionState s)=>s.Phase=="working"?PoweredOff(c)&&StopWouldCut(s)||(s.Timeline!=null&&s.Timeline.Cursor<s.Timeline.Plan.Count?s.Timeline.Plan[s.Timeline.Cursor].At<Now:Now>=s.DueAt)
        :s.Phase=="ready"
        || !PoweredOff(c) && !PreparingRecovery(c) && !c.modData.ContainsKey(CrabCommandKey) && StoredBait(c)>=RequiredBait(c);
    private void ProcessCrab(Chest c, bool deferDelivery = false)
    {
        MigrateCycle(c);
        HandleRewind(c);
        RemoveEmptyStacks(c);
        AdvanceTimeline(c);
        // Switched off while the traps are out: unused bait comes back, the checks already made stay.
        if(PoweredOff(c))StopNow(c);
        var s=Read(c).Snapshot();
        if(s.Phase=="working")
        {
            if(Now>=s.DueAt&&s.Timeline==null&&s.Pending.Any(p=>p.WildClaim==null&&(MutantLobsterRules.IsItem(p.Id)||LobsterKingRules.IsFish(p.Id))))
            {
                // Legacy plans lack a logical rare capture event/annual claim. Preserve
                // already delivered inventories; only uncommitted plans get ordinary fallback.
                var pool=CrabPool(c);if(pool.Count==0)throw new InvalidOperationException("Legacy rare plan has no legal ordinary trap fallback; paid batch retained.");
                var random=new Random(MachineTimeline.SeedFor(s.Seed,0,0x1529));
                s.Pending=s.Pending.Select(p=>
                {
                    if(p.WildClaim!=null||!MutantLobsterRules.IsItem(p.Id)&&!LobsterKingRules.IsFish(p.Id))return p;
                    double roll=random.NextDouble()*pool.Sum(f=>f.Weight);string id=pool[^1].Id;
                    foreach(var fish in pool){roll-=fish.Weight;if(roll<0){id=fish.Id;break;}}
                    return p with{Id=id};
                }).ToList();
            }
            if(!s.Complete(Now))return;
            Commit(c,null,s);
            if(Mod.CrabConfig.DebugMode)Mod.Monitor.Log($"[CrabCatcher] Complete id={s.PersistentId} owner={s.OwnerId} cycles={s.CompletedCycles}; rolls={s.Crab!.Rolls}, aquatic={s.FishCount}, junk={s.OtherCount}, crits={s.Crab.MachineCrits}, book={s.Crab.BookDoubles}",LogLevel.Debug);
        }
        if(s.Phase!="ready" || deferDelivery)return;
        Deliver(c,s);
        if(s.Pending.Count!=0){StopContinuity(c,"output capacity");return;}
        Commit(c,null,s.IdleCopy());
    }
    internal bool StartCrab(Chest c,Farmer owner,bool manualStart=false)
    {
        if(!Context.IsMainPlayer||!c.GetMutex().IsLockHeld())throw new InvalidOperationException("Crab production requires host lock.");
        if (PreparingRecovery(c)) {StopContinuity(c,"recovery");return false;}
        if(!Scheduling)ObserveContinuity(c);
        ProcessCrab(c);var previous=Read(c);
        if(previous.Phase=="ready" && previous.Pending.Count==0)previous=previous.IdleCopy();
        if(previous.Phase!="idle")return false;
        if(PoweredOff(c)){StopContinuity(c,"switched off");return false;}
        long at=StartAt(previous,manualStart);if(at>Now)return false;
        var policy=previous.Continuity!.PolicyAt(at);
        if(!manualStart&&!policy.Auto){SetPower(c,false);StopContinuity(c,"automatic restart disabled");return false;}
        bool luremaster=policy.Lure,book=policy.Book;
        var config=policy.Crab;var budget=FuelBudget(previous,at);
        var bait=c.Items.Select((i,n)=>CrabSlot(i,n) is {} b?b with{Count=FuelLimit(i,budget)}:null).Where(b=>b!=null&&b.Count>0).Cast<CrabBait>();
        // A full load is needed; not enough left: switch off and wait for the player to add bait and press ON.
        var plan=CrabRules.PlanBait(bait,config.Required(previous.Level,luremaster));if(plan==null){SetPower(c,false);StopContinuity(c,"insufficient historically available bait");return false;}
        var pool=CrabPool(c);if(pool.Count==0){StopContinuity(c,"unsupported trap ecology");return false;}
        var next=MachineTimeline.StartCrab(previous,config,plan,owner.UniqueMultiplayerID,luremaster,book,at,
            previous.CompletedCycles==0?Game1.random.Next():MachineTimeline.SeedFor(previous.Seed,(int)(previous.CompletedCycles%int.MaxValue),0x51710));
        PayEvidence(c,next,plan.Select(p=>(p.Index,p.Count)),at);
        foreach(var candidate in pool)Resolve(new(candidate.Id,1));
        var inventory=c.Items.Select(Clone).ToArray();
        foreach(var spend in plan)
        {var i=inventory[spend.Index];if(i==null||i.Stack<spend.Count)throw new InvalidOperationException("Bait changed while planning.");i.Stack-=spend.Count;if(i.Stack==0)inventory[spend.Index]=null!;}
        TimelineEnvironment.Start(c,next);
        Commit(c,inventory.Where(i=>i!=null).ToArray(),next);
        if(Mod.CrabConfig.DebugMode)Mod.Monitor.Log($"[CrabCatcher] Start id={next.PersistentId} owner={next.OwnerId}; level={next.Level}, bait={next.BaitUsed}, rolls={next.Crab!.Rolls}, Luremaster={luremaster}, book={book}",LogLevel.Debug);
        return true;
    }
    internal string? CrabControl(Chest c,Farmer who,ActionKind kind)
    {
        if(!Context.IsMainPlayer||!c.GetMutex().IsLockHeld()||!ModEntry.IsMachine(c))return "network.error";
        if(!ModEntry.IsCrab(c))
        {if(kind!=ActionKind.Start)return "network.error";Process(c,manualStart:true);return null;}
        ProcessCrab(c);var state=Read(c).Snapshot();
        if(kind==ActionKind.Start)
        {
            // Record the authenticated operator for subsequent internal automatic cycles.
            if(state.Phase!="idle")return null;
            state.OwnerId=who.UniqueMultiplayerID;Commit(c,null,state);StartCrab(c,who,manualStart:true);return null;
        }
        if(kind!=ActionKind.Upgrade||state.Phase!="idle"||state.Level!=1||state.CompletedCycles<CrabRules.UpgradeCycles)return "crab.upgrade-unavailable";
        var requirements=CrabRules.Upgrade(who.professions.Contains(7));
        var plan=CrabRules.Plan(c.Items.Select((i,n)=>(i,n)).Where(x=>x.i is StardewValley.Object o && !o.questItem.Value && x.i.Stack>0)
            .Select(x=>new UpgradeSlot(x.n,x.i.QualifiedItemId,x.i.Stack)),requirements);
        if(plan==null)return "crab.upgrade-materials";
        var inventory=c.Items.Select(Clone).ToArray();
        foreach(var p in plan){inventory[p.Index].Stack-=p.Count;if(inventory[p.Index].Stack==0)inventory[p.Index]=null!;}
        state.Level=2;state.LevelCycles=0;Commit(c,inventory.Where(i=>i!=null).ToArray(),state);return null;
    }
    private string CrabStatus(Chest c,ProductionState s)
    {
        if (PreparingRecovery(c)) return Mod.T(s.Phase == "working" ? "remove.status-finishing" : "remove.status-empty");
        if(s.Phase=="working"){if(s.Timeline?.PausedReason!=null)return Mod.T("status.timeline-paused",new{reason=Mod.T(MachinePauseText.KeyFor(s.Timeline.PausedReason))});long left=Math.Max(0,s.DueAt-Now);return Mod.T("status.working",new{hours=left/60,minutes=left%60});}
        if(s.Phase=="ready" && s.Pending.Count>0)return Mod.T("status.full");
        if(PoweredOff(c))return Mod.T("status.off",new{count=StoredBait(c),needed=RequiredBait(c)});
        if(!BetterStardewValley.Configuration.SharedPreferences.Current.AutoRestartProduction)return Mod.T("status.auto-off");
        int required=RequiredBait(c);
        return Mod.T("crab.status",new{count=StoredBait(c),needed=required,rolls=Mod.CrabConfig.Required(s.Level,false)});
    }
    private bool CrabMenuButton(Chest c,ItemGrabMenu menu,SButton button)
    {
        bool clicked=false;if(button==SButton.MouseLeft){Game1.PushUIMode();try{clicked=TrashButton(menu).Contains(Game1.getMouseX(),Game1.getMouseY());}finally{Game1.PopUIMode();}}
        if(!clicked&&button is not(SButton.T or SButton.ControllerX))return false;
        var s=Read(c);if(s.Level!=1||s.CompletedCycles<MachineCycleRules.RequiredCycles(s)||s.Phase=="working"){Mod.Message("crab.upgrade-unavailable");return true;}
        Mod.Actions.Control(c,ActionKind.Upgrade);Mod.ReleaseChest(startCrab:false);Game1.exitActiveMenu();return true;
    }
    private void DrawCrabMenu(SpriteBatch b,Chest c,ItemGrabMenu menu)
    {
        var s=Read(c);string title=Mod.T(s.Level==2?"crab.lv2":"crab.name")+" · "+Mod.T("status.cycles",new{count=s.CompletedCycles});
        if(s.Level==1)title+=" · "+UpgradeSummary(c,s,CrabRules.Upgrade(false));
        float scale=Math.Min(.8f,(Game1.uiViewport.Width-32)/Math.Max(1,Game1.smallFont.MeasureString(title).X));
        var size=Game1.smallFont.MeasureString(title)*scale;var p=new Vector2((Game1.uiViewport.Width-size.X)/2,Math.Max(8,menu.ItemsToGrabMenu.yPositionOnScreen-52));
        b.Draw(Game1.staminaRect,new Rectangle((int)p.X-8,(int)p.Y-4,(int)size.X+16,(int)size.Y+8),Color.Black*.88f);
        b.DrawString(Game1.smallFont,title,p,s.Level==2?Color.Gold:Color.White,0,Vector2.Zero,scale,SpriteEffects.None,1);
        var button=TrashButton(menu);bool ready=s.Level==1&&s.CompletedCycles>=MachineCycleRules.RequiredCycles(s)&&s.Phase!="working";
        b.Draw(Game1.staminaRect,button,ready?new Color(130,94,28):new Color(76,61,47));
        string label=Mod.T(s.Level==2?"crab.max-level":"crab.upgrade-button");float labelScale=Math.Min(.8f,(button.Width-16)/Math.Max(1,Game1.smallFont.MeasureString(label).X));
        b.DrawString(Game1.smallFont,label,new Vector2(button.X+8,button.Y+7),Color.White,0,Vector2.Zero,labelScale,SpriteEffects.None,1);
        string status=Status(c);float statusScale=Math.Min(.7f,(button.Left-menu.inventory.xPositionOnScreen-12)/Math.Max(1,Game1.smallFont.MeasureString(status).X));
        b.DrawString(Game1.smallFont,status,new Vector2(menu.inventory.xPositionOnScreen,button.Y+8),Game1.textColor,0,Vector2.Zero,statusScale,SpriteEffects.None,1);
        if(button.Contains(Game1.getMouseX(),Game1.getMouseY()))IClickableMenu.drawHoverText(b,Mod.T("crab.upgrade-help"),Game1.smallFont);
    }
}

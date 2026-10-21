using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Menus;
using StardewValley.Tools;

internal static class MultiplayerCycleChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var a=new Farmer{UniqueMultiplayerID=101};var b=new Farmer{UniqueMultiplayerID=202};
        Game1.Players[a.UniqueMultiplayerID]=a;Game1.Players[b.UniqueMultiplayerID]=b;
        void As(Farmer who){Game1.player=who;Context.IsMainPlayer=who==Game1.MasterPlayer;}
        foreach(int level in new[]{1,2,3})
        {
            var mod=new ModEntry{Config=new BalanceConfig{Normal=new(){Fish=1}}};
            var host=new ProductionService(mod);var secondHostCallback=new ProductionService(mod);var client=new ProductionService(mod);
            var c=new Chest();c.Location.objects[c.TileLocation]=c;mod.Machines.All[c]=c.Location;c.owner.Value=a.UniqueMultiplayerID;
            var original=new ProductionState{Level=level,CompletedCycles=level*50-2,LevelCycles=48};
            c.modData[ProductionService.StateKey]=original.Save();
            int required=MachineUpgrade.RequiredBait(original,mod.Config);
            // Two genuinely distinct clients operate the same native inventory. Only host production writes progress.
            for(int cycle=1;cycle<=2;cycle++)
            {
                var depositor=cycle==1?a:b;
                As(depositor);c.GetMutex().Locked=true;c.GetMutex().Holder=depositor.UniqueMultiplayerID;
                c.Items.Clear();c.Items.Add(ItemRegistry.Create("(O)685",required));
                string before=c.modData[ProductionService.StateKey];client.Tick();
                check(c.modData[ProductionService.StateKey]==before && c.Items.Single().Stack==required,$"Lv{level} farmhand deposit cannot independently consume bait or advance counter");
                c.GetMutex().ReleaseLock();As(Game1.MasterPlayer);host.Tick();
                var working=host.Read(c);
                check(working.Phase=="working" && working.BaitUsed==required && c.Items.Count==0,$"Lv{level} host consumes exactly one full cycle after {depositor.UniqueMultiplayerID} closes");
                check(working.Timeline?.Plan.Count==required&&working.Timeline.Cursor==0&&working.Pending.Count==0,$"Lv{level} one scheduled decision per paid bait, no future outcomes at start");
                check(working.CompletedCycles==original.CompletedCycles+cycle-1,$"Lv{level} starting is not completing a cycle");
                var immutable=working.Save();As(cycle==1?b:a);client.Tick();
                check(host.Read(c).Save()==immutable,$"Lv{level} different player's callback leaves due time, seed and batch unchanged");
                // Even if another player has the menu open when due, progress must wait and then settle once.
                c.GetMutex().Locked=true;c.GetMutex().Holder=Game1.player.UniqueMultiplayerID;Game1.Date.TotalDays+=level+1;
                As(Game1.MasterPlayer);host.Tick();check(host.Read(c).Phase=="working",$"Lv{level} remote menu mutex prevents simultaneous host production");
                c.GetMutex().ReleaseLock();Game1.Offline.Add(a.UniqueMultiplayerID);SimulationPump.Drain(host);SimulationPump.Drain(secondHostCallback);
                var done=host.Read(c);
                var settled=c.modData.Writes.Where(p=>p.Key==ProductionService.StateKey).Select(p=>ProductionState.Load(p.Value)).Last(w=>w.Phase=="ready");
                check(settled.FishCount+settled.OtherCount+settled.EmptyCount==required&&settled.FishCount==required&&done.Phase=="idle",$"Lv{level} one completed primary decision per paid bait");
                check(done.CompletedCycles==original.CompletedCycles+cycle && done.LevelCycles==48+cycle,$"Lv{level} A then B complete cycles accumulate per machine exactly once, including owner offline");
                var delivered=c.Items.Sum(i=>i.Stack);host.Reset();secondHostCallback.Reset();host.Tick();secondHostCallback.Tick();
                check(host.Read(c).CompletedCycles==original.CompletedCycles+cycle && c.Items.Sum(i=>i.Stack)==delivered,$"Lv{level} repeated host tick and reload do not duplicate completion or output");
                Game1.Offline.Remove(a.UniqueMultiplayerID);
            }
            check(host.Read(c).CompletedCycles==level*50 && host.Read(c).LevelCycles==50,$"Lv{level} two players produce two total and two stage cycles");
            // A lagging client toggles its trash setting from an old snapshot after two host completions.
            var replica=new Chest();replica.modData[ProductionService.StateKey]=original.Save();
            mod.OpenChest.Value=replica;Game1.activeClickableMenu=new ItemGrabMenu{context=replica};
            As(b);replica.GetMutex().Locked=true;replica.GetMutex().Holder=b.UniqueMultiplayerID;
            replica.modData.WrittenKeys.Clear();
            check(!client.MenuButton(SButton.T),"removed trash hotkey does not write from farmhand");
            check(replica.modData.WrittenKeys.Count==0,"lagging farmhand writes neither deprecated setting nor cycle JSON");
            string completed=c.modData[ProductionService.StateKey];
            foreach(var key in replica.modData.WrittenKeys)c.modData[key]=replica.modData[key]; // delayed packet arrives at host
            As(Game1.MasterPlayer);check(c.modData[ProductionService.StateKey]==completed && host.Read(c).CompletedCycles==level*50 && !host.Read(c).AutoTrash,"delayed setting replication preserves both completed cycles without applying deprecated option");
            Game1.activeClickableMenu=null;mod.OpenChest.Value=null;replica.GetMutex().ReleaseLock();
            c.Items.Clear();host.Tick();var carried=host.Portable(c);
            var moved=new Chest();moved.owner.Value=b.UniqueMultiplayerID;moved.modData[ProductionService.StateKey]=carried;
            check(host.Read(moved).CompletedCycles==level*50 && host.Read(moved).LevelCycles==50 && !host.Read(moved).AutoTrash,"another player's pickup/replacement preserves machine counters and ignored legacy setting");
            var request=new MachineRequest{Id=Guid.NewGuid().ToString("N"),Sequence=1,Actor=b.UniqueMultiplayerID,Location="Beach",X=1,Y=1,MachineState=carried};
            var rejected=MachineReceipt.For(request,ReceiptResult.Rejected);
            check(ActionProtocol.Effect(request,rejected)==ReceiptEffect.ReturnMachine && rejected.MachineState==carried,"failed client placement refunds the same progressed machine");
            check(ActionProtocol.Order(request,rejected)==RequestOrder.Replay && ActionProtocol.Effect(null,rejected)==ReceiptEffect.Ignore,"duplicate receipt is replayed, not a second machine return");
            if(level<3)
            {
                foreach(var material in MachineUpgrade.Requirements(level))
                {
                    if(material.Id.StartsWith("(T)"))for(int n=0;n<material.Count;n++)c.Items.Add(new FishingRod{QualifiedItemId=material.Id,UpgradeLevel=3});
                    else c.Items.Add(ItemRegistry.Create(material.Id,material.Count));
                }
                host.Tick();check(host.Read(c).Level==level+1 && host.Read(c).CompletedCycles==level*50 && host.Read(c).LevelCycles==0,"A/B shared threshold upgrades once, preserving total and resetting only new stage");
            }
        }
        // Lv3 boundary fuel, stack splitting, mixed bait, and old working batch preservation.
        var m=new ModEntry();var service=new ProductionService(m);var machine=new Chest();
        machine.Location.objects[machine.TileLocation]=machine;m.Machines.All[machine]=machine.Location;
        machine.modData[ProductionService.StateKey]=new ProductionState{Level=3,CompletedCycles=100}.Save();
        machine.Items.Add(ItemRegistry.Create("(O)685",399));service.Tick();
        check(service.Read(machine).Phase=="idle" && machine.Items[0].Stack==399,"399 bait cannot start Lv3 and is not partially consumed");
        machine.Items.Add(ItemRegistry.Create("(O)ChallengeBait",11));service.Tick();
        var batch=service.Read(machine);
        check(batch.BaitUsed==400 && batch.Timeline?.Plan.Count==400&&batch.Timeline.FishingBait.Sum(b=>b.Count)==400 && machine.Items.Single().Stack==10,"mixed bait consumes exactly 400 across stacks and leaves ten surplus");
        var old=new ProductionState{Level=3,CompletedCycles=100,Phase="working",StartedAt=ProductionService.Now,DueAt=ProductionService.Now+1440,BaitUsed=450,FishCount=450,Pending=new(){new("(O)128",450,4)}};
        machine.modData[ProductionService.StateKey]=old.Save();machine.Items.Clear();var oldRaw=old.Save();service.Tick();
        var migrated=service.Read(machine);
        check(migrated.Pending.SequenceEqual(old.Pending) && migrated.BaitUsed==450 && migrated.DueAt==old.DueAt && migrated.LegacyPrecomputed,"already-working 0.8.0 Lv3 keeps all 450-bait results and its original paid deadline");
        var migratedRaw=migrated.Save();service.Reset();service.Tick();check(service.Read(machine).Save()==migratedRaw,"host migration persists once across cache reset");
        Game1.Date.TotalDays+=2;service.Tick();check(service.Read(machine).CompletedCycles==101 && machine.Items.Sum(i=>i.Stack)==450,"old 450-bait batch finishes and increments once");
        machine.Items.Clear();machine.Items.Add(ItemRegistry.Create("(O)685",400));service.Tick();
        check(service.Read(machine).BaitUsed==400 && service.Read(machine).CompletedCycles==101,"next batch applies 400 without resetting old progression");
        As(Game1.MasterPlayer);Game1.Players.Clear();Game1.Offline.Clear();Game1.activeClickableMenu=null;
    }
}

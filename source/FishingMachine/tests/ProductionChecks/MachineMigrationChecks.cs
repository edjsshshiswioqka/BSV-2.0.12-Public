using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;

internal static class MachineMigrationChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var mod=new ModEntry();var service=new ProductionService(mod);
        var chest=new Chest();chest.Location.objects[chest.TileLocation]=chest;
        chest.modData[ModEntry.Tag+"MachineId"]=Guid.NewGuid().ToString("N");mod.Machines.All[chest]=chest.Location;
        var paid=Production.Begin(new(){FishingBalanceVersion=0,BaitPerCycle=350,CycleGameMinutes=1440},new[]{new BaitSpend(0,350,BaitKind.Normal,null)},new[]{new FishChoice("(O)128",1)},new[]{"(O)168"},ProductionService.Now,91);
        paid.Schema=6;paid.CycleRulesVersion=0;paid.CycleToken=Guid.NewGuid().ToString("N");paid.LegendaryYear=1;
        string before=paid.Save();chest.modData[ProductionService.StateKey]=before;
        chest.Items.Add(ItemRegistry.Create("(O)685",350));
        for(int i=0;i<10;i++){service.Read(chest);service.Status(chest);service.Phase(chest);}
        check(chest.modData[ProductionService.StateKey]==before,"diagnostic and render reads never migrate or write");
        Context.IsMainPlayer=false;service.Tick();Context.IsMainPlayer=true;
        check(chest.modData[ProductionService.StateKey]==before,"farmhand cannot migrate host state");
        mod.Saving=true;service.Tick();mod.Saving=false;
        check(chest.modData[ProductionService.StateKey]==before,"saving guard retains old state");
        chest.GetMutex().Locked=true;service.Tick();chest.GetMutex().ReleaseLock();
        check(chest.modData[ProductionService.StateKey]==before,"open mutex blocks migration");
        chest.modData.FailNextWrite=true;service.Tick();
        check(chest.modData[ProductionService.StateKey]==before && chest.Items.Single().Stack==350,"failed migration write retains paid state and inventory");
        service.Tick();var migrated=service.Read(chest);
        check(migrated.LegacyPrecomputed && migrated.DueAt==paid.DueAt && migrated.Pending.SequenceEqual(paid.Pending) && migrated.Seed==91 && migrated.CycleToken==paid.CycleToken && migrated.LegendaryYear==1,"runtime migration preserves exact output and quota identity");
        string after=migrated.Save();service.Reset();service.Tick();
        check(service.Read(chest).Save()==after && chest.Items.Single().Stack==350,"repeated polling/cache reset does not extend timer or charge bait");
        chest.modData[ProductionService.RecoveryKey]="true";
        Game1.Date.TotalDays++;service.Tick();
        check(service.Read(chest).CompletedCycles==1 && service.Read(chest).Pending.Count==0 && chest.Items.Where(i=>i.QualifiedItemId=="(O)685").Sum(i=>i.Stack)==350,"legacy completion preserves its original one-day deadline; no refuel in recovery");
        foreach(var group in paid.Pending.GroupBy(p=>(p.Id,p.Quality)))
            check(chest.Items.Where(i=>i.QualifiedItemId==group.Key.Id && i.Quality==group.Key.Quality).Sum(i=>i.Stack)==group.Sum(p=>p.Count),"legacy delivered quantity/quality exact");
        service.Tick();check(service.Read(chest).CompletedCycles==1,"legacy completion repeat cannot recount");

        // No fish pool/owner fallback is allowed to substitute the host for a missing owner.
        var missing=new Chest();missing.owner.Value=987654321;
        missing.Location.objects[missing.TileLocation]=missing;missing.modData[ModEntry.Tag+"MachineId"]=Guid.NewGuid().ToString("N");
        mod.Machines.All[missing]=missing.Location;missing.Items.Add(ItemRegistry.Create("(O)685",350));
        service.Tick();check(service.Read(missing).Phase=="idle" && missing.Items.Single().Stack==350,"unavailable owner pauses startup without borrowing host identity or spending bait");
        var bait=ItemRegistry.Create(GiantLobsterRules.Bait,5);missing.Items.Add(bait);
        check(ProductionService.InternalInputProtected(bait,service.Read(missing)),"exclusive Spirit bait is protected as input rather than exported as output");
        var ordinary=Production.Begin(new(){BaitPerCycle=350},new[]{new BaitSpend(0,350,BaitKind.Normal,null)},new[]{new FishChoice("(O)128",1)},new[]{"(O)168"},0,33);
        MachineLegendaryBridge.Start(Game1.MasterPlayer,ordinary,()=>{});
        check(ordinary.OwnerId==Game1.MasterPlayer.UniqueMultiplayerID && ordinary.LegendaryPlans.Count==0,"ordinary batches persist their owner even with no legendary reservation");

        // Existing Spirit reservation is not reassigned by extending a legacy cycle across New Year.
        Game1.MasterPlayer.modData.Clear();int oldYear=Game1.year;
        var spirit=new ProductionState{Schema=6,Phase="working",Level=3,OwnerId=Game1.MasterPlayer.UniqueMultiplayerID,StartedAt=111*1440,DueAt=112*1440,BaitUsed=1,FishCount=1,
            Pending=new(){new(Production.SpiritId,1,4)},LegendaryPlans=new(){new(Production.SpiritId,1,4,"(O)128")}};
        MachineLegendaryBridge.Start(Game1.MasterPlayer,spirit,()=>{});int reservedYear=spirit.LegendaryYear;
        spirit=MachineStateMigration.Prepare(spirit);Game1.year=reservedYear+1;
        spirit.Complete(spirit.DueAt);MachineLegendaryBridge.Finish(spirit,()=>{});
        check(spirit.Pending.All(p=>p.Id!=Production.SpiritId) && BetterStardewValley.AnnualLegendaryFeature.MachineAvailable(Game1.MasterPlayer,Production.SpiritId,reservedYear)
            && BetterStardewValley.AnnualLegendaryFeature.MachineAvailable(Game1.MasterPlayer,Production.SpiritId,reservedYear+1),"legacy Spirit plan without per-bait proof falls back and releases its reservation");
        Game1.year=oldYear;Game1.MasterPlayer.modData.Clear();
    }
}
